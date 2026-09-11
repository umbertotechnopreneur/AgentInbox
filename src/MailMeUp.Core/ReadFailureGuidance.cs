namespace MailMeUp.Core;

/// <summary>Safe failure categories supplied by adapters without carrying provider response content.</summary>
public enum ReadFailureKind
{
    /// <summary>A read failed without a more specific safe diagnosis.</summary>
    Unknown,
    /// <summary>The account must sign in again.</summary>
    SignInRequired,
    /// <summary>The provider denied permission to the requested data.</summary>
    AccessDenied,
    /// <summary>The provider is temporarily unavailable or limiting requests.</summary>
    ProviderUnavailable,
    /// <summary>The provider could not be reached over the network.</summary>
    Network,
    /// <summary>The read did not finish within the allowed time.</summary>
    Timeout,
    /// <summary>The operating system could not open the protected account credentials.</summary>
    LocalCredentialsUnavailable,
    /// <summary>The provider application has not been configured.</summary>
    SetupRequired,
    /// <summary>A previously selected item is no longer available.</summary>
    ItemUnavailable,
    /// <summary>A provider result or continuation limit prevented complete coverage.</summary>
    ResultLimit,
    /// <summary>The selected local account or sharing configuration is unavailable.</summary>
    LocalConfiguration,
    /// <summary>The supplied request or local reference is invalid.</summary>
    InvalidRequest,
    /// <summary>The provider rejected a read because a request or usage limit was reached.</summary>
    RateLimited
}

/// <summary>A plain-English explanation and next step generated from a trusted failure category.</summary>
public sealed record ReadFailureAdvice(string Code, string Explanation, string Action);

/// <summary>Provides bounded user-facing explanations without using exception messages or provider content.</summary>
public static class ReadFailureGuidance
{
    /// <summary>Maps a trusted category to a safe explanation and a practical recovery step.</summary>
    public static ReadFailureAdvice Describe(ReadFailureKind kind) => kind switch
    {
        ReadFailureKind.SignInRequired => new("sign_in_required", "The account's sign-in has expired, was removed, or needs approval again.", "Open MailMeUp and sign in to the affected account again."),
        ReadFailureKind.AccessDenied => new("access_denied", "The email or calendar provider denied read access.", "Open MailMeUp, check the account permissions and sign in again if needed. A work account may require an administrator's approval."),
        ReadFailureKind.ProviderUnavailable => new("provider_unavailable", "The email or calendar provider is temporarily unavailable or is limiting requests.", "Wait a little and try again."),
        ReadFailureKind.RateLimited => new("rate_limited", "The email or calendar provider is limiting requests or has exhausted a usage quota.", "Wait before trying again, use a shorter date range and open only the messages you need. Reconnecting the account does not resolve a request limit."),
        ReadFailureKind.Network => new("network_unavailable", "MailMeUp could not reach the email or calendar provider.", "Check the internet connection and try again."),
        ReadFailureKind.Timeout => new("read_timed_out", "The email or calendar provider took too long to respond and the read timed out.", "Try again, or narrow the search to a shorter date range."),
        ReadFailureKind.LocalCredentialsUnavailable => new("local_credentials_unavailable", "MailMeUp could not open the account's protected sign-in information on this computer.", "Open MailMeUp and try signing in again. If the problem continues, check that the computer's credential storage is available."),
        ReadFailureKind.SetupRequired => new("setup_required", "The Google or Microsoft connection has not been fully configured in MailMeUp.", "Open MailMeUp and complete the provider setup."),
        ReadFailureKind.ItemUnavailable => new("item_unavailable", "The selected message or appointment is no longer available from the provider.", "Refresh the search and select an available item."),
        ReadFailureKind.ResultLimit => new("results_incomplete", "The provider could not finish returning all of the requested results.", "Use a shorter date range or a more specific search."),
        ReadFailureKind.LocalConfiguration => new("local_configuration", "The selected account, sharing choices or local MailMeUp settings are unavailable or changed during the read.", "Open MailMeUp, check the connected accounts and sharing choices, then start a new request."),
        ReadFailureKind.InvalidRequest => new("invalid_request", "The read request is invalid, or a selected result reference has expired.", "Refresh the search and check the requested accounts, dates and result limits."),
        _ => new("read_failed", "MailMeUp could not complete the requested read.", "Try again. If the problem continues, open MailMeUp and check the affected account.")
    };

}
