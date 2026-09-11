namespace MailMeUp.Tests;

internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public static TimeProvider September2026 { get; } = new FixedTimeProvider(new DateTimeOffset(2026, 9, 11, 8, 0, 0, TimeSpan.Zero));

    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;
}
