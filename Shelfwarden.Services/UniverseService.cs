namespace Shelfwarden.Services;

public class UniverseService(
    IUserContextService userContext,
    IRepository<Universe> universeRepository,
    IRepository<UniverseBook> universeBookRepository,
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
                OrderBy = q => q.OrderBy(ub => ub.TimelineOrder),
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
        string normalised = Normalise(trimmed);

        var clash = await universeRepository.FindOneAsync(new SearchOptions<Universe>
        {
            Query = u => u.NormalizedName == normalised,
            CancellationToken = cancellationToken,
        });
        if (clash is not null)
        {
            return Result.Conflict($"A universe called \"{trimmed}\" already exists.");
        }

        var inserted = await universeRepository.InsertAsync(new Universe
        {
            Name = trimmed,
            NormalizedName = normalised,
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
        string normalised = Normalise(trimmed);

        var clash = await universeRepository.FindOneAsync(new SearchOptions<Universe>
        {
            Query = u => u.NormalizedName == normalised && u.Id != id,
            CancellationToken = cancellationToken,
        });
        if (clash is not null)
        {
            return Result.Conflict($"Another universe is already called \"{trimmed}\".");
        }

        universe.Name = trimmed;
        universe.NormalizedName = normalised;
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
        // identical everywhere. A universe holds a handful of series and at most a few hundred
        // timeline rows, so loading them beats needing provider-specific bulk operations.
        var series = (await seriesRepository.FindAsync(new SearchOptions<Series>
        {
            Query = s => s.UniverseId == id,
            CancellationToken = cancellationToken,
        })).ToList();

        if (series.Count > 0)
        {
            foreach (var s in series)
            {
                s.UniverseId = null;
            }

            await seriesRepository.UpdateAsync(series);
        }

        // Timeline rows and universe reading orders belong to the universe and go with it.
        var memberships = (await universeBookRepository.FindAsync(new SearchOptions<UniverseBook>
        {
            Query = ub => ub.UniverseId == id,
            CancellationToken = cancellationToken,
        })).ToList();
        
        if (memberships.Count > 0)
        {
            await universeBookRepository.DeleteAsync(memberships);
        }

        var readingLists = (await readingListRepository.FindAsync(new SearchOptions<ReadingList>
        {
            Query = l => l.UniverseId == id,
            CancellationToken = cancellationToken,
        })).ToList();
        if (readingLists.Count > 0)
        {
            await readingListRepository.DeleteAsync(readingLists);
        }

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

        int maxOrder = await MaxTimelineOrderAsync(universeId, cancellationToken);

        var toInsert = newBookIds
            .Select((bid, idx) => new UniverseBook
            {
                UniverseId = universeId,
                BookId = bid,
                TimelineOrder = maxOrder + 1 + idx,
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

        await universeBookRepository.DeleteAsync(membership);
        await CompactTimelineOrderAsync(universeId, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> SetTimelineDateAsync(int universeId, int bookId, string? timelineDate, CancellationToken cancellationToken = default)
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

        membership.TimelineDate = NormaliseTimelineDate(timelineDate);
        await universeBookRepository.UpdateAsync(membership);
        return Result.Success();
    }

    public async Task<Result> ReorderTimelineAsync(
        int universeId,
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
            Query = ub => ub.UniverseId == universeId,
            CancellationToken = cancellationToken,
        })).ToList();

        var byId = rows.ToDictionary(ub => ub.Id);
        var toUpdate = new List<UniverseBook>();
        int order = 0;

        foreach (int rowId in orderedUniverseBookIds)
        {
            if (!byId.TryGetValue(rowId, out var row))
            {
                continue;
            }

            if (row.TimelineOrder != order)
            {
                row.TimelineOrder = order;
                toUpdate.Add(row);
            }
            order++;
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
            Include = q => q.Include(ub => ub.Universe),
            OrderBy = q => q.OrderBy(ub => ub.UniverseId),
            CancellationToken = cancellationToken,
        });

        return Result.Success(membership is null
            ? null
            : new UniverseMembershipDto(
                membership.UniverseId,
                membership.Universe.Name,
                membership.TimelineDate,
                membership.TimelineOrder));
    }

    public async Task<Result> SetBookMembershipAsync(
        int bookId,
        int? universeId,
        string? timelineDate,
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
                foreach (int affected in existing.Select(ub => ub.UniverseId).Distinct())
                {
                    await CompactTimelineOrderAsync(affected, cancellationToken);
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
            foreach (int affected in stale.Select(ub => ub.UniverseId).Distinct())
            {
                await CompactTimelineOrderAsync(affected, cancellationToken);
            }
        }

        var current = existing.FirstOrDefault(ub => ub.UniverseId == targetId);
        if (current is null)
        {
            int maxOrder = await MaxTimelineOrderAsync(targetId, cancellationToken);
            await universeBookRepository.InsertAsync(new UniverseBook
            {
                UniverseId = targetId,
                BookId = bookId,
                TimelineDate = NormaliseTimelineDate(timelineDate),
                TimelineOrder = maxOrder + 1,
            });
        }
        else
        {
            current.TimelineDate = NormaliseTimelineDate(timelineDate);
            await universeBookRepository.UpdateAsync(current);
        }

        return Result.Success();
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

        return Result.Success(new UniverseReadingListDto(inserted.Id, inserted.Name, inserted.Description, BookCount: 0));
    }

    private static string Normalise(string name) => name.ToSortTitle().ToLowerInvariant();

    private static string? NormaliseTimelineDate(string? timelineDate) =>
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

    private async Task<int> MaxTimelineOrderAsync(int universeId, CancellationToken cancellationToken) =>
        (await universeBookRepository.FindAsync(
                new SearchOptions<UniverseBook>
                {
                    Query = ub => ub.UniverseId == universeId,
                    CancellationToken = cancellationToken,
                },
                ub => ub.TimelineOrder))
            .DefaultIfEmpty(-1)
            .Max();

    /// <summary>
    /// Closes gaps left by removals so the order values stay a contiguous 0..n-1 run, which keeps
    /// the up/down buttons on the timeline symmetrical.
    /// </summary>
    private async Task CompactTimelineOrderAsync(int universeId, CancellationToken cancellationToken)
    {
        var rows = (await universeBookRepository.FindAsync(new SearchOptions<UniverseBook>
        {
            Query = ub => ub.UniverseId == universeId,
            OrderBy = q => q.OrderBy(ub => ub.TimelineOrder),
            CancellationToken = cancellationToken,
        })).ToList();

        var toUpdate = new List<UniverseBook>();
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].TimelineOrder != i)
            {
                rows[i].TimelineOrder = i;
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

    private async Task<IReadOnlyList<UniverseTimelineEntryDto>> LoadTimelineAsync(int universeId, CancellationToken cancellationToken)
    {
        var memberships = (await universeBookRepository.FindAsync(new SearchOptions<UniverseBook>
        {
            Query = ub => ub.UniverseId == universeId,
            OrderBy = q => q.OrderBy(ub => ub.TimelineOrder),
            CancellationToken = cancellationToken,
        })).ToList();

        if (memberships.Count == 0)
        {
            return [];
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

        return memberships
            .Where(ub => booksById.ContainsKey(ub.BookId))
            .Select(ub => new UniverseTimelineEntryDto(
                ub.Id,
                ub.TimelineOrder,
                ub.TimelineDate,
                BookProjections.ToListItem(booksById[ub.BookId], progress.GetValueOrDefault(ub.BookId, 0))))
            .ToList();
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
