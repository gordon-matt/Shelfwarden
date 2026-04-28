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
    /// Prefers the in-repo shared folder (dev). Falls back to <c>wwwroot</c> next
    /// to the executable when running from a published build, where MSBuild has
    /// already copied the linked Content items into the publish output.
    /// </summary>
    public static string ResolveWebRoot()
    {
        string? shared = TryGetRepoSharedWebRoot();
        if (shared is not null && Directory.Exists(shared))
        {
            return shared;
        }

        string published = Path.Combine(AppContext.BaseDirectory, "wwwroot");
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