using MailMeUp.Core;
using MailMeUp.Diagnostics;
using MailMeUp.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;

namespace MailMeUp.Providers.Microsoft;

internal sealed class MicrosoftAccessTokenProvider(IProviderConfigurationStore configurations, ISecretStore secrets, ILogger logger)
{
    private const string AccountIdPrefix = "microsoft:";

    public async Task<string> GetAsync(
        Account account,
        IReadOnlyList<string> scopes,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(account.Provider, "microsoft", StringComparison.Ordinal) ||
            !account.Id.StartsWith(AccountIdPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("The account does not belong to Microsoft.", nameof(account));
        }

        var configuration = await configurations.GetAsync("microsoft", cancellationToken)
            ?? throw new ProviderReadException("Microsoft app setup is missing.", ReadFailureKind.SetupRequired);
        try
        {
            return await MicrosoftIdentitySession.RunAsync(
                configuration.ClientId,
                secrets,
                async application =>
                {
                    var homeAccountId = account.Id[AccountIdPrefix.Length..];
                    var msalAccount = (await application.GetAccountsAsync())
                        .SingleOrDefault(item => string.Equals(item.HomeAccountId?.Identifier, homeAccountId, StringComparison.Ordinal))
                        ?? throw new ProviderReadException("The Microsoft account credential is missing. Reconnect the account.", ReadFailureKind.SignInRequired);
                    try
                    {
                        var result = await application.AcquireTokenSilent(scopes, msalAccount).ExecuteAsync(cancellationToken);
                        return string.IsNullOrWhiteSpace(result.AccessToken)
                            ? throw new ProviderReadException("Microsoft access expired. Reconnect the account.", ReadFailureKind.SignInRequired)
                            : result.AccessToken;
                    }
                    catch (MsalUiRequiredException exception)
                    {
                        ReadDiagnostics.Failure(logger, exception, "token_acquisition");
                        throw new ProviderReadException("Microsoft access expired. Reconnect the account.", ReadFailureKind.SignInRequired);
                    }
                },
                cancellationToken);
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
        catch (SecretStoreException exception)
        {
            ReadDiagnostics.Failure(logger, exception, "token_acquisition");
            throw new ProviderReadException("The protected Microsoft credential could not be accessed. Check local credential storage.", ReadFailureKind.LocalCredentialsUnavailable);
        }
        catch (HttpRequestException exception)
        {
            ReadDiagnostics.Failure(logger, exception, "token_acquisition");
            throw new ProviderReadException("Microsoft authorization could not be reached.", ReadFailureKind.Network);
        }
        catch (Exception exception)
        {
            ReadDiagnostics.Failure(logger, exception, "token_acquisition");
            throw new ProviderReadException("Microsoft authorization is temporarily unavailable. Try again later.", ReadFailureKind.ProviderUnavailable);
        }
    }
}
