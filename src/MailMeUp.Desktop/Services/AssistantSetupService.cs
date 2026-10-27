/* VBWR B
 *
 * Project: AgentInbox
 * Repository: https://github.com/umbertotechnopreneur/AgentInbox
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * File: Explicit assistant configuration, concurrent-edit checks and owned staging.
 * Modified with AI: OpenAI Codex; code readability and documentation, 2026-10-27.
 * Human guidance: Umberto Giacobbi; document parameters and simplify maintenance.
 * Evidence: docs/VALIDATION.md (source review; execution checks pending).
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 * SPDX-License-Identifier: MIT
 *
 * VBWR E */

using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MailMeUp.Desktop.Services;

/// <summary>Describes local client configuration without claiming that its MCP connection was tested.</summary>
/// <param name="ClientId">Stable identifier of the supported assistant client.</param>
/// <param name="DisplayName">User-facing assistant name.</param>
/// <param name="IsDetected">Whether the installed client executable was found.</param>
/// <param name="CanConnect">Whether guided setup can offer a connection action.</param>
/// <param name="IsConfigured">Whether saved settings match this installation and enable the entry.</param>
/// <param name="RequiresReplacement">Whether connecting would replace an existing AgentInbox entry.</param>
/// <param name="ConfigurationPath">Resolved client configuration file path.</param>
/// <param name="Message">Safe presentation guidance or outcome explanation.</param>
/// <param name="SnapshotToken">Fingerprint used to reject changes made since detection.</param>
internal sealed record AssistantClientStatus(
    string ClientId, string DisplayName, bool IsDetected, bool CanConnect, bool IsConfigured,
    bool RequiresReplacement, string ConfigurationPath, string Message, string SnapshotToken);

/// <summary>Reports a configuration write separately from verification of a live assistant connection.</summary>
/// <param name="ConfigurationWritten">Whether the configuration write committed.</param>
/// <param name="ConnectionVerified">Whether a local connection check actually completed.</param>
/// <param name="Message">Safe presentation guidance or outcome explanation.</param>
/// <param name="Status">Updated assistant detection snapshot.</param>
/// <param name="CleanupPending">Whether temporary setup files still require cleanup after the primary outcome.</param>
internal sealed record AssistantSetupResult(
    bool ConfigurationWritten, bool ConnectionVerified, string Message, AssistantClientStatus Status,
    bool CleanupPending = false);

/// <summary>Guides explicit Codex setup using its installed CLI and the Python engine's connection description.</summary>
internal sealed partial class AssistantSetupService
{
    private const int MaximumConfigurationBytes = 2 * 1024 * 1024;
    private const int MaximumOutputCharacters = 1024 * 1024;
    private const string ServerName = "agentinbox";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly SemaphoreSlim _configurationLock = new(1, 1);
    private readonly ConnectionDescription _connection;
    private readonly string _connectionFingerprint;

    /// <summary>Accepts only the secret-free configuration produced by PythonEngine.CreateClientConfiguration.</summary>
    /// <param name="clientConfigurationJson">Secret-free MCP launcher JSON produced by PythonEngine.</param>
    internal AssistantSetupService(string clientConfigurationJson)
    {
        _connection = ReadConnection(clientConfigurationJson);
        _connectionFingerprint = Convert.ToHexString(SHA256.HashData(Utf8.GetBytes(clientConfigurationJson)));
    }

