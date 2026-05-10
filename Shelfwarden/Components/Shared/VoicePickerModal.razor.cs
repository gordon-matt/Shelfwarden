using Microsoft.AspNetCore.Components.Web;

namespace Shelfwarden.Components.Shared;

public partial class VoicePickerModal : ComponentBase, IAsyncDisposable
{
    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public string? InitialVoiceName { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }
    [Parameter] public EventCallback<string> OnSelected { get; set; }

    [Inject] private IJSRuntime Js { get; set; } = default!;

    private bool loading;
    private bool hasLoaded;
    private string? loadError;
    private string? previewError;
    private string? playingVoiceName;
    private string? selectedVoiceName;
    private string filterText = string.Empty;

    /// <summary>
    /// Dropdown value: synthetic <see cref="EnglishBucket"/> maps to American + British English
    /// voices; otherwise matches <see cref="KokoroVoiceDto.Language"/> exactly.
    /// </summary>
    private string selectedLanguageBucketValue = EnglishBucket;

    private string selectedLanguageBucket
    {
        get => selectedLanguageBucketValue;
        set
        {
            if (string.Equals(selectedLanguageBucketValue, value, StringComparison.Ordinal))
            {
                return;
            }

            selectedLanguageBucketValue = value;
            SyncSelectionAfterLanguageChange();
        }
    }

    private IReadOnlyList<KokoroVoiceDto> voices = [];
    private string[] languageDropdownOptions = [];
    private ElementReference audioElement;

    private const string EnglishBucket = "English";

    protected override async Task OnParametersSetAsync()
    {
        if (!IsOpen)
        {
            hasLoaded = false;
            return;
        }

        if (!hasLoaded)
        {
            hasLoaded = true;
            filterText = string.Empty;
            await LoadVoicesAsync();
        }
    }

    private async Task LoadVoicesAsync()
    {
        loading = true;
        loadError = null;
        previewError = null;
        try
        {
            var result = await AudiobookService.GetVoicesAsync();
            if (result.IsSuccess)
            {
                voices = result.Value;
                languageDropdownOptions = BuildLanguageDropdown(voices);

                // Default to English (American + British voices); fall back if the pack has none.
                if (languageDropdownOptions.Contains(EnglishBucket, StringComparer.Ordinal))
                {
                    selectedLanguageBucketValue = EnglishBucket;
                }
                else if (languageDropdownOptions.Length > 0)
                {
                    selectedLanguageBucketValue = languageDropdownOptions[0];
                }

                if (!string.IsNullOrEmpty(InitialVoiceName))
                {
                    var match = voices.FirstOrDefault(v =>
                        v.Name.Equals(InitialVoiceName, StringComparison.OrdinalIgnoreCase));
                    if (match is not null)
                    {
                        selectedLanguageBucketValue = LanguageBucketFor(match);
                        selectedVoiceName = match.Name;
                    }
                    else
                    {
                        PickDefaultVoiceInFilter();
                    }
                }
                else
                {
                    PickDefaultVoiceInFilter();
                }
            }
            else
            {
                loadError = result.Errors.FirstOrDefault() ?? "Could not load voices.";
            }
        }
        finally
        {
            loading = false;
        }
    }

    private IReadOnlyList<KokoroVoiceDto> FilteredVoices
    {
        get
        {
            IEnumerable<KokoroVoiceDto> q = voices;
            if (string.Equals(selectedLanguageBucketValue, EnglishBucket, StringComparison.Ordinal))
            {
                q = q.Where(IsEnglishVoice);
            }
            else if (!string.IsNullOrEmpty(selectedLanguageBucketValue))
            {
                q = q.Where(v =>
                    string.Equals(v.Language, selectedLanguageBucketValue, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(filterText))
            {
                string term = filterText.Trim();
                q = q.Where(v =>
                    v.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    v.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            return q.ToList();
        }
    }

    private static string[] BuildLanguageDropdown(IReadOnlyList<KokoroVoiceDto> all)
    {
        var opts = new List<string>();
        if (all.Any(IsEnglishVoice))
        {
            opts.Add(EnglishBucket);
        }

        foreach (string lang in all
            .Select(v => v.Language)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(l => !IsEnglishLanguageLabel(l))
            .OrderBy(l => l, StringComparer.OrdinalIgnoreCase))
        {
            opts.Add(lang);
        }

        return [.. opts];
    }

    private static bool IsEnglishVoice(KokoroVoiceDto v) => IsEnglishLanguageLabel(v.Language);

    private static bool IsEnglishLanguageLabel(string language)
        => language.Equals("American English", StringComparison.OrdinalIgnoreCase)
           || language.Equals("British English", StringComparison.OrdinalIgnoreCase);

    private static string LanguageBucketFor(KokoroVoiceDto v)
        => IsEnglishVoice(v) ? EnglishBucket : v.Language;

    private void PickDefaultVoiceInFilter()
    {
        var list = FilteredVoices;
        selectedVoiceName = list.Count > 0 ? list[0].Name : null;
    }

    private void SyncSelectionAfterLanguageChange()
    {
        var list = FilteredVoices;
        if (list.Count == 0)
        {
            selectedVoiceName = null;
            return;
        }

        if (!string.IsNullOrEmpty(selectedVoiceName)
            && list.Any(v => v.Name.Equals(selectedVoiceName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        selectedVoiceName = list[0].Name;
    }

    private void SelectVoice(KokoroVoiceDto voice) => selectedVoiceName = voice.Name;

    private void OnVoiceKeyDown(KeyboardEventArgs e, KokoroVoiceDto voice)
    {
        // Mirror native button keyboard semantics — Enter / Space activate the row.
        if (e.Key is "Enter" or " ")
        {
            SelectVoice(voice);
        }
    }

    private async Task PlayPreviewAsync(KokoroVoiceDto voice)
    {
        previewError = null;
        playingVoiceName = voice.Name;
        StateHasChanged();

        try
        {
            string url = $"audiobooks/voices/{Uri.EscapeDataString(voice.Name)}/preview";
            // Ask the browser to load + play the preview. Using JS interop because a single
            // <audio> element with a dynamic src bound through Razor races with re-renders.
            await Js.InvokeVoidAsync("shelfwardenAudio.play", audioElement, url);
        }
        catch (Exception ex)
        {
            previewError = ex.Message;
            playingVoiceName = null;
        }
    }

    private void OnPreviewEnded() => playingVoiceName = null;

    private async Task ConfirmAsync()
    {
        if (string.IsNullOrEmpty(selectedVoiceName))
        {
            return;
        }

        await StopPreviewAsync();
        await OnSelected.InvokeAsync(selectedVoiceName);
    }

    private async Task CloseAsync()
    {
        await StopPreviewAsync();
        await OnClose.InvokeAsync();
    }

    private async Task StopPreviewAsync()
    {
        try
        {
            await Js.InvokeVoidAsync("shelfwardenAudio.stop", audioElement);
        }
        catch
        {
            // Ignore — element may have been disposed already.
        }
        finally
        {
            playingVoiceName = null;
        }
    }

    public async ValueTask DisposeAsync() => await StopPreviewAsync();
}