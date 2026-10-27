/* VBWR B
 *
 * Project: AgentInbox
 * Repository: https://github.com/umbertotechnopreneur/AgentInbox
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * File: Bounded Python management-process adapter and stable MCP launcher settings.
 * Modified with AI: OpenAI Codex; code readability and documentation, 2026-10-27.
 * Human guidance: Umberto Giacobbi; document parameters and simplify maintenance.
 * Evidence: docs/VALIDATION.md (source review; execution checks pending).
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 * SPDX-License-Identifier: MIT
 *
 * VBWR E */

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace MailMeUp.Desktop.Services;

/// <summary>Passes local configuration commands to the Python engine without command-line secrets.</summary>
internal sealed class PythonEngine
{
    private const int MaximumResponseCharacters = 1_048_576;
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    /// <summary>Runs one local operation, optionally forwarding real account-check progress records.</summary>
    /// <param name="operation">Explicit operation name or asynchronous action forwarded to the shared model.</param>
    /// <param name="arguments">Operation arguments or activation payload passed without shell expansion.</param>
    /// <param name="cancellationToken">Cancellation for the requested operation and any owned child process.</param>
    /// <param name="progress">Optional account-check progress callback or one completed engine stage update.</param>
    /// <param name="diagnostic">Optional callback for bounded, redacted local sign-in details.</param>
    internal async Task<JsonElement> ManageAsync(string operation, object arguments, CancellationToken cancellationToken,
        Action<AccountCheckProgress>? progress = null, Action<string>? diagnostic = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        using var process = Start("manage");
        using var cancellation = timeout.Token.Register(() => Stop(process));
        try
        {
            var request = new Dictionary<string, object> { ["operation"] = operation, ["arguments"] = arguments };
            if (progress is not null) request["progress"] = true;
            if (diagnostic is not null) request["diagnostics"] = true;
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), timeout.Token);
            process.StandardInput.Close();
            var result = await ReadResponseAsync(process, timeout.Token, progress, diagnostic);
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0) throw new EngineException("The Python engine exited before completing the request.");
            return result;
        }
        finally { Stop(process); }
    }

    /// <summary>Describe the local MCP launcher using a stable packaged alias when available.</summary>
    internal string CreateClientConfiguration()
    {
        var packaged = HasPackageIdentity();
        var environment = new Dictionary<string, string>
        {
            ["AGENTINBOX_DATA_DIR"] = ResolveDataDirectory(packaged)
        };
        string command;
        string[] arguments;
        if (packaged)
        {
            // The execution alias survives package updates; WindowsApps package paths do not.
            command = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WindowsApps", "AgentInbox.exe");
            arguments = ["-B", "-m", "agentinbox", "mcp"];
        }
        else
        {
            command = ResolvePython();
            arguments = ["-B", ResolveEntryPoint(), "mcp"];
        }
        return JsonSerializer.Serialize(new
        {
            mcpServers = new Dictionary<string, object>
            {
                ["agentinbox"] = new
                {
                    command,
                    args = arguments,
                    env = environment
                }
            }
        }, IndentedJson);
    }

    /// <summary>Start the Python management adapter with redirected streams and no command-line secrets.</summary>
    /// <param name="arguments">Operation arguments or activation payload passed without shell expansion.</param>
    private static Process Start(params string[] arguments)
    {
        var info = new ProcessStartInfo(ResolvePython())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        info.ArgumentList.Add("-B");
        info.ArgumentList.Add(ResolveEntryPoint());
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        info.Environment["AGENTINBOX_DATA_DIR"] = ResolveDataDirectory();
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        var process = new Process { StartInfo = info };
        try
        {
            if (!process.Start()) throw new EngineException("The configured Python runtime could not be started.");
            _ = DiscardDiagnosticsAsync(process.StandardError);
            return process;
        }
        catch
        {
            Stop(process);
            process.Dispose();
            throw new EngineException("Python could not start. Configure AGENTINBOX_PYTHON with the absolute path of an installed Python runtime.");
        }
    }

    /// <summary>Drain child stderr in bounded chunks without retaining private diagnostics.</summary>
    /// <param name="reader">Redirected child stream read in bounded chunks.</param>
    private static async Task DiscardDiagnosticsAsync(StreamReader reader)
    {
        // Discard bounded chunks; do not retain even one potentially unlimited stderr line.
        var buffer = new char[4096];
        try { while (await reader.ReadAsync(buffer.AsMemory()) > 0) { } }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    /// <summary>Find the Python source entry point shipped with the desktop application.</summary>
    private static string ResolveEntryPoint()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Python", "run.py");
        if (!File.Exists(path)) throw new EngineException("The Python engine is missing from this installation.");
        return path;
    }

    /// <summary>Resolve the explicit, bundled or available Python executable without installer aliases.</summary>
    private static string ResolvePython()
    {
        var configured = Environment.GetEnvironmentVariable("AGENTINBOX_PYTHON");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!Path.IsPathFullyQualified(configured) || !File.Exists(configured))
                throw new EngineException("AGENTINBOX_PYTHON must identify an existing Python executable using an absolute path.");
            return Path.GetFullPath(configured);
        }
        var bundled = Path.Combine(AppContext.BaseDirectory, "runtime", "python.exe");
        if (File.Exists(bundled)) return bundled;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (!Path.IsPathFullyQualified(directory.Trim('"'))) continue;
            var candidate = Path.Combine(directory.Trim('"'), "python.exe");
            // App execution aliases may open an installer instead of running Python.
            if (candidate.Contains("\\Microsoft\\WindowsApps\\", StringComparison.OrdinalIgnoreCase)) continue;
            if (File.Exists(candidate)) return candidate;
        }
        throw new EngineException("Python 3.11 or later is required. Set AGENTINBOX_PYTHON to the absolute path of an installed runtime.");
    }

    /// <summary>Resolve the selected profile consistently for packaged and source launchers.</summary>
    /// <param name="packaged">Explicit package-identity result; null resolves current process identity.</param>
    private static string ResolveDataDirectory(bool? packaged = null)
    {
        var configured = Environment.GetEnvironmentVariable("AGENTINBOX_DATA_DIR");
        if (configured is not null)
        {
            if (!Path.IsPathFullyQualified(configured))
                throw new EngineException("AGENTINBOX_DATA_DIR must be an absolute directory path.");
            return Path.GetFullPath(configured);
        }
        if (packaged ?? HasPackageIdentity())
            return Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "AgentInbox");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentInbox");
    }

    /// <summary>Distinguish packaged execution from the explicit unpackaged Windows result.</summary>
    private static bool HasPackageIdentity()
    {
        uint nameLength = 0;
        var identityStatus = GetCurrentPackageFullName(ref nameLength, IntPtr.Zero);
        if (identityStatus is 0 or 122) return true;
        // Only APPMODEL_ERROR_NO_PACKAGE selects the unpackaged profile; all other errors fail closed.
        if (identityStatus != 15700)
            throw new EngineException("AgentInbox could not resolve its package identity and local data directory.");
        return false;
    }

    /// <summary>Read Windows package identity using a caller-owned native buffer.</summary>
    /// <param name="packageFullNameLength">In/out character capacity required by the Windows package-name API.</param>
    /// <param name="packageFullName">Native output buffer, or a null pointer when querying required capacity.</param>
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);

    /// <summary>Reads one bounded protocol line without retaining child diagnostics or unlimited output.</summary>
    /// <param name="process">Owned child process whose protocol streams or lifetime are being handled.</param>
    /// <param name="cancellationToken">Cancellation for the requested operation and any owned child process.</param>
    private static async Task<string> ReadRecordAsync(Process process, CancellationToken cancellationToken)
    {
        // Read a bounded line instead of allocating an unlimited response from a child process.
        var output = new StringBuilder();
        var character = new char[1];
        while (true)
        {
            var read = await process.StandardOutput.ReadAsync(character.AsMemory(), cancellationToken);
            if (read == 0) throw new EngineException("The Python engine closed before returning a complete response.");
            if (character[0] == '\n') break;
            if (output.Length >= MaximumResponseCharacters)
                throw new EngineException("The Python engine returned a response larger than the permitted limit.");
            output.Append(character[0]);
        }
        return output.ToString();
    }

    /// <summary>Accepts a bounded sequence of opt-in progress records before the final operation result.</summary>
    /// <param name="process">Owned child process whose protocol streams or lifetime are being handled.</param>
    /// <param name="cancellationToken">Cancellation for the requested operation and any owned child process.</param>
    /// <param name="progress">Optional account-check progress callback or one completed engine stage update.</param>
    /// <param name="diagnostic">Optional callback for bounded, redacted local sign-in details.</param>
    private static async Task<JsonElement> ReadResponseAsync(Process process, CancellationToken cancellationToken,
        Action<AccountCheckProgress>? progress = null, Action<string>? diagnostic = null)
    {
        try
        {
            for (var count = 0; count <= 16; count++)
            {
                using var document = JsonDocument.Parse(await ReadRecordAsync(process, cancellationToken));
                var root = document.RootElement;
                if (root.TryGetProperty("diagnostic", out var detailRecord))
                {
                    if (diagnostic is null || root.EnumerateObject().Count() != 1
                        || detailRecord.ValueKind != JsonValueKind.String
                        || detailRecord.GetString() is not { Length: > 0 and <= 1200 } detail
                        || detail.Any(char.IsControl))
                        throw new EngineException("The Python engine returned invalid sign-in details.");
                    diagnostic(detail);
                    continue;
                }
                if (root.TryGetProperty("progress", out var update))
                {
                    var stage = update.GetProperty("stage").GetString();
                    var state = update.GetProperty("state").GetString();
                    if (progress is null || root.EnumerateObject().Count() != 1
                        || stage is not ("settings" or "connection" or "authentication" or "folders" or "smtp" or "reading")
                        || (state is not ("started" or "passed") && !(stage == "reading" && state == "empty")))
                        throw new EngineException("The Python engine returned an unexpected progress update.");
                    progress(new AccountCheckProgress(stage, state));
                    continue;
                }
                if (!root.GetProperty("ok").GetBoolean())
                {
                    var error = root.GetProperty("error");
                    var message = error.GetProperty("message").GetString();
                    message = message is { Length: > 0 and <= 2048 } ? message : "The Python engine rejected this operation.";
                    if (error.TryGetProperty("server_response", out var response) && response.GetString() is { Length: > 0 and <= 2200 } detail)
                        message += "\n\nServer reply (sign-in details hidden):\n" + detail;
                    throw new EngineException(message);
                }
                return root.GetProperty("result").Clone();
            }
            throw new EngineException("The Python engine returned too many progress records.");
        }
        catch (JsonException) { throw new EngineException("The Python engine returned an invalid response."); }
        catch (InvalidOperationException) { throw new EngineException("The Python engine returned an unexpected response."); }
        catch (KeyNotFoundException) { throw new EngineException("The Python engine returned an incomplete response."); }
    }

    /// <summary>Stop the owned child process when it has not already exited.</summary>
    /// <param name="process">Owned child process whose protocol streams or lifetime are being handled.</param>
    private static void Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}

/// <summary>Describes a completed or active account-check stage without credentials or server content.</summary>
/// <param name="Stage">Engine progress-stage key.</param>
/// <param name="State">Engine progress state, such as started or passed.</param>
internal readonly record struct AccountCheckProgress(string Stage, string State);

/// <summary>A safe, actionable error returned by the local mail engine.</summary>
/// <param name="message">Safe user-facing outcome or guidance; never raw credentials or mail content.</param>
internal sealed class EngineException(string message) : Exception(message);
