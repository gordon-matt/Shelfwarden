using Microsoft.AspNetCore.WebUtilities;
using Shelfwarden.Infrastructure;

namespace Shelfwarden.Components.Pages;

public partial class Setup : ComponentBase
{
    [SupplyParameterFromQuery] public string? Step { get; set; }
    [SupplyParameterFromQuery] public string? Error { get; set; }

    private SetupStatusDto? status;
    private string? errorMessage;
    private string adminEmail = "admin@shelfwarden.local";
    private string shelfName = "My Shelf";
    private string shelfFolders = "";

    private record StepDescriptor(string Id, int Number, string Label);

    private List<StepDescriptor> steps = [];

    private string CurrentStep => ResolveStep(Step);

    protected override async Task OnInitializedAsync()
    {
        var result = await SetupService.GetStatusAsync();
        if (result.IsSuccess)
        {
            status = result.Value;
            BuildSteps();
            SetupWizardLogger.LogInformation(
                "[SetupWizard] GetStatusAsync OK SetupComplete={Complete} Auth={Auth} ShelfCount={ShelfCount} CallerAdmin={Admin}",
                status.SetupComplete,
                status.AuthProvider,
                status.ShelfCount,
                status.CallerIsAdministrator);
        }
        else
        {
            errorMessage = ResultMessages.UserFacing(result, "Failed to load setup state.");
            SetupWizardLogger.LogWarning(
                "[SetupWizard] GetStatusAsync failed Status={Status} MessageLen={Len}",
                result.Status,
                errorMessage?.Length ?? 0);
        }
    }

    private void BuildSteps()
    {
        steps.Clear();
        steps.Add(new StepDescriptor("welcome", 1, "Welcome"));

        int n = 2;
        if (status?.AuthProvider == nameof(Shelfwarden.Services.Auth.AuthProvider.Identity))
        {
            steps.Add(new StepDescriptor("admin", n++, "Administrator"));
        }

        steps.Add(new StepDescriptor("shelf", n++, "First shelf"));
        steps.Add(new StepDescriptor("done", n, "Finish"));
    }

    private string ResolveStep(string? requested)
    {
        if (status is null) return "welcome";
        if (string.IsNullOrEmpty(requested)) return "welcome";

        bool valid = steps.Any(s => s.Id.Equals(requested, StringComparison.OrdinalIgnoreCase));
        return valid ? requested.ToLowerInvariant() : "welcome";
    }

    private bool IsStepDone(string stepId)
    {
        int currentIndex = steps.FindIndex(s => s.Id == CurrentStep);
        int targetIndex = steps.FindIndex(s => s.Id == stepId);
        return currentIndex >= 0 && targetIndex >= 0 && targetIndex < currentIndex;
    }

    private string NextLinkAfterWelcome()
    {
        if (status?.AuthProvider == nameof(Shelfwarden.Services.Auth.AuthProvider.Identity))
        {
            return "/setup?step=admin";
        }
        return "/setup?step=shelf";
    }

    private static string FolderPlaceholder()
    {
        return OperatingSystem.IsWindows()
            ? "D:\\Library\\Fiction\nD:\\Library\\NonFiction"
            : "/app/data/library/Fiction\n/app/data/library/NonFiction";
    }

    protected override void OnParametersSet()
    {
        LogQueryDiagnostics();

        if (!string.IsNullOrWhiteSpace(Error))
        {
            errorMessage = Error.Trim();
            SetupWizardLogger.LogWarning(
                "[SetupWizard] errorMessage from query SupplyParameter Len={Len} AfterTrim Len={TrimLen} ShowAlert={Show}",
                Error.Length,
                errorMessage?.Length ?? 0,
                !string.IsNullOrWhiteSpace(errorMessage));
            if (!string.IsNullOrEmpty(errorMessage) && errorMessage.Length <= 400)
            {
                SetupWizardLogger.LogWarning("[SetupWizard] errorMessage preview: {Preview}", errorMessage);
            }
        }
        else if (Error is not null)
        {
            SetupWizardLogger.LogWarning(
                "[SetupWizard] Error query present but whitespace-only SupplyLen={Len}",
                Error.Length);
        }
    }

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender && !string.IsNullOrWhiteSpace(errorMessage))
        {
            SetupWizardLogger.LogWarning(
                "[SetupWizard] First render: showing alert with errorMessage length={Len}",
                errorMessage!.Length);
        }
    }

    /// <summary>Logs raw query vs supply parameters — helps debug proxy/encoding issues on NAS.</summary>
    private void LogQueryDiagnostics()
    {
        try
        {
            var uri = Nav.ToAbsoluteUri(Nav.Uri);
            var qs = QueryHelpers.ParseQuery(uri.Query);
            qs.TryGetValue("error", out var errorFromQuery);
            string? parsed = errorFromQuery.Count > 0 ? errorFromQuery[0] : null;

            bool supplyMatchesParsed = string.Equals(Error, parsed, StringComparison.Ordinal);
            SetupWizardLogger.LogInformation(
                "[SetupWizard] Query diag PathAndQuery={Path} SupplyStep={SupplyStep} SupplyErrorLen={SupplyErrLen} ParsedErrorLen={ParsedLen} ParamsMatch={Match}",
                uri.PathAndQuery,
                Step,
                Error?.Length ?? 0,
                parsed?.Length ?? 0,
                supplyMatchesParsed);

            if (parsed is { Length: > 0 } && string.IsNullOrWhiteSpace(parsed))
            {
                SetupWizardLogger.LogWarning("[SetupWizard] Parsed 'error' is non-empty but all whitespace (invisible characters?) Codes={Codes}",
                    FormatCodePoints(parsed, 48));
            }

            if (parsed is { Length: > 0 } && !supplyMatchesParsed)
            {
                SetupWizardLogger.LogWarning(
                    "[SetupWizard] SupplyParameter Error differs from parsed query — binding issue? SupplyCodes={Supply} ParsedCodes={Parsed}",
                    FormatCodePoints(Error ?? "", 32),
                    FormatCodePoints(parsed, 32));
            }
        }
        catch (Exception ex)
        {
            SetupWizardLogger.LogError(ex, "[SetupWizard] LogQueryDiagnostics failed");
        }
    }

    private static string FormatCodePoints(string s, int maxChars)
    {
        int n = Math.Min(s.Length, maxChars);
        var parts = new string[n];
        for (int i = 0; i < n; i++)
        {
            parts[i] = $"U+{(uint)s[i]:X4}";
        }

        return string.Join(",", parts);
    }
}