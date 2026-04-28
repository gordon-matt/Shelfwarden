namespace Shelfwarden.Components.Layout;

/// <summary>
/// Lets pages notify <see cref="Sidebar"/> that sidebar-backed data (libraries, reading lists,
/// collections) changed so the nav can reload without a full page refresh.
/// </summary>
public interface ISidebarNavRefreshService
{
    event EventHandler? NavigationDataChanged;

    void NotifyNavigationDataChanged();
}

public sealed class SidebarNavRefreshService : ISidebarNavRefreshService
{
    public event EventHandler? NavigationDataChanged;

    public void NotifyNavigationDataChanged() =>
        NavigationDataChanged?.Invoke(this, EventArgs.Empty);
}
