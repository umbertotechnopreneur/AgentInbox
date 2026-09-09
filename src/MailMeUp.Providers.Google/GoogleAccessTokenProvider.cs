using System.Security.Cryptography;
using System.Text;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using MailMeUp.Core;
using MailMeUp.Diagnostics;
using MailMeUp.Security;
using Microsoft.Extensions.Logging;

namespace MailMeUp.Providers.Google;

internal sealed class GoogleAccessTokenProvider(IProviderConfigurationStore configurations, ISecretStore secrets, ILogger logger)
{
    private const string MailScope = "https://www.googleapis.com/auth/gmail.readonly";
    private const string CalendarListScope = "https://www.googleapis.com/auth/calendar.calendarlist.readonly";
    private const string CalendarEventsScope = "https://www.googleapis.com/auth/calendar.events.readonly";

    public async Task<string> GetAsync(Account account, CancellationToken cancellationToken)
    {
        if (!string.Equals(account.Provider, "google", StringComparison.Ordinal))
        {
            throw new ArgumentException("The account does not belong to Google.", nameof(account));
        }

        var configuration = await configurations.GetAsync("google", cancellationToken)
            ?? throw new ProviderReadException("Google app setup is missing.", ReadFailureKind.SetupRequired);
        if (string.IsNullOrWhiteSpace(configuration.ClientSecretReference))
        {
            throw new ProviderReadException("The protected Google client credential is missing.", ReadFailureKind.LocalCredentialsUnavailable);
        }

        byte[]? secretBytes = null;
        try
        {
            using var session = await GoogleCredentialSession.AcquireAsync(secrets, account.Id, cancellationToken);
            secretBytes = await secrets.ReadAsync(configuration.ClientSecretReference, cancellationToken)
                ?? throw new ProviderReadException("The protected Google client credential is missing.", ReadFailureKind.LocalCredentialsUnavailable);
            var tokenStore = new ProtectedGoogleTokenStore(secrets, account.Id, cancellationToken);
            using var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = configuration.ClientId,
                    ClientSecret = Encoding.UTF8.GetString(secretBytes)
                },
                Scopes = CreateScopes(account),
                DataStore = tokenStore
            });
            var token = await flow.LoadTokenAsync(account.Id, cancellationToken)
                ?? throw new ProviderReadException("The Google account credential is missing. Reconnect the account.", ReadFailureKind.SignInRequired);
            var credential = new UserCredential(flow, account.Id, token);
            var accessToken = await credential.GetAccessTokenForRequestAsync(null, cancellationToken);
            return string.IsNullOrWhiteSpace(accessToken)
                ? throw new ProviderReadException("Google access expired. Reconnect the account.", ReadFailureKind.SignInRequired)
                : accessToken;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ProviderReadException exception)
        {
            ReadDiagnostics.Failure(logger, exception, "token_acquisition");
            throw;
        }
        catch (TokenResponseException exception) when (
            string.Equals(exception.Error?.Error, "invalid_grant", StringComparison.Ordinal))
        {
            ReadDiagnostics.Failure(logger, exception, "token_acquisition");
            throw new ProviderReadException("Google authorization expired or was revoked. Reconnect the account.", ReadFailureKind.SignInRequired);
        }
        catch (SecretStoreException exception)
        {
            ReadDiagnostics.Failure(logger, exception, "token_acquisition");
            throw new ProviderReadException("The protected Google credential could not be accessed. Check local credential storage.", ReadFailureKind.LocalCredentialsUnavailable);
        }
        catch (HttpRequestException exception)
        {
            ReadDiagnostics.Failure(logger, exception, "token_acquisition");
            throw new ProviderReadException("Google authorization could not be reached.", ReadFailureKind.Network);
        }
        catch (Exception exception)
        {
            ReadDiagnostics.Failure(logger, exception, "token_acquisition");
            throw new ProviderReadException("Google authorization is temporarily unavailable. Try again later.", ReadFailureKind.ProviderUnavailable);
        }
        finally
        {
            if (secretBytes is not null)
            {
                CryptographicOperations.ZeroMemory(secretBytes);
            }
        }
    }

    private static IReadOnlyList<string> CreateScopes(Account account)
    {
        var scopes = new List<string> { "openid", "email", "profile" };
        if (account.MailReadEnabled)
        {
            scopes.Add(MailScope);
        }

        if (account.CalendarReadEnabled)
        {
            scopes.Add(CalendarListScope);
            scopes.Add(CalendarEventsScope);
        }

        return scopes;
    }
}
