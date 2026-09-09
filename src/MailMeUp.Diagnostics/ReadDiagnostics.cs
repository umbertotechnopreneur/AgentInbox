using System.Security.Cryptography;
using System.Text;
using MailMeUp.Core;
using Microsoft.Extensions.Logging;

namespace MailMeUp.Diagnostics;

/// <summary>Correlates provider reads without recording account identities or request content.</summary>
public static class ReadDiagnostics
{
    /// <summary>Gets a stable pseudonym from a local account identifier, never from its address.</summary>
    public static string AccountKey(string accountId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accountId)))[..24];

    /// <summary>Begins a per-call scope; concurrent reads never share mutable account state.</summary>
    public static IDisposable? Begin(ILogger logger, Account account, string operation)
    {
        var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["Provider"] = account.Provider is "google" or "microsoft" ? account.Provider : "other",
            ["AccountKey"] = AccountKey(account.Id),
            ["ProviderOperation"] = operation,
            ["ReadId"] = Guid.NewGuid().ToString("N")
        });
        logger.LogDebug("Provider operation {ProviderOperation} started", operation);
        return scope;
    }

    /// <summary>Records only exception type and fixed categories, never messages, stacks or inner exceptions.</summary>
    public static void Failure(ILogger logger, Exception exception, string phase)
    {
        logger.LogWarning(
            "Provider failure at {Phase}: type={ErrorType}; category={FailureCategory}; network={NetworkError}",
            phase,
            exception.GetType().Name,
            exception is ProviderReadException provider ? provider.Kind :
                exception is HttpRequestException ? ReadFailureKind.Network :
                exception is OperationCanceledException ? ReadFailureKind.Timeout : ReadFailureKind.Unknown,
            exception is HttpRequestException http ? http.HttpRequestError.ToString() : "none");
    }
}
