namespace MailMeUp.Core;

/// <summary>Reads aggregate local usage and saves controls for the next process startup.</summary>
public interface IReadGuardrailManagement
{
    /// <summary>Gets active and saved limits without admitting reads or creating usage files.</summary>
    Task<ReadGuardrailStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves validated limits only if the previously displayed settings are still current.</summary>
    Task<ReadGuardrailStatus> SaveLimitsAsync(ReadGuardrailLimits limits, ReadGuardrailLimits expectedLimits,
        CancellationToken cancellationToken = default);
}

/// <summary>Distinguishes this process's active limits from the settings used at the next startup.</summary>
/// <param name="ActiveLimits">Immutable controls enforced by this process.</param>
/// <param name="SavedLimits">Current controls saved for newly started processes.</param>
/// <param name="Usage">Aggregate usage evaluated with the active limits.</param>
public sealed record ReadGuardrailStatus(
    ReadGuardrailLimits ActiveLimits,
    ReadGuardrailLimits SavedLimits,
    ReadGuardrailUsage Usage)
{
    /// <summary>Gets whether this process needs a restart to apply the saved settings.</summary>
    public bool RequiresRestart => ActiveLimits != SavedLimits;
}

/// <summary>Contains aggregate counts and rolling charge expiry times, never account or message identities.</summary>
/// <param name="CapturedAt">Time at which the usage snapshot was evaluated.</param>
/// <param name="ProviderAttemptsInMinute">Provider attempts charged to this profile in the active minute.</param>
/// <param name="ContentReadsInWindow">Content admissions charged in the active read window.</param>
/// <param name="DetailReadsInWindow">Detail admissions charged in the active read window.</param>
/// <param name="OutputBytesInWindow">Serialized output bytes charged in the active read window.</param>
/// <param name="ActiveCooldowns">Number of account/service scopes with a current provider pause.</param>
/// <param name="NextProviderCapacityAt">Earliest expiry of a currently counted provider attempt, if any.</param>
/// <param name="NextContentCapacityAt">Earliest expiry of a currently counted content admission, if any.</param>
/// <param name="NextDetailCapacityAt">Earliest expiry of a currently counted detail admission, if any.</param>
/// <param name="NextOutputCapacityAt">Earliest expiry of a currently counted output charge, if any.</param>
/// <param name="LatestCooldownEndsAt">Latest end of a currently active provider pause, if any.</param>
public sealed record ReadGuardrailUsage(
    DateTimeOffset CapturedAt,
    int ProviderAttemptsInMinute,
    int ContentReadsInWindow,
    int DetailReadsInWindow,
    long OutputBytesInWindow,
    int ActiveCooldowns,
    DateTimeOffset? NextProviderCapacityAt,
    DateTimeOffset? NextContentCapacityAt,
    DateTimeOffset? NextDetailCapacityAt,
    DateTimeOffset? NextOutputCapacityAt,
    DateTimeOffset? LatestCooldownEndsAt);
