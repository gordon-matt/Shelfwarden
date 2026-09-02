namespace Shelfwarden.Services;

public class UniverseService(
    IUserContextService userContext,
    IRepository<Universe> universeRepository,
    IRepository<UniverseBook> universeBookRepository,
    IRepository<TimelineDate> timelineDateRepository,
    IRepository<Series> seriesRepository,
    IRepository<Book> bookRepository,
    IRepository<ReadingList> readingListRepository,
    IRepository<ReadingListItem> readingListItemRepository,
    IRepository<BookProgress> progressRepository) : IUniverseService
{
    public async Task<Result<IReadOnlyList<UniverseDto>>> ListAsync(string? query = null, CancellationToken cancellationToken = default)
    {
        var options = new SearchOptions<Universe>
        {
            OrderBy = q => q.OrderBy(u => u.NormalizedName),
            CancellationToken = cancellationToken,
        };

        if (!string.IsNullOrWhiteSpace(query))
        {
            string needle = query.Trim().ToLowerInvariant();
            options.Query = u => EF.Functions.Like(u.NormalizedName, $"%{needle}%");
        }

        var universes = (await universeRepository.FindAsync(options)).ToList();
        if (universes.Count == 0)
        {
            return Result.Success<IReadOnlyList<UniverseDto>>([]);
        }

        var ids = universes.Select(u => u.Id).ToList();

        // One projection per related table rather than per universe — the lists are small enough
        // that grouping in memory beats N round-trips.
        var memberships = (await universeBookRepository.FindAsync(
            new SearchOptions<UniverseBook>
            {
                Query = ub => ids.Contains(ub.UniverseId),
                OrderBy = q => q
                    .OrderBy(ub => ub.TimelineDateId == null)
                    .ThenBy(ub => ub.TimelineDate!.Order)
                    .ThenBy(ub => ub.Order),
                CancellationToken = cancellationToken,
            },
            ub => new { ub.UniverseId, ub.BookId, ub.Book.CoverImagePath })).ToList();

        var seriesCounts = (await seriesRepository.FindAsync(
                new SearchOptions<Series>
                {
                    Query = s => s.UniverseId != null && ids.Contains(s.UniverseId.Value),
                    CancellationToken = cancellationToken,
                },
                s => s.UniverseId))
            .Where(uid => uid.HasValue)
            .GroupBy(uid => uid!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var readingListCounts = (await readingListRepository.FindAsync(
                new SearchOptions<ReadingList>
                {
                    Query = l => l.UniverseId != null && ids.Contains(l.UniverseId.Value),
                    CancellationToken = cancellationToken,
                },
                l => l.UniverseId))
            .Where(uid => uid.HasValue)
            .GroupBy(uid => uid!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var booksByUniverse = memberships
            .GroupBy(m => m.UniverseId)
            .ToDictionary(g => g.Key, g => g.ToList());

        IReadOnlyList<UniverseDto> result = universes
            .Select(u =>
            {
                booksByUniverse.TryGetValue(u.Id, out var rows);
                rows ??= [];
                IReadOnlyList<SeriesCoverDto> covers = rows
                    .Take(4)
                    .Select(r => new SeriesCoverDto(r.BookId, r.CoverImagePath))
                    .ToList();

                return new UniverseDto(
                    u.Id,
                    u.Name,
                    u.Description,
                    seriesCounts.GetValueOrDefault(u.Id, 0),
                    rows.Count,
                    readingListCounts.GetValueOrDefault(u.Id, 0),
                    u.CreatedAt,
                    covers);
            })
            .ToList();

        return Result.Success(result);
    }

    public async Task<Result<IReadOnlyList<UniverseOptionDto>>> SearchAsync(
        string? query = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var options = new SearchOptions<Universe>
        {
            PageNumber = 1,
            PageSize = Math.Clamp(limit, 1, 500),
            OrderBy = q => q.OrderBy(u => u.NormalizedName),
            CancellationToken = cancellationToken,
        };

        if (!string.IsNullOrWhiteSpace(query))
        {
            string needle = query.Trim().ToLowerInvariant();
            options.Query = u => EF.Functions.Like(u.NormalizedName, $"%{needle}%");
        }

        IReadOnlyList<UniverseOptionDto> result = (await universeRepository.FindAsync(
            options,
            u => new UniverseOptionDto(u.Id, u.Name))).ToList();

        return Result.Success(result);
    }

    public async Task<Result<UniverseDetailDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var universe = await universeRepository.FindOneAsync(new SearchOptions<Universe>
        {
            Query = u => u.Id == id,
            CancellationToken = cancellationToken,
        });
        if (universe is null)
        {
            return Result.NotFound();
        }

        var series = await LoadSeriesAsync(id, cancellationToken);
        var timeline = await LoadTimelineAsync(id, cancellationToken);
        var readingLists = await LoadReadingListsAsync(id, cancellationToken);

        return Result.Success(new UniverseDetailDto(
            universe.Id,
            universe.Name,
            universe.Description,
            universe.CreatedAt,
            series,
            timeline,
            readingLists));
    }

    public async Task<Result<UniverseDto>> CreateAsync(CreateUniverseRequest request, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Invalid(new ValidationError(nameof(request.Name), "Name is required."));
        }

        string trimmed = request.Name.Trim();
        string normalized = Normalize(trimmed);

        var clash = await universeRepository.FindOneAsync(new SearchOptions<Universe>
        {
            Query = u => u.NormalizedName == normalized,
            CancellationToken = cancellationToken,
        });
        if (clash is not null)
        {
            return Result.Conflict($"A universe called \"{trimmed}\" already exists.");
        }

        var inserted = await universeRepository.InsertAsync(new Universe
        {
            Name = trimmed,
            NormalizedName = normalized,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            CreatedAt = DateTime.UtcNow,
        });

        return Result.Success(Map(inserted, seriesCount: 0, bookCount: 0, readingListCount: 0, covers: []));
    }

    public async Task<Result<UniverseDto>> UpdateAsync(int id, UpdateUniverseRequest request, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Invalid(new ValidationError(nameof(request.Name), "Name is required."));
        }

        var universe = await universeRepository.FindOneAsync(new SearchOptions<Universe>
        {
            Query = u => u.Id == id,
            CancellationToken = cancellationToken,
        });
        if (universe is null)
        {
            return Result.NotFound();
        }

        string trimmed = request.Name.Trim();
        string normalized = Normalize(trimmed);

        var clash = await universeRepository.FindOneAsync(new SearchOptions<Universe>
        {
            Query = u => u.NormalizedName == normalized && u.Id != id,
            CancellationToken = cancellationToken,
        });
        if (clash is not null)
        {
            return Result.Conflict($"Another universe is already called \"{trimmed}\".");
        }

        universe.Name = trimmed;
        universe.NormalizedName = normalized;
        universe.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        var updated = await universeRepository.UpdateAsync(universe);

        int seriesCount = await seriesRepository.CountAsync(s => s.UniverseId == id);
        int bookCount = await universeBookRepository.CountAsync(ub => ub.UniverseId == id);
        int readingListCount = await readingListRepository.CountAsync(l => l.UniverseId == id);

        return Result.Success(Map(updated, seriesCount, bookCount, readingListCount, covers: []));
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var universe = await universeRepository.FindOneAsync(new SearchOptions<Universe>
        {
            Query = u => u.Id == id,
            CancellationToken = cancellationToken,
        });
        if (universe is null)
        {
            return Result.NotFound();
        }

        // Detach series rather than leaning on the provider's SET NULL, so the outcome is
        // identical everywhere. Timeline rows and universe reading orders belong to the universe
        // and go with it.
        await seriesRepository.UpdateAsync(
            s => s.UniverseId == id,
            setters => setters.SetProperty(s => s.UniverseId, (int?)null));

        await universeBookRepository.DeleteAsync(ub => ub.UniverseId == id);
        await timelineDateRepository.DeleteAsync(td => td.UniverseId == id);
        await readingListRepository.DeleteAsync(l => l.UniverseId == id);

        await universeRepository.DeleteAsync(universe);
        return Result.Success();
    }

    public async Task<Result<int>> AddBooksAsync(int universeId, IReadOnlyCollection<int> bookIds, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var distinctBookIds = bookIds.Where(bid => bid > 0).Distinct().ToList();
        if (distinctBookIds.Count == 0)
        {
            return Result.Success(0);
        }

        if (!await UniverseExistsAsync(universeId, cancellationToken))
        {
            return Result.NotFound("Universe not found.");
        }

        var existingBookIds = (await bookRepository.FindAsync(
            new SearchOptions<Book>
            {
                Query = b => distinctBookIds.Contains(b.Id),
                CancellationToken = cancellationToken,
            },
            b => b.Id)).ToHashSet();

        var alreadyLinked = (await universeBookRepository.FindAsync(
            new SearchOptions<UniverseBook>
            {
                Query = ub => ub.UniverseId == universeId && distinctBookIds.Contains(ub.BookId),
                CancellationToken = cancellationToken,
            },
            ub => ub.BookId)).ToHashSet();

        var newBookIds = distinctBookIds
            .Where(bid => existingBookIds.Contains(bid) && !alreadyLinked.Contains(bid))
            .ToList();

        if (newBookIds.Count == 0)
        {
            return Result.Success(0);
        }

        // Books arrive unscheduled: which point on the timeline they belong to is a judgement
        // call, and guessing one here would just make every add something to undo.
        int nextOrder = await NextGroupOrderAsync(universeId, null, cancellationToken);

        var toInsert = newBookIds
            .Select((bid, idx) => new UniverseBook
            {
                UniverseId = universeId,
                BookId = bid,
                TimelineDateId = null,
                Order = nextOrder + idx,
            })
            .ToList();

        await universeBookRepository.InsertAsync(toInsert);
        return Result.Success(toInsert.Count);
    }

    public async Task<Result> RemoveBookAsync(int universeId, int bookId, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var membership = await universeBookRepository.FindOneAsync(new SearchOptions<UniverseBook>
        {
            Query = ub => ub.UniverseId == universeId && ub.BookId == bookId,
            CancellationToken = cancellationToken,
        });
        if (membership is null)
        {
            return Result.NotFound();
        }

        int? dateId = membership.TimelineDateId;
        await universeBookRepository.DeleteAsync(membership);
        await CompactGroupOrderAsync(universeId, dateId, cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<UniverseTimelineDateDto>>> ListTimelineDatesAsync(
        int universeId,
        CancellationToken cancellationToken = default)
    {
        var dates = (await timelineDateRepository.FindAsync(new SearchOptions<TimelineDate>
        {
            Query = td => td.UniverseId == universeId,
            OrderBy = q => q.OrderBy(td => td.Order),
            CancellationToken = cancellationToken,
        })).ToList();

        if (dates.Count == 0)
        {
            return Result.Success<IReadOnlyList<UniverseTimelineDateDto>>([]);
        }

        var counts = await BookCountsByDateAsync(universeId, cancellationToken);

        IReadOnlyList<UniverseTimelineDateDto> result = dates
            .Select(td => new UniverseTimelineDateDto(td.Id, td.Date, td.Order, counts.GetValueOrDefault(td.Id, 0)))
            .ToList();

        return Result.Success(result);
    }

    public async Task<Result<UniverseTimelineDateDto>> CreateTimelineDateAsync(
        int universeId,
        string date,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        string? trimmed = NormalizeTimelineDate(date);
        if (trimmed is null)
        {
            return Result.Invalid(new ValidationError(nameof(date), "Date is required."));
        }

        if (!await UniverseExistsAsync(universeId, cancellationToken))
        {
            return Result.NotFound("Universe not found.");
        }

        if (await FindDateByTextAsync(universeId, trimmed, cancellationToken) is not null)
        {
            return Result.Conflict($"This universe already has a timeline date called \"{trimmed}\".");
        }

        int maxOrder = await MaxDateOrderAsync(universeId, cancellationToken);
        var inserted = await timelineDateRepository.InsertAsync(new TimelineDate
        {
            UniverseId = universeId,
            Date = trimmed,
            Order = maxOrder + 1,
        });

        return Result.Success(new UniverseTimelineDateDto(inserted.Id, inserted.Date, inserted.Order, 0));
    }

    public async Task<Result<UniverseTimelineDateDto>> RenameTimelineDateAsync(
        int timelineDateId,
        string date,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        string? trimmed = NormalizeTimelineDate(date);
        if (trimmed is null)
        {
            return Result.Invalid(new ValidationError(nameof(date), "Date is required."));
        }

        var existing = await timelineDateRepository.FindOneAsync(new SearchOptions<TimelineDate>
        {
            Query = td => td.Id == timelineDateId,
            CancellationToken = cancellationToken,
        });
        if (existing is null)
        {
            return Result.NotFound();
        }

        var clash = await FindDateByTextAsync(existing.UniverseId, trimmed, cancellationToken);
        if (clash is not null && clash.Id != timelineDateId)
        {
            return Result.Conflict($"This universe already has a timeline date called \"{trimmed}\".");
        }

        existing.Date = trimmed;
        var updated = await timelineDateRepository.UpdateAsync(existing);

        int bookCount = await universeBookRepository.CountAsync(ub => ub.TimelineDateId == timelineDateId);
        return Result.Success(new UniverseTimelineDateDto(updated.Id, updated.Date, updated.Order, bookCount));
    }

    public async Task<Result> DeleteTimelineDateAsync(int timelineDateId, CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var existing = await timelineDateRepository.FindOneAsync(new SearchOptions<TimelineDate>
        {
            Query = td => td.Id == timelineDateId,
            CancellationToken = cancellationToken,
        });
        if (existing is null)
        {
            return Result.NotFound();
        }

        // Un-schedule the books explicitly rather than relying on the provider's SET NULL, so the
        // outcome — and their position in the unscheduled bucket — is the same everywhere.
        int unscheduledFrom = await NextGroupOrderAsync(existing.UniverseId, null, cancellationToken);
        var orphaned = (await universeBookRepository.FindAsync(new SearchOptions<UniverseBook>
        {
            Query = ub => ub.TimelineDateId == timelineDateId,
            OrderBy = q => q.OrderBy(ub => ub.Order),
            CancellationToken = cancellationToken,
        })).ToList();

        for (int i = 0; i < orphaned.Count; i++)
        {
            orphaned[i].TimelineDateId = null;
            orphaned[i].TimelineDate = null;
            orphaned[i].Order = unscheduledFrom + i;
        }

        if (orphaned.Count > 0)
        {
            await universeBookRepository.UpdateAsync(orphaned);
        }

        await timelineDateRepository.DeleteAsync(existing);
        await CompactDateOrderAsync(existing.UniverseId, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ReorderTimelineDatesAsync(
        int universeId,
        IReadOnlyList<int> orderedTimelineDateIds,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        if (!await UniverseExistsAsync(universeId, cancellationToken))
        {
            return Result.NotFound();
        }

        var dates = (await timelineDateRepository.FindAsync(new SearchOptions<TimelineDate>
        {
            Query = td => td.UniverseId == universeId,
            OrderBy = q => q.OrderBy(td => td.Order),
            CancellationToken = cancellationToken,
        })).ToList();

        var ordered = ApplyRequestedOrder(dates, td => td.Id, orderedTimelineDateIds);

        var toUpdate = new List<TimelineDate>();
        for (int i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Order != i)
            {
                ordered[i].Order = i;
                toUpdate.Add(ordered[i]);
            }
        }

        if (toUpdate.Count > 0)
        {
            await timelineDateRepository.UpdateAsync(toUpdate);
        }

        return Result.Success();
    }

    public async Task<Result> SetBookTimelineDateAsync(
        int universeId,
        int bookId,
        int? timelineDateId,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var membership = await universeBookRepository.FindOneAsync(new SearchOptions<UniverseBook>
        {
            Query = ub => ub.UniverseId == universeId && ub.BookId == bookId,
            CancellationToken = cancellationToken,
        });
        if (membership is null)
        {
            return Result.NotFound();
        }

        if (membership.TimelineDateId == timelineDateId)
        {
            return Result.Success();
        }

        if (timelineDateId is int targetDateId && !await DateBelongsToUniverseAsync(targetDateId, universeId, cancellationToken))
        {
            return Result.NotFound("Timeline date not found.");
        }

        int? previousDateId = membership.TimelineDateId;
        membership.TimelineDateId = timelineDateId;
        membership.TimelineDate = null;
        membership.Order = await NextGroupOrderAsync(universeId, timelineDateId, cancellationToken);
        await universeBookRepository.UpdateAsync(membership);

        await CompactGroupOrderAsync(universeId, previousDateId, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ReorderTimelineGroupAsync(
        int universeId,
        int? timelineDateId,
        IReadOnlyList<int> orderedUniverseBookIds,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        if (!await UniverseExistsAsync(universeId, cancellationToken))
        {
            return Result.NotFound();
        }

        var rows = (await universeBookRepository.FindAsync(new SearchOptions<UniverseBook>
        {
            Query = ub => ub.UniverseId == universeId && ub.TimelineDateId == timelineDateId,
            OrderBy = q => q.OrderBy(ub => ub.Order),
            CancellationToken = cancellationToken,
        })).ToList();

        var ordered = ApplyRequestedOrder(rows, ub => ub.Id, orderedUniverseBookIds);

        var toUpdate = new List<UniverseBook>();
        for (int i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Order != i)
            {
                ordered[i].Order = i;
                toUpdate.Add(ordered[i]);
            }
        }

        if (toUpdate.Count > 0)
        {
            await universeBookRepository.UpdateAsync(toUpdate);
        }

        return Result.Success();
    }

    public async Task<Result<UniverseMembershipDto?>> GetBookMembershipAsync(int bookId, CancellationToken cancellationToken = default)
    {
        var membership = await universeBookRepository.FindOneAsync(new SearchOptions<UniverseBook>
        {
            Query = ub => ub.BookId == bookId,
            Include = q => q.Include(ub => ub.Universe).Include(ub => ub.TimelineDate),
            OrderBy = q => q.OrderBy(ub => ub.UniverseId),
            CancellationToken = cancellationToken,
        });

        return Result.Success(membership is null
            ? null
            : new UniverseMembershipDto(
                membership.UniverseId,
                membership.Universe.Name,
                membership.TimelineDateId,
                membership.TimelineDate?.Date));
    }

    public async Task<Result> SetBookMembershipAsync(
        int bookId,
        int? universeId,
        int? timelineDateId,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var existing = (await universeBookRepository.FindAsync(new SearchOptions<UniverseBook>
        {
            Query = ub => ub.BookId == bookId,
            CancellationToken = cancellationToken,
        })).ToList();

        if (universeId is not int targetId)
        {
            if (existing.Count > 0)
            {
                await universeBookRepository.DeleteAsync(existing);
                foreach (var affected in existing.Select(ub => (ub.UniverseId, ub.TimelineDateId)).Distinct())
                {
                    await CompactGroupOrderAsync(affected.UniverseId, affected.TimelineDateId, cancellationToken);
                }
            }

            return Result.Success();
        }

        if (!await UniverseExistsAsync(targetId, cancellationToken))
        {
            return Result.NotFound("Universe not found.");
        }

        var book = await bookRepository.FindOneAsync(new SearchOptions<Book>
        {
            Query = b => b.Id == bookId,
            CancellationToken = cancellationToken,
        });
        if (book is null)
        {
            return Result.NotFound("Book not found.");
        }

        // The edit page only ever surfaces a single membership, so moving the book means dropping
        // whatever it was in before.
        var stale = existing.Where(ub => ub.UniverseId != targetId).ToList();
        if (stale.Count > 0)
        {
            await universeBookRepository.DeleteAsync(stale);
            foreach (var affected in stale.Select(ub => (ub.UniverseId, ub.TimelineDateId)).Distinct())
            {
                await CompactGroupOrderAsync(affected.UniverseId, affected.TimelineDateId, cancellationToken);
            }
        }

        if (timelineDateId is int dateId && !await DateBelongsToUniverseAsync(dateId, targetId, cancellationToken))
        {
            return Result.NotFound("Timeline date not found.");
        }

        var current = existing.FirstOrDefault(ub => ub.UniverseId == targetId);
        if (current is null)
        {
            await universeBookRepository.InsertAsync(new UniverseBook
            {
                UniverseId = targetId,
                BookId = bookId,
                TimelineDateId = timelineDateId,
                Order = await NextGroupOrderAsync(targetId, timelineDateId, cancellationToken),
            });

            return Result.Success();
        }

        return await SetBookTimelineDateAsync(targetId, bookId, timelineDateId, cancellationToken);
    }

    public async Task<Result> SetSeriesUniverseAsync(
        int seriesId,
        int? universeId,
        bool addSeriesBooks = false,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        var series = await seriesRepository.FindOneAsync(new SearchOptions<Series>
        {
            Query = s => s.Id == seriesId,
            CancellationToken = cancellationToken,
        });
        if (series is null)
        {
            return Result.NotFound("Series not found.");
        }

        if (universeId is int targetId && !await UniverseExistsAsync(targetId, cancellationToken))
        {
            return Result.NotFound("Universe not found.");
        }

        series.UniverseId = universeId;
        await seriesRepository.UpdateAsync(series);

        // Book membership stays explicit — we only bulk-add when the caller asked for it, and even
        // then the two relationships drift independently from here on.
        if (universeId is int addTo && addSeriesBooks)
        {
            var bookIds = (await bookRepository.FindAsync(
                new SearchOptions<Book>
                {
                    Query = b => b.SeriesId == seriesId,
                    OrderBy = q => q.OrderBy(b => b.NumberInSeries).ThenBy(b => b.SortTitle ?? b.Title),
                    CancellationToken = cancellationToken,
                },
                b => b.Id)).ToList();

            if (bookIds.Count > 0)
            {
                var addResult = await AddBooksAsync(addTo, bookIds, cancellationToken);
                if (!addResult.IsSuccess)
                {
                    return Result.Error("The series was saved, but its books could not be added to the universe.");
                }
            }
        }

        return Result.Success();
    }

    public async Task<Result<UniverseReadingListDto>> CreateReadingListAsync(
        int universeId,
        CreateReadingListRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!userContext.IsAdministrator())
        {
            return Result.Forbidden();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Invalid(new ValidationError(nameof(request.Name), "Name is required."));
        }

        if (!await UniverseExistsAsync(universeId, cancellationToken))
        {
            return Result.NotFound("Universe not found.");
        }

        string trimmed = request.Name.Trim();
        var clash = await readingListRepository.FindOneAsync(new SearchOptions<ReadingList>
        {
            Query = l => l.UniverseId == universeId && l.Name == trimmed,
            CancellationToken = cancellationToken,
        });
        if (clash is not null)
        {
            return Result.Conflict($"This universe already has a reading order called \"{trimmed}\".");
        }

        // Owned by the global user so every reader sees the universe's reading orders, the same
        // way global collections work. Only administrators can change them.
        var inserted = await readingListRepository.InsertAsync(new ReadingList
        {
            Name = trimmed,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            OwnerUserId = Constants.GlobalUserId,
            UniverseId = universeId,
            CreatedAt = DateTime.UtcNow,
        });

        // A reading order is a re-ordering of the universe, not a subset to be assembled book by
        // book, so it starts as the full timeline. Removing the odd book is easier than finding
        // dozens of them one at a time.
        var timelineBookIds = (await universeBookRepository.FindAsync(
            new SearchOptions<UniverseBook>
            {
                Query = ub => ub.UniverseId == universeId,
                OrderBy = q => q
                    .OrderBy(ub => ub.TimelineDateId == null)
                    .ThenBy(ub => ub.TimelineDate!.Order)
                    .ThenBy(ub => ub.Order),
                CancellationToken = cancellationToken,
            },
            ub => ub.BookId)).ToList();

        if (timelineBookIds.Count > 0)
        {
            await readingListItemRepository.InsertAsync(timelineBookIds
                .Select((bookId, index) => new ReadingListItem
                {
                    ReadingListId = inserted.Id,
                    BookId = bookId,
                    Position = index,
                })
                .ToList());
        }

        return Result.Success(new UniverseReadingListDto(
            inserted.Id, inserted.Name, inserted.Description, timelineBookIds.Count));
    }

    private static string Normalize(string name) => name.ToSortTitle().ToLowerInvariant();

    /// <summary>Buckets entries by their date, in within-date order. Unscheduled ones are left out.</summary>
    private static Dictionary<int, IReadOnlyList<UniverseTimelineEntryDto>> GroupByDate(
        IEnumerable<UniverseTimelineEntryDto> entries) =>
        entries
            .Where(e => e.TimelineDateId.HasValue)
            .GroupBy(e => e.TimelineDateId!.Value)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<UniverseTimelineEntryDto>)g.OrderBy(e => e.Order).ToList());

    private static string? NormalizeTimelineDate(string? timelineDate) =>
        string.IsNullOrWhiteSpace(timelineDate) ? null : timelineDate.Trim();

    private static UniverseDto Map(
        Universe u,
        int seriesCount,
        int bookCount,
        int readingListCount,
        IReadOnlyList<SeriesCoverDto> covers) =>
        new(u.Id, u.Name, u.Description, seriesCount, bookCount, readingListCount, u.CreatedAt, covers);

    private async Task<bool> UniverseExistsAsync(int universeId, CancellationToken cancellationToken) =>
        await universeRepository.FindOneAsync(new SearchOptions<Universe>
        {
            Query = u => u.Id == universeId,
            CancellationToken = cancellationToken,
        }) is not null;

    private async Task<TimelineDate?> FindDateByTextAsync(int universeId, string date, CancellationToken cancellationToken) =>
        (await timelineDateRepository.FindAsync(new SearchOptions<TimelineDate>
        {
            Query = td => td.UniverseId == universeId,
            CancellationToken = cancellationToken,
        })).FirstOrDefault(td => string.Equals(td.Date, date, StringComparison.OrdinalIgnoreCase));

    private async Task<bool> DateBelongsToUniverseAsync(int timelineDateId, int universeId, CancellationToken cancellationToken) =>
        await timelineDateRepository.FindOneAsync(new SearchOptions<TimelineDate>
        {
            Query = td => td.Id == timelineDateId && td.UniverseId == universeId,
            CancellationToken = cancellationToken,
        }) is not null;

    private async Task<Dictionary<int, int>> BookCountsByDateAsync(int universeId, CancellationToken cancellationToken) =>
        (await universeBookRepository.FindAsync(
                new SearchOptions<UniverseBook>
                {
                    Query = ub => ub.UniverseId == universeId && ub.TimelineDateId != null,
                    CancellationToken = cancellationToken,
                },
                ub => ub.TimelineDateId))
            .Where(id => id.HasValue)
            .GroupBy(id => id!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

    private async Task<int> MaxDateOrderAsync(int universeId, CancellationToken cancellationToken) =>
        (await timelineDateRepository.FindAsync(
                new SearchOptions<TimelineDate>
                {
                    Query = td => td.UniverseId == universeId,
                    CancellationToken = cancellationToken,
                },
                td => td.Order))
            .DefaultIfEmpty(-1)
            .Max();

    /// <summary>Position a book would take if it joined <paramref name="timelineDateId"/> right now.</summary>
    private async Task<int> NextGroupOrderAsync(int universeId, int? timelineDateId, CancellationToken cancellationToken) =>
        (await universeBookRepository.FindAsync(
                new SearchOptions<UniverseBook>
                {
                    Query = ub => ub.UniverseId == universeId && ub.TimelineDateId == timelineDateId,
                    CancellationToken = cancellationToken,
                },
                ub => ub.Order))
            .DefaultIfEmpty(-1)
            .Max() + 1;

    /// <summary>
    /// Returns <paramref name="items"/> in the order the caller asked for, with anything they
    /// didn't mention keeping its relative place at the end and unknown ids ignored.
    /// </summary>
    private static List<T> ApplyRequestedOrder<T>(
        IReadOnlyList<T> items,
        Func<T, int> idSelector,
        IReadOnlyList<int> requestedIds)
    {
        var byId = items.ToDictionary(idSelector);
        var seen = new HashSet<int>();
        var ordered = new List<T>(items.Count);

        foreach (int id in requestedIds)
        {
            if (byId.TryGetValue(id, out var item) && seen.Add(id))
            {
                ordered.Add(item);
            }
        }

        foreach (var item in items)
        {
            if (seen.Add(idSelector(item)))
            {
                ordered.Add(item);
            }
        }

        return ordered;
    }

    /// <summary>Closes gaps in the universe's date order so it stays a contiguous 0..n-1 run.</summary>
    private async Task CompactDateOrderAsync(int universeId, CancellationToken cancellationToken)
    {
        var dates = (await timelineDateRepository.FindAsync(new SearchOptions<TimelineDate>
        {
            Query = td => td.UniverseId == universeId,
            OrderBy = q => q.OrderBy(td => td.Order),
            CancellationToken = cancellationToken,
        })).ToList();

        var toUpdate = new List<TimelineDate>();
        for (int i = 0; i < dates.Count; i++)
        {
            if (dates[i].Order != i)
            {
                dates[i].Order = i;
                toUpdate.Add(dates[i]);
            }
        }

        if (toUpdate.Count > 0)
        {
            await timelineDateRepository.UpdateAsync(toUpdate);
        }
    }

    /// <summary>Same, for the books sharing one date (or the unscheduled bucket).</summary>
    private async Task CompactGroupOrderAsync(int universeId, int? timelineDateId, CancellationToken cancellationToken)
    {
        var rows = (await universeBookRepository.FindAsync(new SearchOptions<UniverseBook>
        {
            Query = ub => ub.UniverseId == universeId && ub.TimelineDateId == timelineDateId,
            OrderBy = q => q.OrderBy(ub => ub.Order),
            CancellationToken = cancellationToken,
        })).ToList();

        var toUpdate = new List<UniverseBook>();
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Order != i)
            {
                rows[i].Order = i;
                toUpdate.Add(rows[i]);
            }
        }

        if (toUpdate.Count > 0)
        {
            await universeBookRepository.UpdateAsync(toUpdate);
        }
    }

    private async Task<IReadOnlyList<UniverseSeriesDto>> LoadSeriesAsync(int universeId, CancellationToken cancellationToken)
    {
        var series = (await seriesRepository.FindAsync(new SearchOptions<Series>
        {
            Query = s => s.UniverseId == universeId,
            OrderBy = q => q.OrderBy(s => s.NormalizedName),
            CancellationToken = cancellationToken,
        })).ToList();

        if (series.Count == 0)
        {
            return [];
        }

        var seriesIds = series.Select(s => s.Id).ToList();
        var bookRows = (await bookRepository.FindAsync(
            new SearchOptions<Book>
            {
                Query = b => b.SeriesId != null && seriesIds.Contains(b.SeriesId.Value),
                OrderBy = q => q.OrderBy(b => b.NumberInSeries).ThenBy(b => b.SortTitle ?? b.Title),
                CancellationToken = cancellationToken,
            },
            b => new { b.Id, b.SeriesId, b.CoverImagePath })).ToList();

        var bySeries = bookRows
            .Where(b => b.SeriesId.HasValue)
            .GroupBy(b => b.SeriesId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        return series
            .Select(s =>
            {
                bySeries.TryGetValue(s.Id, out var rows);
                rows ??= [];
                IReadOnlyList<SeriesCoverDto> covers = rows
                    .Take(4)
                    .Select(r => new SeriesCoverDto(r.Id, r.CoverImagePath))
                    .ToList();
                return new UniverseSeriesDto(s.Id, s.Name, rows.Count, covers);
            })
            .ToList();
    }

    private async Task<UniverseTimelineDto> LoadTimelineAsync(int universeId, CancellationToken cancellationToken)
    {
        var dates = (await timelineDateRepository.FindAsync(new SearchOptions<TimelineDate>
        {
            Query = td => td.UniverseId == universeId,
            OrderBy = q => q.OrderBy(td => td.Order),
            CancellationToken = cancellationToken,
        })).ToList();

        var memberships = (await universeBookRepository.FindAsync(new SearchOptions<UniverseBook>
        {
            Query = ub => ub.UniverseId == universeId,
            Include = q => q.Include(ub => ub.TimelineDate),
            OrderBy = q => q
                .OrderBy(ub => ub.TimelineDateId == null)
                .ThenBy(ub => ub.TimelineDate!.Order)
                .ThenBy(ub => ub.Order),
            CancellationToken = cancellationToken,
        })).ToList();

        if (memberships.Count == 0)
        {
            IReadOnlyList<UniverseTimelineDateDto> emptyDates = dates
                .Select(td => new UniverseTimelineDateDto(td.Id, td.Date, td.Order, 0))
                .ToList();

            return new UniverseTimelineDto(emptyDates, [], [], []);
        }

        var bookIds = memberships.Select(ub => ub.BookId).ToList();
        var books = (await bookRepository.FindAsync(new SearchOptions<Book>
        {
            Query = b => bookIds.Contains(b.Id),
            Include = q => q.Include(b => b.Series).Include(b => b.BookAuthors).ThenInclude(ba => ba.Author),
            SplitQuery = true,
            CancellationToken = cancellationToken,
        })).ToList();

        var booksById = books.ToDictionary(b => b.Id);
        var progress = await BookProjections.LoadProgressPercentagesAsync(
            progressRepository, userContext.GetCurrentUserId(), bookIds, cancellationToken);

        var entries = memberships
            .Where(ub => booksById.ContainsKey(ub.BookId))
            .Select(ub => new UniverseTimelineEntryDto(
                ub.Id,
                ub.TimelineDateId,
                ub.Order,
                ub.TimelineDate?.Date,
                BookProjections.ToListItem(booksById[ub.BookId], progress.GetValueOrDefault(ub.BookId, 0))))
            .ToList();

        var entriesByDate = GroupByDate(entries);

        IReadOnlyList<UniverseTimelineDateDto> dateDtos = dates
            .Select(td => new UniverseTimelineDateDto(
                td.Id,
                td.Date,
                td.Order,
                entriesByDate.TryGetValue(td.Id, out var onDate) ? onDate.Count : 0))
            .ToList();

        // One column per date, in timeline order, plus a trailing "unscheduled" column that only
        // appears while something still needs placing.
        var groups = dates
            .Select(td => new UniverseTimelineGroupDto(
                td.Id,
                td.Date,
                entriesByDate.GetValueOrDefault(td.Id, [])))
            .ToList();

        var unscheduled = entries.Where(e => e.TimelineDateId is null).OrderBy(e => e.Order).ToList();
        if (unscheduled.Count > 0)
        {
            groups.Add(new UniverseTimelineGroupDto(null, "Unscheduled", unscheduled));
        }

        // One lane per series, plus a single shared lane for books with no series at all — that's
        // what a plain GroupBy on a nullable key already gives us. Each lane then gets one cell
        // per column so the view can render it as a straight matrix row.
        var rows = entries
            .GroupBy(e => booksById[e.Book.Id].SeriesId)
            .Select(g =>
            {
                var laneEntries = g.ToList();
                var laneByDate = GroupByDate(laneEntries);
                var laneUnscheduled = laneEntries.Where(e => e.TimelineDateId is null).OrderBy(e => e.Order).ToList();

                IReadOnlyList<UniverseTimelineCellDto> cells = groups
                    .Select(col => new UniverseTimelineCellDto(
                        col.TimelineDateId,
                        col.TimelineDateId is int dateId
                            ? laneByDate.GetValueOrDefault(dateId, [])
                            : laneUnscheduled))
                    .ToList();

                return new UniverseTimelineRowDto(
                    g.Key,
                    g.Key is null ? "Standalone" : g.First().Book.SeriesName ?? "Standalone",
                    cells);
            })
            // Series lanes first, in the order they debut on the timeline; standalone books last.
            .OrderBy(r => r.SeriesId is null)
            .ThenBy(r => r.Cells.TakeWhile(c => c.Entries.Count == 0).Count())
            .ThenBy(r => r.Label)
            .ToList();

        return new UniverseTimelineDto(dateDtos, groups, rows, entries);
    }

    private async Task<IReadOnlyList<UniverseReadingListDto>> LoadReadingListsAsync(int universeId, CancellationToken cancellationToken)
    {
        var lists = (await readingListRepository.FindAsync(new SearchOptions<ReadingList>
        {
            Query = l => l.UniverseId == universeId,
            OrderBy = q => q.OrderBy(l => l.Name),
            CancellationToken = cancellationToken,
        })).ToList();

        if (lists.Count == 0)
        {
            return [];
        }

        var listIds = lists.Select(l => l.Id).ToList();
        var counts = (await readingListItemRepository.FindAsync(
                new SearchOptions<ReadingListItem>
                {
                    Query = i => listIds.Contains(i.ReadingListId),
                    CancellationToken = cancellationToken,
                },
                i => i.ReadingListId))
            .GroupBy(lid => lid)
            .ToDictionary(g => g.Key, g => g.Count());

        return lists
            .Select(l => new UniverseReadingListDto(l.Id, l.Name, l.Description, counts.GetValueOrDefault(l.Id, 0)))
            .ToList();
    }
}
