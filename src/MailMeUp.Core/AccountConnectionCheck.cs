namespace MailMeUp.Core;

/// <summary>Reports whether a locally connected account can reach each of its enabled read services.</summary>
public sealed record AccountConnectionCheck(
    string AccountId,
    bool Reachable,
    ReadFailureKind? FailureKind = null,
    bool? MailReachable = null,
    ReadFailureKind? MailFailureKind = null,
    bool? CalendarReachable = null,
    ReadFailureKind? CalendarFailureKind = null);

/// <summary>Returns the safe read-capability status for every account registered on this device.</summary>
public sealed record AccountConnectionCheckResult(IReadOnlyList<AccountConnectionCheck> Accounts);

/// <summary>Performs a minimal provider check without returning mail, calendar, or credential data.</summary>
public interface IAccountConnectionChecker
{
    /// <summary>Gets the stable provider identifier.</summary>
    string ProviderId { get; }

    /// <summary>Checks every read service enabled for one locally connected account and reports them independently.</summary>
    Task<AccountConnectionCheck> CheckAsync(Account account, CancellationToken cancellationToken = default);
}
