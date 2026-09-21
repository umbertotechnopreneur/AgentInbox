using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MailMeUp.Storage;
using Microsoft.Extensions.Logging;

namespace MailMeUp.Desktop.Services;

/// <summary>Prepares and installs the local Codex plugin only after an explicit desktop action.</summary>
public sealed class CodexSetupService(ILogger<CodexSetupService> logger) : IDisposable
{
    private const string PluginId = "agentinbox@agentinbox-local";
    private const string MarketplaceName = "agentinbox-local";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim installationLock = new(1, 1);

    /// <summary>Releases the local installation synchronization primitive.</summary>
    public void Dispose() => installationLock.Dispose();

    /// <summary>Returns reviewable commands and stable paths without creating files or starting Codex.</summary>
    public CodexSetupPreview GetPreview()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local))
        {
            throw new InvalidOperationException("The Windows local application directory is unavailable.");
        }

        var executablePath = ResolveExecutablePath(AppContext.BaseDirectory, local);
        var pluginDirectory = Path.Combine(ResolveDataDirectory(), "codex-plugin");
        var commands = $"codex plugin marketplace add {QuotePowerShell(pluginDirectory)}{Environment.NewLine}"
            + $"codex plugin add {PluginId}{Environment.NewLine}codex plugin list --json";
        var mcpCommand = $"codex mcp add agentinbox --env {QuotePowerShell($"AGENTINBOX_DATA_DIR={ResolveDataDirectory()}")} "
            + $"-- {QuotePowerShell(executablePath)} --stdio";
        return new(executablePath, pluginDirectory, commands, mcpCommand);
    }

    // An explicit package marker prevents a portable setup from selecting an unrelated installed alias.
    internal static string ResolveExecutablePath(string applicationDirectory, string localDirectory,
        Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        return fileExists(Path.Combine(applicationDirectory, "agentinbox-portable.txt"))
            ? Path.GetFullPath(Path.Combine(applicationDirectory, "cli", "agentinbox.exe"))
            : Path.Combine(localDirectory, "Microsoft", "WindowsApps", "agentinbox.exe");
    }

    /// <summary>Copies only the bundled AgentInbox marketplace into its writable local directory.</summary>
    public async Task<CodexSetupPreview> PreparePluginAsync(CancellationToken cancellationToken = default)
    {
        await installationLock.WaitAsync(cancellationToken);
        try
        {
            return await PreparePluginCoreAsync(cancellationToken);
        }
        finally
        {
            installationLock.Release();
        }
    }

    /// <summary>Reads local configuration without starting a prompt, MCP server or mailbox check.</summary>
    public async Task<CodexSetupStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var checks = CodexSetupCheck.Pending().ToDictionary(check => check.Name, check => check.Result);
        var installed = false;
        var enabled = false;
        var hasDirectRegistration = false;
        int? directRegistrationCount = null;
        bool? directRegistrationEnabled = null;
        var otherPlugin = false;
        var otherPluginEnabled = false;
        var collision = false;
        var marketplaceRegistered = false;

        CodexSetupStatus Finish(string code, string message, bool canInstall = false)
        {
            logger.LogInformation("Codex setup inspection result={SetupCode}; canInstall={CanInstall}", code, canInstall);
            foreach (var check in checks)
                logger.LogInformation("Codex setup check={SetupCheck}; result={CheckResult}", check.Key, check.Value);
            return new(code, message, canInstall, installed && enabled, hasDirectRegistration)
            {
                DirectRegistrationCount = directRegistrationCount,
                IsDirectRegistrationEnabled = directRegistrationEnabled,
                Checks = checks.Select(check => new CodexSetupCheck(check.Key, check.Value)).ToArray()
            };
        }

        async Task<bool> InspectAsync(string executable, string name, string[] arguments, Func<string, bool> read)
        {
            checks[name] = "Could not check";
            try
            {
                var result = await RunCodexAsync(executable, arguments, cancellationToken);
                if (!result.Success)
                {
                    checks[name] = result.ExitCode != 0
                        ? $"Command failed (exit {result.ExitCode})"
                        : "Response exceeded the output limit";
                    return false;
                }
                if (!read(result.Output))
                {
                    checks[name] = "Unsupported response";
                    logger.LogWarning("Codex setup check={SetupCheck} returned an unsupported response", name);
                    return false;
                }
                return true;
            }
            catch (Exception exception) when (IsExpectedFailure(exception))
            {
                checks[name] = exception is TimeoutException ? "Timed out" : "Could not read configuration";
                logger.LogWarning("Codex setup check={SetupCheck} failed with {FailureType}", name, exception.GetType().Name);
                return false;
            }
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var preview = GetPreview();
            var aliasAvailable = File.Exists(preview.StableExecutablePath);
            checks["AgentInbox command"] = aliasAvailable ? "Available" : "Not found";
            var executable = FindCodexExecutable();
            checks["Codex CLI"] = executable is null ? "Not found" : "Found (native executable)";
            if (executable is null)
                return Finish("CodexUnavailable", "Automatic setup could not find the native Codex CLI. This does not check whether the Codex desktop app is installed. Manual setup is available below.");

            // Inspect each component independently so one failure does not hide the other findings.
            var mcpKnown = await InspectAsync(executable, "Direct MCP connection", ["mcp", "list", "--json"], output =>
            {
                if (!TryReadDirectRegistration(output, out var observedCount, out var observedEnabled)) return false;
                directRegistrationCount = observedCount;
                directRegistrationEnabled = observedEnabled;
                hasDirectRegistration = observedCount > 0;
                checks["Direct MCP connection"] = observedCount switch
                {
                    0 => "Not registered",
                    1 => observedEnabled switch
                    {
                        true => "Registered and enabled",
                        false => "Registered but disabled",
                        null => "Registration found (enabled state unknown)"
                    },
                    _ => $"{observedCount} registrations found"
                };
                return true;
            });
            var pluginsKnown = await InspectAsync(executable, "AgentInbox plugin", ["plugin", "list", "--json"], output =>
            {
                if (!TryReadPluginState(output, out var observedInstalled, out var observedEnabled, out var observedOther, out var observedOtherEnabled)) return false;
                installed = observedInstalled;
                enabled = observedEnabled;
                otherPlugin = observedOther;
                otherPluginEnabled = observedOtherEnabled;
                checks["AgentInbox plugin"] = installed
                    ? enabled ? "Installed and enabled" : "Installed but disabled"
                    : "Not installed";
                if (otherPlugin) checks["AgentInbox plugin"] += "; another source also found";
                return true;
            });
            var marketplaceKnown = await InspectAsync(executable, "Local marketplace", ["plugin", "marketplace", "list", "--json"], output =>
            {
                if (!TryReadMarketplaceState(output, preview.PluginDirectory, out collision, out marketplaceRegistered)) return false;
                checks["Local marketplace"] = collision ? "Name used by a different source"
                    : marketplaceRegistered ? "Added" : "Not added";
                return true;
            });
            cancellationToken.ThrowIfCancellationRequested();

            if (!aliasAvailable)
                return Finish("AliasUnavailable", "The AgentInbox command was not found. Restore the complete portable folder or the installed Windows app execution alias before configuring Codex.");
            if (!mcpKnown)
                return Finish("ConfigurationUnknown", "The direct MCP check did not finish. Installation is paused because duplicate AgentInbox tools cannot be ruled out.");
            if (!pluginsKnown)
                return Finish("PluginStatusUnknown", "The plugin check did not finish. Review the result below, update Codex if needed, then refresh status. No installation was attempted.");
            if (IsDirectConfigurationReady(directRegistrationCount, directRegistrationEnabled, pluginsKnown, installed && enabled, otherPluginEnabled))
                return Finish("DirectConfigured", "Codex reports one direct AgentInbox connection enabled and no enabled AgentInbox plugin. Start a new Codex task to load its tools; no live connection was tested.");
            if (hasDirectRegistration)
                return Finish("DirectRegistrationExists", installed && enabled
                    ? "A direct MCP registration and the enabled local plugin were both found. Review the two methods before installing or updating."
                    : otherPluginEnabled
                        ? "A direct MCP registration and an enabled AgentInbox plugin from another source were found. Keep one enabled connection method in Codex, then check again."
                        : directRegistrationCount.GetValueOrDefault() > 1
                            ? "Several direct AgentInbox registrations were found. Keep one enabled entry in Codex, then check again."
                            : directRegistrationEnabled == false
                                ? "The direct AgentInbox connection is disabled. Enable it in Codex to keep that setup, or remove it before installing the local plugin."
                                : "A direct AgentInbox registration was found, but its enabled state could not be confirmed. Review it in Codex, then check again.");
            if (otherPlugin)
                return Finish("OtherPluginExists", "An AgentInbox plugin from another marketplace was found. Review that source before adding this local copy.");
            if (!marketplaceKnown)
                return Finish("MarketplaceStatusUnknown", "The marketplace check did not finish. Installation is paused until its source can be confirmed.");
            if (collision)
                return Finish("MarketplaceConflict", "A different source uses the agentinbox-local name. Review that marketplace before installing this copy.");
            if (installed)
                return enabled
                    ? Finish("PluginConfigured", "Codex reports the local plugin installed and enabled. Start a new Codex task to load its tools; no live connection was tested.", true)
                    : Finish("PluginDisabled", "The local plugin is installed but disabled. Enable it in Codex's plugin settings, then refresh status.");

            return Finish("ReadyToInstall", marketplaceRegistered
                ? "The local marketplace is already added. Install the AgentInbox plugin to complete setup."
                : "Install the local plugin to add the AgentInbox marketplace and connect your sharing choices to Codex.", true);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            logger.LogWarning("Codex configuration inspection failed with {FailureType}.", exception.GetType().Name);
            return Finish("ConfigurationUnknown", "Local setup could not be fully inspected. Review the available results below, then refresh status.");
        }
    }

    /// <summary>Installs the app-owned marketplace and plugin, preserving all direct MCP registrations.</summary>
    public async Task<CodexSetupResult> InstallPluginAsync(CancellationToken cancellationToken = default)
    {
        await installationLock.WaitAsync(cancellationToken);
        try
        {
            var status = await GetStatusAsync(cancellationToken);
            if (!status.CanInstall)
            {
                return new(false, status.Message, status);
            }

            var executable = FindCodexExecutable();
            if (executable is null)
            {
                return new(false, "The Codex executable is no longer available. Refresh the setup status.", State("CodexUnavailable", "Codex CLI is unavailable."));
            }

            var preview = await PreparePluginCoreAsync(cancellationToken);
            var marketplace = await RunCodexAsync(executable, ["plugin", "marketplace", "add", preview.PluginDirectory, "--json"], cancellationToken);
            if (!marketplace.Success)
            {
                const string message = "The plugin files are ready, but Codex did not confirm adding the marketplace. Refresh status to inspect what was saved before retrying.";
                return new(false, message, State("InstallationIncomplete", message));
            }

            var install = await RunCodexAsync(executable, ["plugin", "add", PluginId, "--json"], cancellationToken);
            if (!install.Success)
            {
                const string message = "The local marketplace was added, but plugin installation was not confirmed. Refresh status, or review AgentInbox in Codex's plugin settings.";
                return new(false, message, State("InstallationIncomplete", message));
            }

            var refreshed = await GetStatusAsync(cancellationToken);
            var confirmed = refreshed.Code == "PluginConfigured" && refreshed.IsPluginConfigured;
            return new(confirmed, confirmed
                ? "The AgentInbox plugin is installed and enabled. Start a new Codex task to load its tools."
                : "Codex accepted the installation request, but enabled configuration could not be confirmed. Refresh the status or inspect the plugin in Codex.", refreshed);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            logger.LogWarning("Codex plugin installation stopped with {FailureType}.", exception.GetType().Name);
            var status = State("InstallationIncomplete", "Setup did not finish. Some local plugin files or marketplace configuration may already exist. Refresh status before retrying.");
            return new(false, status.Message, status);
        }
        finally
        {
            installationLock.Release();
        }
    }

    private async Task<CodexSetupPreview> PreparePluginCoreAsync(CancellationToken cancellationToken)
    {
        var preview = GetPreview();
        var source = Path.Combine(AppContext.BaseDirectory, "CodexPlugin");
        var mcp = new Dictionary<string, object>
        {
            ["agentinbox"] = new
            {
                command = preview.StableExecutablePath,
                args = new[] { "--stdio" },
                env = new Dictionary<string, string> { ["AGENTINBOX_DATA_DIR"] = ResolveDataDirectory() }
            }
        };
        var mcpContents = JsonSerializer.Serialize(mcp, JsonOptions);
        const string manifestRelativePath = "plugins/agentinbox/.codex-plugin/plugin.json";
        var manifestContents = await File.ReadAllTextAsync(Path.Combine(source, manifestRelativePath), cancellationToken);
        var manifest = System.Text.Json.Nodes.JsonNode.Parse(manifestContents)?.AsObject()
            ?? throw new InvalidDataException("The bundled plugin manifest is missing.");
        var version = manifest["version"]?.GetValue<string>()
            ?? throw new InvalidDataException("The bundled plugin manifest has no version.");
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(manifestContents + mcpContents)))[..16];
        manifest["version"] = $"{version.Split('+')[0]}+codex.{fingerprint}";
        const string marketplaceRelativePath = ".agents/plugins/marketplace.json";
        var marketplace = await File.ReadAllTextAsync(Path.Combine(source, marketplaceRelativePath), cancellationToken);

        await WriteOwnedFileAsync(preview.PluginDirectory, "plugins/agentinbox/.mcp.json", mcpContents, cancellationToken);
        await WriteOwnedFileAsync(preview.PluginDirectory, manifestRelativePath, manifest.ToJsonString(JsonOptions), cancellationToken);
        await WriteOwnedFileAsync(preview.PluginDirectory, marketplaceRelativePath, marketplace, cancellationToken);
        return preview;
    }

    private static async Task WriteOwnedFileAsync(string root, string relative, string contents, CancellationToken cancellationToken)
    {
        var destination = Path.Combine(root, relative);
        var parent = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(parent);
        var temporary = Path.Combine(parent, $".agentinbox-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, contents, new UTF8Encoding(false), cancellationToken);
            File.Move(temporary, destination, true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private async Task<CommandResult> RunCodexAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new IOException("Codex could not be started.");
        process.StandardInput.Close();
        var output = ReadBoundedOutputAsync(process.StandardOutput, timeout.Token);
        var errors = ReadBoundedOutputAsync(process.StandardError, timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var stdout = await output;
            await errors;
            // Arguments are fixed by this service; paths and raw streams are deliberately excluded.
            logger.LogInformation("Codex configuration command={CommandKind} completed with exit code {ExitCode}; outputLimited={OutputLimited}.",
                arguments[0] == "mcp" ? "mcp-list" : arguments.Count > 1 && arguments[1] == "marketplace" ? "marketplace" : "plugin",
                process.ExitCode, stdout is null);
            return new(process.ExitCode == 0 && stdout is not null, stdout ?? string.Empty, process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
            {
                logger.LogWarning("The canceled Codex process could not be terminated: {FailureType}.", exception.GetType().Name);
            }

            try
            {
                await Task.WhenAll(output, errors);
            }
            catch (OperationCanceledException)
            {
                // Both redirected streams are observed before their process is disposed.
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("Codex configuration command exceeded its time limit.");
        }
    }

    private static async Task<string?> ReadBoundedOutputAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        const int maximumCharacters = 1024 * 1024;
        var text = new StringBuilder();
        var buffer = new char[4096];
        var exceeded = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
        {
            if (text.Length + count <= maximumCharacters)
            {
                text.Append(buffer, 0, count);
            }
            else
            {
                exceeded = true;
            }
        }

        return exceeded ? null : text.ToString();
    }

    internal static bool IsDirectConfigurationReady(int? registrationCount, bool? registrationEnabled,
        bool pluginsKnown, bool localPluginEnabled, bool otherPluginEnabled) =>
        registrationCount == 1 && registrationEnabled == true && pluginsKnown && !localPluginEnabled && !otherPluginEnabled;

    internal static bool TryReadDirectRegistration(string output, out int count, out bool? enabled)
    {
        count = 0;
        enabled = null;
        using var document = JsonDocument.Parse(output);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var server in document.RootElement.EnumerateArray())
        {
            if (!TryString(server, "name", out var name))
            {
                return false;
            }

            var matches = IsProductName(name);

            if (server.TryGetProperty("transport", out var transport) && TryString(transport, "command", out var command)
                && IsProductExecutable(command))
            {
                matches = true;
            }

            if (server.TryGetProperty("transport", out transport) && transport.ValueKind == JsonValueKind.Object
                && transport.TryGetProperty("args", out var arguments) && arguments.ValueKind == JsonValueKind.Array
                && arguments.EnumerateArray().Any(argument => argument.ValueKind == JsonValueKind.String
                    && IsProductAssembly(argument.GetString())))
            {
                matches = true;
            }

            if (!matches) continue;
            count++;
            enabled = count == 1 && server.TryGetProperty("enabled", out var enabledValue)
                && enabledValue.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? enabledValue.GetBoolean()
                    : null;
        }

        return true;
    }

    internal static bool TryReadPluginState(string output, out bool installed, out bool enabled, out bool otherPlugin)
        => TryReadPluginState(output, out installed, out enabled, out otherPlugin, out _);

    internal static bool TryReadPluginState(string output, out bool installed, out bool enabled, out bool otherPlugin, out bool otherPluginEnabled)
    {
        installed = false;
        enabled = false;
        otherPlugin = false;
        otherPluginEnabled = false;
        using var document = JsonDocument.Parse(output);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("installed", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (!TryString(entry, "pluginId", out var id) || !TryString(entry, "name", out var name))
            {
                return false;
            }

            if (string.Equals(id, PluginId, StringComparison.Ordinal))
            {
                if (!entry.TryGetProperty("enabled", out var enabledValue)
                    || enabledValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    return false;
                }

                installed = true;
                enabled |= enabledValue.GetBoolean();
            }
            else if (IsProductName(name))
            {
                if (!entry.TryGetProperty("enabled", out var enabledValue)
                    || enabledValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    return false;
                }

                otherPlugin = true;
                otherPluginEnabled |= enabledValue.GetBoolean();
            }
        }

        return true;
    }

    internal static bool TryReadMarketplaceState(string output, string expectedRoot, out bool collision, out bool registered)
    {
        collision = false;
        registered = false;
        using var document = JsonDocument.Parse(output);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("marketplaces", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (!TryString(entry, "name", out var name))
            {
                return false;
            }

            if (name == MarketplaceName)
            {
                registered = true;
                if (!TryString(entry, "root", out var root) || !Path.IsPathFullyQualified(root))
                {
                    return false;
                }

                collision |= !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)),
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(expectedRoot)), StringComparison.OrdinalIgnoreCase);
            }
        }

        return true;
    }

    private static bool TryString(JsonElement value, string property, out string text)
    {
        text = string.Empty;
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(property, out var item) || item.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        text = item.GetString()!;
        return true;
    }

    private static string? FindCodexExecutable()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var unquoted = directory.Trim('"');
            if (!Path.IsPathFullyQualified(unquoted))
            {
                continue;
            }

            var candidate = Path.Combine(unquoted, "codex.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var npm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@openai");
        foreach (var architecture in new[] { "x64", "arm64" })
        {
            var target = architecture == "x64" ? "x86_64-pc-windows-msvc" : "aarch64-pc-windows-msvc";
            string[] packageRoots = [Path.Combine(npm, "codex"), Path.Combine(npm, "codex", "node_modules", "@openai", $"codex-win32-{architecture}")];
            foreach (var packageRoot in packageRoots)
            {
                var candidate = Path.Combine(packageRoot, "vendor", target, "codex", "codex.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static bool IsProductName(string value) =>
        string.Equals(value, "agentinbox", StringComparison.OrdinalIgnoreCase);

    private static bool IsProductExecutable(string path)
    {
        var name = Path.GetFileName(path);
        return string.Equals(name, "agentinbox.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "agentinbox", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProductAssembly(string? path)
    {
        var name = Path.GetFileName(path);
        return string.Equals(name, "agentinbox.dll", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveDataDirectory() => DataDirectory.ResolveFromEnvironment();

    private static string QuotePowerShell(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static CodexSetupStatus State(string code, string message) => new(code, message, false, false, false);

    private static bool IsExpectedFailure(Exception exception) => exception is IOException or UnauthorizedAccessException
        or JsonException or Win32Exception or TimeoutException or InvalidOperationException or ArgumentException;

    private sealed record CommandResult(bool Success, string Output, int ExitCode);
}
