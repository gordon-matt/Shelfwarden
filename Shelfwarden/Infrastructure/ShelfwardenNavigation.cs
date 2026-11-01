using Heroicsoft.NetForge.Abstractions.Navigation;

namespace Shelfwarden.Infrastructure;

/// <summary>
/// The sidebar content: the browse views with their counts, the next few reading lists, the shelves,
/// and the administration links. Scoped to the circuit.
/// </summary>
public sealed class ShelfwardenNavigation(
    IShelfService shelfService,
    ICollectionService collectionService,
    IReadingListService readingListService,
    IUniverseService universeService,
    ILogger<ShelfwardenNavigation> logger) : IShellNavigation
{
    private const int MAX_READING_NEXT = 5;

    public string? SearchUrlFormat => "/books?q={0}";

    public async Task<IReadOnlyList<ForgeNavSection>> GetSectionsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ShelfDto>? shelves = null;
        IReadOnlyList<CollectionDto>? collections = null;
        IReadOnlyList<ReadingListDto>? readingLists = null;
        IReadOnlyList<UniverseDto>? universes = null;

        try
        {
            var shelvesTask = shelfService.GetAllAsync();
            var collectionsTask = collectionService.ListAsync();
            var listsTask = readingListService.ListAsync();
            var universesTask = universeService.ListAsync();
            await Task.WhenAll(shelvesTask, collectionsTask, listsTask, universesTask);

            if (shelvesTask.Result.IsSuccess)
            {
                shelves = shelvesTask.Result.Value;
            }

            if (collectionsTask.Result.IsSuccess)
            {
                collections = collectionsTask.Result.Value;
            }

            if (listsTask.Result.IsSuccess)
            {
                readingLists = listsTask.Result.Value;
            }

            if (universesTask.Result.IsSuccess)
            {
                universes = universesTask.Result.Value;
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Anonymous browsing leaves the sidebar at the fixed links.
            logger.LogDebug(ex, "Sidebar data could not be loaded");
        }

        return BuildSections(shelves, collections, readingLists, universes);
    }

    private static List<ForgeNavSection> BuildSections(
        IReadOnlyList<ShelfDto>? shelves,
        IReadOnlyList<CollectionDto>? collections,
        IReadOnlyList<ReadingListDto>? readingLists,
        IReadOnlyList<UniverseDto>? universes)
    {
        var sections = new List<ForgeNavSection>
        {
            new()
            {
                Heading = "Browse",
                Items =
                [
                    new() { Title = "Home", Href = "/", Icon = "bi-house-door", MatchAll = true },
                    new() { Title = "All books", Href = "/books", Icon = "bi-book" },
                    new() { Title = "Shelves", Href = "/shelves", Icon = "bi-collection" },
                    new() { Title = "Authors", Href = "/authors", Icon = "bi-person-lines-fill" },
                    new() { Title = "Series", Href = "/series", Icon = "bi-collection-fill" },
                    new() { Title = "Universes", Href = "/universes", Icon = "bi-stars", Badge = FormatCount(universes?.Count) },
                    new() { Title = "Collections", Href = "/collections", Icon = "bi-bookshelf", Badge = FormatCount(collections?.Count) },
                    new() { Title = "Reading lists", Href = "/reading-lists", Icon = "bi-list-ol", Badge = FormatCount(readingLists?.Count) },
                ],
            },
        };

        if (readingLists is { Count: > 0 })
        {
            sections.Add(new ForgeNavSection
            {
                Heading = "Reading next",
                Items =
                [
                    .. readingLists.Take(MAX_READING_NEXT).Select(list => new ForgeNavItem
                    {
                        Title = list.Name,
                        Href = $"/reading-lists/{list.Id}",
                        Icon = "bi-list-ol",
                    }),
                ],
            });
        }

        if (shelves is { Count: > 0 })
        {
            sections.Add(new ForgeNavSection
            {
                Heading = "Your shelves",
                Items =
                [
                    .. shelves.Select(shelf => new ForgeNavItem
                    {
                        Title = shelf.Name,
                        Href = $"/shelves/{shelf.Id}",
                        Icon = "bi-folder2-open",
                    }),
                ],
            });
        }

        sections.Add(new ForgeNavSection
        {
            Heading = "Admin",
            Roles = WellKnownRoles.Administrator,
            Items =
            [
                new() { Title = "Users", Href = "/users", Icon = "bi-people" },
                new() { Title = "Metadata", Href = "/metadata", Icon = "bi-tags" },
                new() { Title = "Extra Content", Href = "/extra-content", Icon = "bi-folder-plus" },
                new() { Title = "Audiobooks", Href = "/audiobooks", Icon = "bi-headphones" },
                new() { Title = "Logs", Href = "/logvue", Icon = "bi-journal-text" },
                new() { Title = "Hangfire", Href = "/hangfire", Icon = "bi-speedometer2", External = true, Tooltip = "Background jobs" },
            ],
        });

        return sections;
    }

    private static string? FormatCount(int? count) => count is > 0 ? count.Value.ToString("N0") : null;
}
