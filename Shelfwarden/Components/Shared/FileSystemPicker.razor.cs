using System.Runtime.InteropServices;

namespace Shelfwarden.Components.Shared;

public partial class FileSystemPicker : ComponentBase
{
    // Sentinel path when on Windows with no explicit RootPath — "This PC" drive list view.
    private const string WindowsVirtualRoot = "\x00DRIVES";

    public enum PickerMode : byte
    {
        Folder,
        File,
    }

    [Parameter] public PickerMode Mode { get; set; } = PickerMode.Folder;

    /// <summary>
    /// When <see cref="Mode"/> is <see cref="PickerMode.File"/>, allows selecting several files
    /// (across folders) before confirming. Invokes <see cref="OnConfirmMultiple"/> instead of
    /// <see cref="OnConfirm"/>.
    /// </summary>
    [Parameter] public bool AllowMultiple { get; set; }

    [Parameter] public string[]? AllowedExtensions { get; set; }

    [Parameter] public string? RootPath { get; set; }

    [Parameter] public string Title { get; set; } = "Select";

    [Parameter] public EventCallback<string> OnConfirm { get; set; }

    [Parameter] public EventCallback<IReadOnlyList<string>> OnConfirmMultiple { get; set; }

    [Parameter] public EventCallback OnCancel { get; set; }

    private string _currentPath = "/";
    private string? _selectedPath;
    private readonly HashSet<string> _selectedPaths = new(StringComparer.OrdinalIgnoreCase);
    private string? _error;
    private readonly List<FileSystemEntry> _entries = [];
    private readonly List<Breadcrumb> _breadcrumbs = [];

    private bool IsMultiFile => AllowMultiple && Mode == PickerMode.File;

    protected override void OnInitialized()
    {
        _currentPath = RootPath ?? (IsWindows() ? WindowsVirtualRoot : "/");
        LoadEntries(_currentPath);
    }

    private void NavigateTo(string path)
    {
        if (!IsMultiFile)
        {
            _selectedPath = null;
        }

        _currentPath = path;
        LoadEntries(path);
    }

