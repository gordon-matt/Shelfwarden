namespace Shelfwarden.Services;

/// <summary>
/// Default <see cref="ISetupService"/>. Performs the bootstrap operations that need to
/// bypass the normal admin gate during a fresh install. Every mutating method first checks
/// the cached <c>setup.complete</c> flag and refuses to run when the wizard has already
/// finished, which is what keeps this from becoming a privilege-escalation surface.
/// </summary>
public class SetupService(
    ILogger<SetupService> logger,
    IUserContextService userContext,
    IAuthProviderService authProviderService,
    IServerSettingsService serverSettings,
    IRepository<ServerSetting> serverSettingRepository,
    IRepository<Shelf> shelfRepository,
    IRepository<ShelfFolder> folderRepository) : ISetupService
{
    public async Task<Result<SetupStatusDto>> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var settings = await serverSettings.GetAsync(cancellationToken);
        bool complete = settings.IsSuccess && settings.Value.SetupComplete;

        int shelfCount = await shelfRepository.CountAsync();

        bool requiresIdentityAdmin = authProviderService.Provider == AuthProvider.Identity
            && !userContext.IsAuthenticated();

        var dto = new SetupStatusDto(
            SetupComplete: complete,
            AuthProvider: authProviderService.Provider.ToString(),
            RequiresIdentityAdmin: requiresIdentityAdmin,
            CallerIsAdministrator: userContext.IsAdministrator(),
            CallerIsAuthenticated: userContext.IsAuthenticated(),
            ShelfCount: shelfCount);

        return Result.Success(dto);
    }

    public async Task<Result<ShelfDto>> CreateInitialShelfAsync(CreateShelfRequest request, CancellationToken cancellationToken = default)
    {
        if (await IsAlreadyCompleteAsync(cancellationToken))
        {
            return Result.Conflict("Setup is already complete; shelves must now be managed via /shelves.");
        }

        var folderPaths = NormalizeFolders(request.Folders);
        if (folderPaths.Count == 0)
        {
            return Result.Invalid(new ValidationError(nameof(request.Folders), "At least one folder is required."));
        }

        var existing = await shelfRepository.FindOneAsync(new SearchOptions<Shelf>
        {
            Query = s => s.Name == request.Name,
            CancellationToken = cancellationToken,
        });

        if (existing is not null)
        {
            return Result.Conflict($"A shelf named '{request.Name}' already exists.");
        }

        var shelf = await shelfRepository.InsertAsync(new Shelf
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
        });

        var folders = folderPaths
            .Select(p => new ShelfFolder { ShelfId = shelf.Id, Path = p })
            .ToList();

        await folderRepository.InsertAsync(folders);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("[Setup] Created initial shelf {ShelfId} '{Name}' with {FolderCount} folder(s)",
                shelf.Id, shelf.Name, folders.Count);
        }

        return Result.Success(new ShelfDto(
            shelf.Id,
            shelf.Name,
            shelf.Description,
            shelf.LastScannedAt,
            shelf.CreatedAt,
            BookCount: 0,
            Folders: folders.Select(f => new ShelfFolderDto(f.Id, f.Path)).ToList(),
            AllowedUserIds: [],
            AllowedRoleNames: []));
    }

    public async Task<Result> CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (await IsAlreadyCompleteAsync(cancellationToken))
        {
            return Result.Success();
        }

        // We deliberately bypass IServerSettingsService.SetAsync's admin check here because
        // an unauthenticated browser has just walked through the wizard and the alternative
        // is requiring the user to sign in *again* purely to flip a flag. Once this row is
        // written every subsequent call goes through the normal admin-gated service.
        var existing = await serverSettingRepository.FindOneAsync(new SearchOptions<ServerSetting>
        {
            Query = s => s.Key == Constants.ServerSettingKeys.SetupComplete,
            CancellationToken = cancellationToken,
        });

        if (existing is null)
        {
            await serverSettingRepository.InsertAsync(new ServerSetting
            {
                Key = Constants.ServerSettingKeys.SetupComplete,
                Value = bool.TrueString,
            });
        }
        else
        {
            existing.Value = bool.TrueString;
            await serverSettingRepository.UpdateAsync(existing);
        }

        // Force the next GetAsync to re-read so the cached snapshot picks up the change.
        ServerSettingsService.InvalidateCache();

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("[Setup] First-run wizard marked complete");
        }

        return Result.Success();
    }

    private async Task<bool> IsAlreadyCompleteAsync(CancellationToken cancellationToken)
    {
        var settings = await serverSettings.GetAsync(cancellationToken);
        return settings.IsSuccess && settings.Value.SetupComplete;
    }

    private static List<string> NormalizeFolders(IReadOnlyList<string> folders)
        => [.. folders
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim().TrimEnd('/', '\\'))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
}
