using Shelfwarden.Models.Opds;

namespace Shelfwarden.Components.Pages;

public partial class OpdsSettings : ComponentBase
{
    private IReadOnlyList<OpdsCredentialDto>? allCredentials;
    private string? bannerError;
    private OpdsCredentialDto? credential;
    private bool isAdministrator;
    private OpdsKeyIssuedDto? issuedKey;
    private bool loading = true;
    private bool opdsEnabled;
    private bool saving;

    /// <summary>Absolute catalogue address. Built from the browser's view of the site, so it carries the public host and path base.</summary>
    private string FeedUrl => Navigation.ToAbsoluteUri("opds").ToString();

    private string KeyFeedUrl(string compactKey) => Navigation.ToAbsoluteUri($"opds/key/{compactKey}").ToString();

    protected override async Task OnInitializedAsync()
    {
        var auth = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        isAdministrator = auth.User.IsInRole(Constants.Roles.Administrator);

        await LoadAsync();
        loading = false;
    }

    private static string FormatResult(Ardalis.Result.IResult result) => result.ValidationErrors is not null && result.ValidationErrors.Any()
        ? string.Join(" ", result.ValidationErrors.Select(e => e.ErrorMessage))
        : string.Join(" ", result.Errors);

    private async Task LoadAsync()
    {
        var settings = await ServerSettings.GetAsync();
        opdsEnabled = settings.IsSuccess && settings.Value.OpdsEnabled;

        var current = await CredentialService.GetCurrentAsync();
        credential = current.IsSuccess ? current.Value : null;
        if (!current.IsSuccess && current.Status != ResultStatus.NotFound)
        {
            bannerError = FormatResult(current);
        }

        if (isAdministrator)
        {
            var all = await CredentialService.ListAllAsync();
            allCredentials = all.IsSuccess ? all.Value : [];
        }
    }

    private async Task ToggleEnabledAsync(ChangeEventArgs e)
    {
        bool enable = e.Value is true;
        saving = true;
        try
        {
            var result = await ServerSettings.SetAsync(Constants.ServerSettingKeys.OpdsEnabled, enable ? "true" : "false");
            if (result.IsSuccess)
            {
                opdsEnabled = enable;
            }
            else
            {
                bannerError = FormatResult(result);
            }
        }
        finally
        {
            saving = false;
        }
    }

    private async Task GenerateAsync()
    {
        if (credential is not null
            && !await JS.InvokeAsync<bool>("confirm", "Replace your OPDS key? Apps using the current key will need the new one."))
        {
            return;
        }

        saving = true;
        try
        {
            var result = await CredentialService.GenerateAsync();
            if (result.IsSuccess)
            {
                issuedKey = result.Value;
                await LoadAsync();
            }
            else
            {
                bannerError = FormatResult(result);
            }
        }
        finally
        {
            saving = false;
        }
    }

    private async Task RevokeAsync()
    {
        if (!await JS.InvokeAsync<bool>("confirm", "Revoke your OPDS key? Reading apps will stop working until you generate a new one."))
        {
            return;
        }

        saving = true;
        try
        {
            var result = await CredentialService.RevokeAsync();
            if (result.IsSuccess)
            {
                issuedKey = null;
                await LoadAsync();
            }
            else
            {
                bannerError = FormatResult(result);
            }
        }
        finally
        {
            saving = false;
        }
    }

    private async Task RevokeForUserAsync(OpdsCredentialDto target)
    {
        if (!await JS.InvokeAsync<bool>("confirm", $"Revoke the OPDS key for \"{target.UserName}\"?"))
        {
            return;
        }

        saving = true;
        try
        {
            var result = await CredentialService.RevokeForUserAsync(target.UserId);
            if (result.IsSuccess)
            {
                if (credential?.UserId == target.UserId)
                {
                    issuedKey = null;
                }

                await LoadAsync();
            }
            else
            {
                bannerError = FormatResult(result);
            }
        }
        finally
        {
            saving = false;
        }
    }

    private async Task CopyAsync(string text)
    {
        try
        {
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", text);
        }
        catch (JSException)
        {
            // Clipboard access needs a secure context; the value is still selectable in the page.
        }
    }
}