    /// <summary>Detects Codex and reads its local MCP configuration without starting any mail server or prompt.</summary>
    /// <param name="cancellationToken">Cancellation for the requested operation and any owned child process.</param>
    internal async Task<IReadOnlyList<AssistantClientStatus>> DetectAsync(CancellationToken cancellationToken)
    {
        string? executable;
        try { executable = FindCodexExecutable(); }
        catch (Exception error) when (IsExpectedFailure(error))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return [Status(false, "", "Codex detection could not finish. Open Codex and refresh detection.")];
        }
        string configurationPath;
        try { configurationPath = Path.Combine(ResolveCodexHome(), "config.toml"); }
        catch (InvalidOperationException error)
        {
            return [Status(executable is not null, "", error.Message)];
        }
        if (executable is null)
            return [Status(false, configurationPath,
                "Open or update the Codex app, then choose Check Codex again. You can also use the manual connection settings below.")];
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = ReadSnapshot(configurationPath);
            var servers = await ReadServersAsync(executable, Path.GetDirectoryName(configurationPath)!, cancellationToken);
            if (ReadSnapshot(configurationPath).Fingerprint != snapshot.Fingerprint)
                return [Status(true, configurationPath, "Codex settings changed during detection. Refresh before connecting.")];
            var token = SnapshotToken(executable, configurationPath, snapshot.Fingerprint);
            if (!File.Exists(_connection.Command))
                return [Status(true, configurationPath,
                    "The AgentInbox launch command is unavailable. Restore the installed app execution alias or repair this installation.", token)];
            if (servers.DuplicateConnection)
                return [Status(true, configurationPath,
                    "Another Codex entry already launches AgentInbox. Review that entry in Codex before adding a duplicate connection.", token)];
            if (servers.AgentInbox is { } existing)
            {
                var matches = ConnectionMatches(existing);
                var enabled = existing.TryGetProperty("enabled", out var enabledValue)
                    && enabledValue.ValueKind == JsonValueKind.True;
                if (matches && enabled)
                    return [new("codex", "Codex", true, false, true, false, configurationPath,
                        "Codex has the matching AgentInbox settings, and this connection is enabled. Use Test connection to check that AgentInbox can start and reply.", token)];
                return [new("codex", "Codex", true, true, false, true, configurationPath,
                    "Codex already has an AgentInbox connection. Update it to use the settings from this app.", token)];
            }
            return [new("codex", "Codex", true, true, false, false, configurationPath,
                "Codex is installed. Add AgentInbox so it can use the email accounts you choose to share.", token)];
        }
        catch (Exception error) when (IsExpectedFailure(error))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return [Status(true, configurationPath,
                "Codex configuration could not be inspected safely. Close any configuration editor, check Codex settings and refresh detection.")];
        }
    }

    /// <summary>Writes only the selected AgentInbox entry after an explicit action and an unchanged detection snapshot.</summary>
    /// <param name="detected">Detection snapshot to compare with current assistant settings before acting.</param>
    /// <param name="cancellationToken">Cancellation for the requested operation and any owned child process.</param>
    /// <param name="replaceExisting">Whether the user explicitly confirmed replacing the existing AgentInbox entry.</param>
    internal async Task<AssistantSetupResult> ConnectAsync(AssistantClientStatus detected,
        CancellationToken cancellationToken, bool replaceExisting = false)
    {
        if (detected.ClientId != "codex") throw new InvalidOperationException("This assistant is not supported by guided setup.");
        await _configurationLock.WaitAsync(cancellationToken);
        string? stagingDirectory = null;
        AssistantSetupResult? result = null;
        Exception? operationFailure = null;
        var cleanupPending = false;
        try
        {
            var current = (await DetectAsync(cancellationToken))[0];
            if (string.IsNullOrEmpty(detected.SnapshotToken) || current.SnapshotToken != detected.SnapshotToken)
                throw new InvalidOperationException("The detected configuration changed. Refresh detection and review it before connecting.");
            if (current.IsConfigured)
                return new(false, false, current.Message, current);
            if (!current.CanConnect) throw new InvalidOperationException(current.Message);
            if (current.RequiresReplacement && !replaceExisting)
                throw new InvalidOperationException("Confirm replacement of the existing AgentInbox entry before updating it.");

            var executable = FindCodexExecutable()
                ?? throw new InvalidOperationException("The Codex command is no longer available. Refresh detection.");
            var home = Path.GetDirectoryName(current.ConfigurationPath)!;
            var original = ReadSnapshot(current.ConfigurationPath);
            if (SnapshotToken(executable, current.ConfigurationPath, original.Fingerprint) != current.SnapshotToken)
                throw new InvalidOperationException("Codex settings changed. Refresh detection before connecting.");

            EnsureUnredirectedPath(home);
            Directory.CreateDirectory(home);
            stagingDirectory = Path.Combine(home, ".agentinbox-setup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingDirectory);
            var stagedConfiguration = Path.Combine(stagingDirectory, "config.toml");
            if (original.Contents is not null)
                await File.WriteAllBytesAsync(stagedConfiguration, original.Contents, cancellationToken);

            // Codex edits its own TOML in an isolated home; no server or agent task is started.
            // Official format: https://learn.chatgpt.com/docs/extend/mcp?surface=cli
            var arguments = new List<string> { "mcp", "add", ServerName };
            foreach (var variable in _connection.Environment)
            {
                arguments.Add("--env");
                arguments.Add(variable.Key + "=" + variable.Value);
            }
            arguments.Add("--");
            arguments.Add(_connection.Command);
            arguments.AddRange(_connection.Arguments);
            await RunCodexAsync(executable, arguments, stagingDirectory, cancellationToken);
            var stagedServers = await ReadServersAsync(executable, stagingDirectory, cancellationToken);
            if (stagedServers.AgentInbox is not { } stagedEntry || !ConnectionMatches(stagedEntry)
                || !stagedEntry.TryGetProperty("enabled", out var enabled) || enabled.ValueKind != JsonValueKind.True)
                throw new InvalidOperationException("Codex did not produce the requested AgentInbox configuration. Nothing was installed.");
            var prepared = ReadSnapshot(stagedConfiguration);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureUnredirectedPath(current.ConfigurationPath);
            if (ReadSnapshot(current.ConfigurationPath).Fingerprint != original.Fingerprint)
                throw new InvalidOperationException("Codex settings changed while preparing the connection. Nothing was replaced; refresh detection.");

            // The staged file is on the same volume. A failed preparation leaves the live file intact.
            if (original.Contents is null)
                File.Move(stagedConfiguration, current.ConfigurationPath, overwrite: false);
            else
                File.Replace(stagedConfiguration, current.ConfigurationPath, destinationBackupFileName: null);
            var saved = current with
            {
                CanConnect = false, IsConfigured = true, RequiresReplacement = false,
                SnapshotToken = SnapshotToken(executable, current.ConfigurationPath, prepared.Fingerprint),
                Message = "Your AgentInbox settings are saved in Codex. Use Test connection, then try a new Codex chat."
            };
            result = new(true, false, saved.Message, saved);
        }
        catch (OperationCanceledException error)
        {
            operationFailure = error;
            throw;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or Win32Exception)
        {
            operationFailure = new InvalidOperationException("The Codex configuration could not be updated. Refresh detection to inspect the current state.");
            throw operationFailure;
        }
        catch (Exception error)
        {
            operationFailure = error;
            throw;
        }
        finally
        {
            try
            {
                if (stagingDirectory is not null) RemoveStagingDirectory(stagingDirectory);
            }
            catch (Exception error) when (IsExpectedFailure(error))
            {
                cleanupPending = true;
                if (operationFailure is not null) operationFailure.Data["AgentInboxCleanupPending"] = true;
            }
            finally { _configurationLock.Release(); }
        }
        if (result is null) throw new InvalidOperationException("Codex setup did not produce a configuration result.");
        return cleanupPending ? result with
        {
            CleanupPending = true,
            Message = result.Message + " Some temporary setup files could not be removed. See the Codex settings folder shown on this page. You do not need to connect again."
        } : result;
    }

    /// <summary>Describe a detection outcome without claiming an MCP connection was verified.</summary>
    /// <param name="detected">Detection snapshot to compare with current assistant settings before acting.</param>
    /// <param name="path">Configuration file path or observable property path consumed by the adapter.</param>
    /// <param name="message">Safe user-facing outcome or guidance; never raw credentials or mail content.</param>
    /// <param name="token">Configuration snapshot fingerprint, empty when detection produced none.</param>
    private static AssistantClientStatus Status(bool detected, string path, string message, string token = "")
        => new("codex", "Codex", detected, false, false, false, path, message, token);

    /// <summary>Fingerprint the executable, settings path and observed configuration contents.</summary>
    /// <param name="executable">Resolved Codex executable path; arguments are passed without shell expansion.</param>
    /// <param name="path">Configuration file path or observable property path consumed by the adapter.</param>
    /// <param name="configurationFingerprint">Hash of the configuration bytes observed during detection.</param>
    private string SnapshotToken(string executable, string path, string configurationFingerprint)
        => Convert.ToHexString(SHA256.HashData(Utf8.GetBytes(
            executable + "\n" + path + "\n" + configurationFingerprint + "\n" + _connectionFingerprint)));

    /// <summary>Resolve the explicit or default Codex configuration directory.</summary>
    private static string ResolveCodexHome()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (configured is not null)
        {
            if (!Path.IsPathFullyQualified(configured))
                throw new InvalidOperationException("CODEX_HOME must identify an absolute directory. Correct it and refresh detection.");
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(configured));
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    }

    /// <summary>Find the installed Codex CLI used for explicit configuration operations.</summary>
    private static string? FindCodexExecutable()
    {
        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var directory = entry.Trim().Trim('"');
            if (!Path.IsPathFullyQualified(directory)) continue;
            var candidate = Path.Combine(directory, "codex.exe");
            if (File.Exists(candidate)) return candidate;
        }
        var appDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        if (Directory.Exists(appDirectory))
        {
            var direct = Path.Combine(appDirectory, "codex.exe");
            if (File.Exists(direct)) return direct;
            foreach (var directory in Directory.EnumerateDirectories(appDirectory).Take(64)
                         .OrderByDescending(Directory.GetLastWriteTimeUtc))
            {
                var candidate = Path.Combine(directory, "codex.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }
        var npm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@openai", "codex");
        foreach (var architecture in new[] { (Name: "x64", Target: "x86_64-pc-windows-msvc"), (Name: "arm64", Target: "aarch64-pc-windows-msvc") })
        {
            foreach (var package in new[] { npm, Path.Combine(npm, "node_modules", "@openai", "codex-win32-" + architecture.Name) })
            {
                var candidate = Path.Combine(package, "vendor", architecture.Target, "codex", "codex.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    /// <summary>Read Codex MCP entries and identify matching or duplicate AgentInbox launchers.</summary>
    /// <param name="executable">Resolved Codex executable path; arguments are passed without shell expansion.</param>
    /// <param name="home">Explicit Codex configuration directory used by this child operation.</param>
    /// <param name="cancellationToken">Cancellation for the requested operation and any owned child process.</param>
    private async Task<ServerListing> ReadServersAsync(string executable, string home, CancellationToken cancellationToken)
    {
        var output = await RunCodexAsync(executable, ["mcp", "list", "--json"], home, cancellationToken);
        using var document = JsonDocument.Parse(output, new JsonDocumentOptions { MaxDepth = 32 });
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Codex returned an unsupported configuration response.");
        JsonElement? match = null;
        var duplicate = false;
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            if (!entry.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String)
                throw new InvalidOperationException("Codex returned an incomplete server description.");
            if (name.GetString() == ServerName)
            {
                if (match is not null) throw new InvalidOperationException("Codex reported duplicate AgentInbox entries.");
                match = entry.Clone();
            }
            else if (entry.TryGetProperty("transport", out var transport)
                && transport.TryGetProperty("command", out var command) && command.ValueKind == JsonValueKind.String)
            {
                duplicate |= string.Equals(Path.GetFileName(command.GetString()), "AgentInbox.exe", StringComparison.OrdinalIgnoreCase)
                    || ConnectionMatches(entry);
            }
        }
        return new(match, duplicate);
    }

    /// <summary>Compare a saved MCP entry with this installation command, arguments and environment.</summary>
    /// <param name="entry">One saved MCP server entry to compare with the expected launcher.</param>
    private bool ConnectionMatches(JsonElement entry)
    {
        if (!entry.TryGetProperty("transport", out var transport) || transport.ValueKind != JsonValueKind.Object
            || !transport.TryGetProperty("type", out var type) || type.GetString() != "stdio"
            || !transport.TryGetProperty("command", out var command) || command.GetString() != _connection.Command
            || !transport.TryGetProperty("args", out var args) || args.ValueKind != JsonValueKind.Array
            || !transport.TryGetProperty("env", out var environment) || environment.ValueKind != JsonValueKind.Object)
            return false;
        if (!args.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() : null)
                .SequenceEqual(_connection.Arguments)) return false;
        var variables = environment.EnumerateObject().ToArray();
        return variables.Length == _connection.Environment.Count && variables.All(variable =>
            variable.Value.ValueKind == JsonValueKind.String && _connection.Environment.TryGetValue(variable.Name, out var expected)
            && variable.Value.GetString() == expected);
    }

    /// <summary>Validate the secret-free MCP launch configuration produced by the engine adapter.</summary>
    /// <param name="json">Secret-free local MCP launcher JSON to validate.</param>
    private static ConnectionDescription ReadConnection(string json)
    {
        if (json.Length > 65_536) throw new InvalidOperationException("The AgentInbox client configuration is too large.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1
            || !root.TryGetProperty("mcpServers", out var servers) || servers.ValueKind != JsonValueKind.Object
            || servers.EnumerateObject().Count() != 1 || !servers.TryGetProperty(ServerName, out var server)
            || server.ValueKind != JsonValueKind.Object || server.EnumerateObject().Count() != 3)
            throw new InvalidOperationException("Use the AgentInbox-generated client configuration.");
        var command = server.GetProperty("command").GetString();
        if (command is null || !Path.IsPathFullyQualified(command) || command.Any(char.IsControl))
            throw new InvalidOperationException("The AgentInbox launch command must be an absolute path.");
        var arguments = server.GetProperty("args").EnumerateArray().Select(value => value.GetString()).ToArray();
        if (arguments.Length is 0 or > 20 || arguments.Any(value => value is null || value.Length > 8192 || value.Any(char.IsControl))
            || arguments[^1] != "mcp")
            throw new InvalidOperationException("The AgentInbox launch arguments are invalid.");
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var variable in server.GetProperty("env").EnumerateObject())
        {
            var value = variable.Value.GetString();
            if (variable.Name != "AGENTINBOX_DATA_DIR"
                || value is null || !Path.IsPathFullyQualified(value) || value.Any(char.IsControl)
                || !environment.TryAdd(variable.Name, value))
                throw new InvalidOperationException("The AgentInbox client configuration contains an unsupported environment value.");
        }
        if (!environment.ContainsKey("AGENTINBOX_DATA_DIR"))
            throw new InvalidOperationException("The explicit AgentInbox data directory is required.");
        return new(command, arguments.Select(value => value!).ToArray(), environment);
    }

    /// <summary>Read bounded configuration bytes and fingerprint them for concurrent-edit detection.</summary>
    /// <param name="path">Configuration file path or observable property path consumed by the adapter.</param>
    private static ConfigurationSnapshot ReadSnapshot(string path)
    {
        EnsureUnredirectedPath(path);
        if (!File.Exists(path)) return new(null, "missing");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumConfigurationBytes)
            throw new InvalidOperationException("The Codex configuration exceeds the supported size.");
        var contents = new byte[checked((int)stream.Length)];
        stream.ReadExactly(contents);
        return new(contents, Convert.ToHexString(SHA256.HashData(contents)));
    }

    /// <summary>Reject redirected configuration paths before reading or replacing files.</summary>
    /// <param name="path">Configuration file path or observable property path consumed by the adapter.</param>
    private static void EnsureUnredirectedPath(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current))
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Guided setup cannot modify redirected Codex configuration paths. Use advanced configuration.");
        }
    }

    /// <summary>Remove the owned setup staging directory after rejecting redirected paths.</summary>
    /// <param name="directory">Exact owned staging directory; redirected paths must be rejected.</param>
    private static void RemoveStagingDirectory(string directory)
    {
        EnsureUnredirectedPath(directory);
        var name = Path.GetFileName(directory);
        if (!name.StartsWith(".agentinbox-setup-", StringComparison.Ordinal) || !Guid.TryParseExact(name[18..], "N", out _))
            throw new InvalidOperationException("The temporary setup directory could not be safely removed.");
        if (!Directory.Exists(directory)) return;
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("A redirected item was found in temporary setup output.");
            if (Directory.Exists(entry)) RemoveStagingContents(entry);
            else File.Delete(entry);
        }
        Directory.Delete(directory);
    }

    /// <summary>Remove only the files and subdirectories of an owned setup staging directory.</summary>
    /// <param name="directory">Exact owned staging directory; redirected paths must be rejected.</param>
    private static void RemoveStagingContents(string directory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("A redirected item was found in temporary setup output.");
            if (Directory.Exists(entry)) RemoveStagingContents(entry);
            else File.Delete(entry);
        }
        Directory.Delete(directory);
    }

    /// <summary>Run one bounded Codex configuration command without shell expansion.</summary>
    /// <param name="executable">Resolved Codex executable path; arguments are passed without shell expansion.</param>
    /// <param name="arguments">Operation arguments or activation payload passed without shell expansion.</param>
    /// <param name="home">Explicit Codex configuration directory used by this child operation.</param>
    /// <param name="cancellationToken">Cancellation for the requested operation and any owned child process.</param>
    private static async Task<string> RunCodexAsync(string executable, IEnumerable<string> arguments, string home,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Directory.Exists(home) ? home : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        start.Environment["CODEX_HOME"] = home;
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Codex could not start.");
        using var cancellation = timeout.Token.Register(() => Stop(process));
        process.StandardInput.Close();
        var output = ReadBoundedAsync(process.StandardOutput, timeout.Token, retain: true);
        var errors = ReadBoundedAsync(process.StandardError, timeout.Token, retain: false);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var stdout = await output;
            await errors;
            if (process.ExitCode != 0 || stdout is null)
                throw new InvalidOperationException("Codex did not complete the configuration command. Check its settings and refresh detection.");
            return stdout;
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Codex configuration inspection timed out. Close Codex setup dialogs and try again.");
        }
        finally
        {
            Stop(process);
            try { await Task.WhenAll(output, errors); }
            catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
        }
    }

    /// <summary>Drain bounded child output, optionally retaining it for protocol parsing.</summary>
    /// <param name="reader">Redirected child stream read in bounded chunks.</param>
    /// <param name="cancellationToken">Cancellation for the requested operation and any owned child process.</param>
    /// <param name="retain">Whether to retain bounded output; false drains it without keeping content.</param>
    private static async Task<string?> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken, bool retain)
    {
        var output = new StringBuilder();
        var buffer = new char[4096];
        var exceeded = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
        {
            if (!retain) continue;
            if (output.Length + count <= MaximumOutputCharacters) output.Append(buffer, 0, count);
            else exceeded = true;
        }
        return exceeded ? null : output.ToString();
    }

    /// <summary>Stop the owned child process when it has not already exited.</summary>
    /// <param name="process">Owned child process whose protocol streams or lifetime are being handled.</param>
    private static void Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }

    /// <summary>Identify configuration and process failures that can become safe local guidance.</summary>
    /// <param name="error">Exception classified for safe local failure handling.</param>
    private static bool IsExpectedFailure(Exception error)
        => error is IOException or UnauthorizedAccessException or JsonException or Win32Exception
            or InvalidOperationException or ArgumentException;

    /// <summary>Describe the typed local operation contract.</summary>
    /// <param name="Command">Resolved process command for the MCP launcher.</param>
    /// <param name="Arguments">Launcher arguments passed as separate process arguments.</param>
    /// <param name="Environment">Non-secret environment required for the selected profile.</param>
    private sealed record ConnectionDescription(string Command, string[] Arguments, Dictionary<string, string> Environment);
    /// <summary>Describe the typed local operation contract.</summary>
    /// <param name="Contents">Observed configuration bytes, or null when the file is absent.</param>
    /// <param name="Fingerprint">Hash of the observed configuration contents.</param>
    private sealed record ConfigurationSnapshot(byte[]? Contents, string Fingerprint);
    /// <summary>Describe the typed local operation contract.</summary>
    /// <param name="AgentInbox">Located AgentInbox MCP entry, if present.</param>
    /// <param name="DuplicateConnection">Whether another entry launches the same AgentInbox server.</param>
    private sealed record ServerListing(JsonElement? AgentInbox, bool DuplicateConnection);
}
