using System.Net.Http.Headers;
using MailMeUp.Core;
using MailMeUp.Security;

namespace MailMeUp.Providers.Google;

/// <summary>Checks Google mail and calendar read access without returning message or calendar content.</summary>
public sealed class GoogleConnectionChecker : IAccountConnectionChecker
{
    private static readonly HttpClient HttpClient = new();
    private readonly GoogleAccessTokenProvider _tokens;

    /// <summary>Creates a Google connection checker backed by protected account tokens.</summary>
    public GoogleConnectionChecker(IProviderConfigurationStore configurations, ISecretStore secrets)
    {
        _tokens = new GoogleAccessTokenProvider(configurations, secrets);
    }

    /// <inheritdoc />
    public string ProviderId => "google";

    /// <inheritdoc />
    public async Task<AccountConnectionCheck> CheckAsync(Account account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (!string.Equals(account.Provider, ProviderId, StringComparison.Ordinal))
        {
            throw new ArgumentException("The account does not belong to Google.", nameof(account));
        }

        if (!account.MailReadEnabled && !account.CalendarReadEnabled)
        {
            throw new ProviderReadException("The Google account has no enabled read service.", ReadFailureKind.LocalConfiguration);
        }

        string accessToken;
        try
        {
            accessToken = await _tokens.GetAsync(account, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var failure = ClassifyFailure(exception);
            return new AccountConnectionCheck(
                account.Id,
                Reachable: false,
                failure,
                account.MailReadEnabled ? false : null,
                account.MailReadEnabled ? failure : null,
                account.CalendarReadEnabled ? false : null,
                account.CalendarReadEnabled ? failure : null);
        }

        CapabilityCheck? mail = null;
        CapabilityCheck? calendar = null;
        if (account.MailReadEnabled)
        {
            mail = await CheckCapabilityAsync(() => ProbeAsync(
                "https://gmail.googleapis.com/gmail/v1/users/me/profile",
                accessToken,
                cancellationToken));
        }
        if (account.CalendarReadEnabled)
        {
            calendar = await CheckCapabilityAsync(() => ProbeAsync(
                "https://www.googleapis.com/calendar/v3/users/me/calendarList?maxResults=1&fields=nextPageToken",
                accessToken,
                cancellationToken));
        }

        var failureKind = mail?.FailureKind ?? calendar?.FailureKind;
        return new AccountConnectionCheck(
            account.Id,
            Reachable: (mail is null || mail.Value.Reachable) && (calendar is null || calendar.Value.Reachable),
            failureKind,
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
            throw new ProviderReadException("Google returned an unsuccessful connection response.", ClassifyHttpFailure((int)response.StatusCode));
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
