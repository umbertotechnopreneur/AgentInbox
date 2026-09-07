using System.Net.Http.Headers;
using MailMeUp.Core;
using MailMeUp.Security;

namespace MailMeUp.Providers.Microsoft;

/// <summary>Checks Microsoft mail and calendar read access without returning message or calendar content.</summary>
public sealed class MicrosoftConnectionChecker : IAccountConnectionChecker
{
    private const string MailScope = "Mail.Read";
    private const string CalendarScope = "Calendars.Read";
    private static readonly HttpClient HttpClient = new();
    private readonly MicrosoftAccessTokenProvider _tokens;

    /// <summary>Creates a Microsoft connection checker backed by the protected MSAL cache.</summary>
    public MicrosoftConnectionChecker(IProviderConfigurationStore configurations, ISecretStore secrets)
    {
        _tokens = new MicrosoftAccessTokenProvider(configurations, secrets);
    }

    /// <inheritdoc />
    public string ProviderId => "microsoft";

    /// <inheritdoc />
    public async Task<AccountConnectionCheck> CheckAsync(Account account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (!string.Equals(account.Provider, ProviderId, StringComparison.Ordinal))
        {
            throw new ArgumentException("The account does not belong to Microsoft.", nameof(account));
        }

        if (!account.MailReadEnabled && !account.CalendarReadEnabled)
        {
            throw new ProviderReadException("The Microsoft account has no enabled read service.", ReadFailureKind.LocalConfiguration);
        }

        CapabilityCheck? mail = null;
        CapabilityCheck? calendar = null;
        if (account.MailReadEnabled)
        {
            mail = await CheckCapabilityAsync(async () =>
            {
                var accessToken = await _tokens.GetAsync(account, [MailScope], cancellationToken);
                await ProbeAsync("https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages?%24select=id&%24top=1", accessToken, cancellationToken);
            });
        }
        if (account.CalendarReadEnabled)
        {
            calendar = await CheckCapabilityAsync(async () =>
            {
                var accessToken = await _tokens.GetAsync(account, [CalendarScope], cancellationToken);
                await ProbeAsync("https://graph.microsoft.com/v1.0/me/calendars?%24select=id&%24top=1", accessToken, cancellationToken);
            });
        }

        var failure = mail?.FailureKind ?? calendar?.FailureKind;
        return new AccountConnectionCheck(
            account.Id,
            Reachable: (mail is null || mail.Value.Reachable) && (calendar is null || calendar.Value.Reachable),
            failure,
            mail?.Reachable,
            mail?.FailureKind,
            calendar?.Reachable,
            calendar?.FailureKind);
    }

    private static async Task<CapabilityCheck> CheckCapabilityAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            return new CapabilityCheck(true, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new CapabilityCheck(false, ClassifyFailure(exception));
        }
    }

    private static ReadFailureKind ClassifyFailure(Exception exception) => exception switch
    {
        ProviderReadException provider => provider.Kind,
        ProviderAuthenticationException => ReadFailureKind.SignInRequired,
        HttpRequestException => ReadFailureKind.Network,
        _ => ReadFailureKind.ProviderUnavailable
    };

    private static async Task ProbeAsync(string url, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderReadException("Microsoft Graph returned an unsuccessful connection response.", ClassifyHttpFailure((int)response.StatusCode));
        }
    }

    private static ReadFailureKind ClassifyHttpFailure(int statusCode) => statusCode switch
    {
        401 => ReadFailureKind.SignInRequired,
        403 => ReadFailureKind.AccessDenied,
        408 or 504 => ReadFailureKind.Timeout,
        429 or >= 500 => ReadFailureKind.ProviderUnavailable,
        _ => ReadFailureKind.Unknown
    };

    private readonly record struct CapabilityCheck(bool Reachable, ReadFailureKind? FailureKind);
}