    private void LoadEntries(string path)
    {
        _error = null;
        _entries.Clear();

        try
        {
            if (path == WindowsVirtualRoot)
            {
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(d => new FileSystemEntry
                    {
                        Name = string.IsNullOrWhiteSpace(d.VolumeLabel)
                            ? d.Name.TrimEnd('\\', '/')
                            : $"{d.Name.TrimEnd('\\', '/')}  —  {d.VolumeLabel}",
                        FullPath = d.RootDirectory.FullName,
                        IsDirectory = true
                    });
                _entries.AddRange(drives);
            }
            else
            {
                var dirs = Directory.GetDirectories(path)
                    .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                    .Select(d => new FileSystemEntry
                    {
                        Name = Path.GetFileName(d),
                        FullPath = d,
                        IsDirectory = true
                    });
                _entries.AddRange(dirs);

                if (Mode == PickerMode.File)
                {
                    var files = Directory.GetFiles(path)
                        .Where(f => AllowedExtensions == null ||
                                    AllowedExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                        .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                        .Select(f => new FileSystemEntry
                        {
                            Name = Path.GetFileName(f),
                            FullPath = f,
                            IsDirectory = false,
                            Size = new FileInfo(f).Length
                        });
                    _entries.AddRange(files);
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            _error = "Access denied to this folder.";
        }
        catch (Exception ex)
        {
            _error = $"Could not read folder: {ex.Message}";
        }

        BuildBreadcrumbs(path);
    }

    private void BuildBreadcrumbs(string path)
    {
        _breadcrumbs.Clear();

        bool isVirtualRoot = RootPath == null && IsWindows();
        string rootPath = isVirtualRoot ? WindowsVirtualRoot : (RootPath ?? "/");
        string rootLabel = IsWindows() ? "This PC" : "/";

        _breadcrumbs.Add(new Breadcrumb { Label = rootLabel, Path = rootPath });

        if (path == rootPath)
        {
            return;
        }

        if (isVirtualRoot)
        {
            string? driveRoot = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(driveRoot))
            {
                return;
            }

            string driveLabel = driveRoot.TrimEnd('\\', '/');
            _breadcrumbs.Add(new Breadcrumb { Label = driveLabel, Path = driveRoot });

            if (string.Equals(path, driveRoot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string relative = Path.GetRelativePath(driveRoot, path);
            string accumulated = driveRoot;
            foreach (string? part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         .Where(p => !string.IsNullOrEmpty(p)))
            {
                accumulated = Path.Combine(accumulated, part);
                _breadcrumbs.Add(new Breadcrumb { Label = part, Path = accumulated });
            }
        }
        else
        {
            string relative = Path.GetRelativePath(rootPath, path);
            if (relative == ".")
            {
                return;
            }

            string accumulated = rootPath;
            foreach (string? part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         .Where(p => !string.IsNullOrEmpty(p)))
            {
                accumulated = Path.Combine(accumulated, part);
                _breadcrumbs.Add(new Breadcrumb { Label = part, Path = accumulated });
            }
        }
    }

    private void HandleEntryClick(FileSystemEntry entry)
    {
        if (Mode == PickerMode.Folder && entry.IsDirectory)
        {
            _selectedPath = entry.FullPath;
        }
        else if (Mode == PickerMode.File && !entry.IsDirectory)
        {
            if (IsMultiFile)
            {
                if (!_selectedPaths.Add(entry.FullPath))
                {
                    _selectedPaths.Remove(entry.FullPath);
                }
            }
            else
            {
                _selectedPath = entry.FullPath;
            }
        }
    }

    private void HandleEntryDoubleClick(FileSystemEntry entry)
    {
        if (entry.IsDirectory)
        {
            NavigateTo(entry.FullPath);
        }
        else if (Mode == PickerMode.File)
        {
            if (IsMultiFile)
            {
                _ = _selectedPaths.Add(entry.FullPath);
            }
            else
            {
                _selectedPath = entry.FullPath;
                Confirm();
            }
        }
    }

    private void SelectCurrentFolder()
    {
        if (_currentPath != WindowsVirtualRoot)
        {
            _selectedPath = _currentPath;
        }
    }

    private void HandleOverlayClick() => Cancel();

    private void Cancel()
    {
        if (OnCancel.HasDelegate)
        {
            OnCancel.InvokeAsync();
        }
    }

    private void ClearMultiSelection()
    {
        _selectedPaths.Clear();
    }

    private void Confirm()
    {
        if (IsMultiFile)
        {
            if (_selectedPaths.Count > 0 && OnConfirmMultiple.HasDelegate)
            {
                OnConfirmMultiple.InvokeAsync(_selectedPaths.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList());
            }

            return;
        }

        if (_selectedPath != null && OnConfirm.HasDelegate)
        {
            OnConfirm.InvokeAsync(_selectedPath);
        }
    }

    private bool IsEntrySelected(FileSystemEntry entry)
    {
        if (IsMultiFile && !entry.IsDirectory)
        {
            return _selectedPaths.Contains(entry.FullPath);
        }

        return _selectedPath == entry.FullPath;
    }

    private static bool IsWindows() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static string GetFileIcon(string filename) =>
        Path.GetExtension(filename).ToLowerInvariant() switch
        {
            ".epub" => "bi-book",
            ".pdf" => "bi-file-earmark-pdf",
            ".mobi" or ".azw" or ".azw3" => "bi-file-earmark-text",
            ".cbz" or ".cbr" => "bi-images",
            ".txt" => "bi-file-earmark-text",
            ".jpg" or ".jpeg"
                or ".png" or ".gif"
                or ".webp" => "bi-file-earmark-image",
            ".mp3" or ".flac" or ".ogg" => "bi-file-earmark-music",
            ".mp4" or ".mkv" or ".avi" => "bi-file-earmark-play",
            ".zip" or ".tar" or ".gz" => "bi-file-earmark-zip",
            _ => "bi-file-earmark",
        };

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB",
    };

    private static string TruncatePath(string path)
    {
        const int max = 60;
        return path.Length <= max ? path : "…" + path[^(max - 1)..];
    }

    private sealed class FileSystemEntry
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
    }

    private sealed class Breadcrumb
    {
        public string Label { get; set; } = "";
        public string Path { get; set; } = "";
    }
}