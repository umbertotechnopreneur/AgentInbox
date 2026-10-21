// SPDX-License-Identifier: MIT

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
    internal async Task<JsonElement> ManageAsync(string operation, object arguments, CancellationToken cancellationToken,
        Action<AccountCheckProgress>? progress = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        using var process = Start("manage");
        using var cancellation = timeout.Token.Register(() => Stop(process));
        try
        {
            var request = new Dictionary<string, object> { ["operation"] = operation, ["arguments"] = arguments };
            if (progress is not null) request["progress"] = true;
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), timeout.Token);
            process.StandardInput.Close();
            var result = await ReadResponseAsync(process, timeout.Token, progress);
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0) throw new EngineException("The Python engine exited before completing the request.");
            return result;
        }
        finally { Stop(process); }
    }

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

    private static async Task DiscardDiagnosticsAsync(StreamReader reader)
    {
        // Discard bounded chunks; do not retain even one potentially unlimited stderr line.
        var buffer = new char[4096];
        try { while (await reader.ReadAsync(buffer.AsMemory()) > 0) { } }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    private static string ResolveEntryPoint()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Python", "run.py");
        if (!File.Exists(path)) throw new EngineException("The Python engine is missing from this installation.");
        return path;
    }

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

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);

    /// <summary>Reads one bounded protocol line without retaining child diagnostics or unlimited output.</summary>
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
    private static async Task<JsonElement> ReadResponseAsync(Process process, CancellationToken cancellationToken,
        Action<AccountCheckProgress>? progress = null)
    {
        try
        {
            for (var count = 0; count <= 16; count++)
            {
                using var document = JsonDocument.Parse(await ReadRecordAsync(process, cancellationToken));
                var root = document.RootElement;
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
                        message += "\n\nServer / connection detail (credentials redacted):\n" + detail;
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

    private static void Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}

/// <summary>Describes a completed or active account-check stage without credentials or server content.</summary>
internal readonly record struct AccountCheckProgress(string Stage, string State);

/// <summary>A safe, actionable error returned by the local mail engine.</summary>
internal sealed class EngineException(string message) : Exception(message);
