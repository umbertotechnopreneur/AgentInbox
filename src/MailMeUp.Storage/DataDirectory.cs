namespace MailMeUp.Storage;

/// <summary>Resolves account metadata storage independently of the current working directory.</summary>
public static class DataDirectory
{
    /// <summary>Uses the AgentInbox environment override or the default local profile.</summary>
    public static string ResolveFromEnvironment()
    {
        return Resolve(Environment.GetEnvironmentVariable("AGENTINBOX_DATA_DIR"));
    }

    /// <summary>Uses an explicit absolute override or the platform's per-user local application directory.</summary>
    public static string Resolve(string? overridePath = null)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            if (!Path.IsPathFullyQualified(overridePath))
            {
                throw new ArgumentException("AGENTINBOX_DATA_DIR must be an absolute path.", nameof(overridePath));
            }

            return Path.GetFullPath(overridePath);
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(local))
        {
            throw new InvalidOperationException("No local application directory is available. Set AGENTINBOX_DATA_DIR to an absolute path.");
        }

        return Path.Combine(local, "AgentInbox");
    }
}
