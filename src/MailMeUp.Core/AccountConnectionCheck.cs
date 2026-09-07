namespace MailMeUp.Core;

/// <summary>Records how far a read check actually progressed, separately from connectivity.</summary>
public enum ReadCheckEvidence
{
    /// <summary>No successful read was verified.</summary>
    None,
    /// <summary>Search worked, but no item was available for a detail check.</summary>
    SearchOnly,
    /// <summary>Search and a selected item's detail read both worked.</summary>
    SampleRead
}

/// <summary>Reports whether a locally connected account can reach each of its enabled read services.</summary>
public sealed record AccountConnectionCheck(
    string AccountId,
    bool Reachable,
    ReadFailureKind? FailureKind = null,
    bool? MailReachable = null,
    ReadFailureKind? MailFailureKind = null,
    bool? CalendarReachable = null,
    ReadFailureKind? CalendarFailureKind = null,
    ReadCheckEvidence MailEvidence = ReadCheckEvidence.None,
    ReadCheckEvidence CalendarEvidence = ReadCheckEvidence.None)
{
    /// <summary>Rejects contradictory success flags and missing capability outcomes.</summary>
    public bool HasFailures => !Reachable || FailureKind is not null || MailReachable == false ||
        CalendarReachable == false || MailFailureKind is not null || CalendarFailureKind is not null ||
        (MailReachable is null && CalendarReachable is null);

    /// <summary>True only when every checked capability has search and detail evidence, without failures.</summary>
    public bool SampleChecksPassed => !HasFailures &&
        (MailReachable is null || MailEvidence == ReadCheckEvidence.SampleRead) &&
        (CalendarReachable is null || CalendarEvidence == ReadCheckEvidence.SampleRead);
}

/// <summary>Returns the safe read-capability status for every account registered on this device.</summary>
public sealed record AccountConnectionCheckResult(IReadOnlyList<AccountConnectionCheck> Accounts);

/// <summary>Performs bounded read checks without returning mail, calendar, or credential data.</summary>
public interface IAccountConnectionChecker
{
    /// <summary>Gets the stable provider identifier.</summary>
    string ProviderId { get; }

    /// <summary>Checks every read service enabled for one locally connected account and reports them independently.</summary>
    Task<AccountConnectionCheck> CheckAsync(Account account, CancellationToken cancellationToken = default);
}
