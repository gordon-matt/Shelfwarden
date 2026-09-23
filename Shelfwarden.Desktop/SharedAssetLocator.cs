using System.Runtime.CompilerServices;

namespace Shelfwarden.Desktop;

/// <summary>
/// Locates the shared <c>wwwroot</c> folder used by both the web project and this
/// Electron-hosted desktop project. Static assets live under
/// <c>..\Shelfwarden\wwwroot</c> relative to this file's compile-time path. Using
/// <see cref="CallerFilePathAttribute"/> avoids fragile runtime walks of
/// <c>Directory.GetCurrentDirectory()</c>, which can vary depending on how the
/// process was launched (Visual Studio F5, <c>dotnet run</c>, Electron, …).
/// </summary>
internal static class SharedAssetLocator
{
    /// <summary>
/// Returns the absolute path that should be used as ASP.NET Core's WebRoot.
/// A published build has <c>wwwroot</c> next to the executable; that wins so an
/// install on a dev machine does not keep reading the git checkout. <c>dotnet run</c>
/// does not copy wwwroot, so it still uses the shared folder under Shelfwarden.
    /// </summary>
    public static string ResolveWebRoot()
    {
        // A published / installed build copies wwwroot next to the executable. Prefer that
        // even when this machine also has the git checkout: CallerFilePath still points at
        // the source tree, and using it would serve dev files (and miss published assets).
        string published = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (File.Exists(Path.Combine(published, "css", "site.css")))
        {
            return published;
        }

        string? shared = TryGetRepoSharedWebRoot();
        if (shared is not null && Directory.Exists(shared))
        {
            return shared;
        }

        Directory.CreateDirectory(published);
        return published;
    }

    private static string? TryGetRepoSharedWebRoot([CallerFilePath] string thisFilePath = "")
    {
        if (string.IsNullOrEmpty(thisFilePath))
        {
            return null;
        }

        string? desktopDir = Path.GetDirectoryName(thisFilePath);
        string? repoDir = Path.GetDirectoryName(desktopDir);
        return repoDir is null ? null : Path.Combine(repoDir, "Shelfwarden", "wwwroot");
    }
}