// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace MailMeUp.Desktop.Services;

internal sealed partial class AssistantSetupService
{
    /// <summary>Checks saved settings and the actual local MCP launcher without calling any mail tool.</summary>
    internal async Task<int> TestConnectionAsync(AssistantClientStatus detected, CancellationToken cancellationToken)
    {
        if (!detected.IsConfigured)
            throw new InvalidOperationException("Add or update the AgentInbox connection in Codex before testing it.");
        var snapshot = ReadSnapshot(detected.ConfigurationPath);
        var clients = await DetectAsync(cancellationToken);
        var current = clients.Single(client => client.ClientId == "codex");
        if (!current.IsConfigured) throw new InvalidOperationException(current.Message);
        if (current.SnapshotToken != detected.SnapshotToken)
            throw new InvalidOperationException("Codex settings changed. Press Check Codex again, then test the connection.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var start = new ProcessStartInfo(_connection.Command)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        foreach (var argument in _connection.Arguments) start.ArgumentList.Add(argument);
        foreach (var variable in _connection.Environment) start.Environment[variable.Key] = variable.Value;
        using var process = new Process { StartInfo = start };
        Task<string?>? errors = null;
        try
        {
            if (!process.Start()) throw new InvalidOperationException("Windows could not start AgentInbox. Repair or reinstall the app, then try again.");
            using var cancellation = timeout.Token.Register(() => Stop(process));
            errors = ReadBoundedAsync(process.StandardError, timeout.Token, retain: false);
            process.StandardInput.NewLine = "\n";
            var initialize = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0", id = 1, method = "initialize",
                @params = new
                {
                    protocolVersion = "2025-06-18", capabilities = new Dictionary<string, object>(),
                    clientInfo = new { name = "agentinbox-setup", version = "1" }
                }
            });
            await process.StandardInput.WriteLineAsync(initialize.AsMemory(), timeout.Token);
            await process.StandardInput.FlushAsync(timeout.Token);
            var initialized = await ReadConnectionReplyAsync(process, 1, timeout.Token);
            if (!initialized.TryGetProperty("serverInfo", out var server) || server.ValueKind != JsonValueKind.Object
                || !server.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String
                || name.GetString() != ServerName
                || !initialized.TryGetProperty("protocolVersion", out var protocol) || protocol.ValueKind != JsonValueKind.String
                || protocol.GetString() != "2025-06-18")
                throw new InvalidOperationException("The saved command did not start the expected AgentInbox connection. Update the connection and try again.");

            await process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}".AsMemory(), timeout.Token);
            await process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}".AsMemory(), timeout.Token);
            await process.StandardInput.FlushAsync(timeout.Token);
            var listed = await ReadConnectionReplyAsync(process, 2, timeout.Token);
            if (!listed.TryGetProperty("tools", out var tools) || tools.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("AgentInbox started but did not return its mail tools. Repair or reinstall the app, then try again.");
            var expected = new HashSet<string>(StringComparer.Ordinal)
                { "list_accounts", "search_mail", "read_mail", "send_email", "get_send_status" };
            foreach (var tool in tools.EnumerateArray())
            {
                if (tool.ValueKind != JsonValueKind.Object || !tool.TryGetProperty("name", out var toolName)
                    || toolName.ValueKind != JsonValueKind.String || !expected.Remove(toolName.GetString()!))
                    throw new InvalidOperationException("AgentInbox returned an unexpected set of mail tools. Update the connection and try again.");
            }
            if (expected.Count != 0)
                throw new InvalidOperationException("Some AgentInbox mail tools are missing. Repair or reinstall the app, then try again.");
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0)
                throw new InvalidOperationException("AgentInbox stopped with an error after replying. Repair or reinstall the app, then try again.");
            if (ReadSnapshot(current.ConfigurationPath).Fingerprint != snapshot.Fingerprint)
                throw new InvalidOperationException("Codex settings changed during the check. Press Check Codex again and retry.");
            return tools.GetArrayLength();
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("AgentInbox did not finish the connection check within 30 seconds. Try again or repair the app.");
        }
        catch (Win32Exception)
        {
            throw new InvalidOperationException("Windows could not start AgentInbox. Check its app execution alias in Windows Settings or reinstall the app.");
        }
        catch (IOException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (timeout.IsCancellationRequested)
                throw new InvalidOperationException("AgentInbox did not finish the connection check within 30 seconds. Try again or repair the app.");
            throw new InvalidOperationException("AgentInbox stopped before the connection check finished. Repair or reinstall the app, then try again.");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("AgentInbox replied, but the connection information could not be read. Update the connection and try again.");
        }
        finally
        {
            Stop(process);
            if (errors is not null)
            {
                try { await errors; }
                catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
            }
        }
    }

    /// <summary>Reads one bounded JSON-RPC result without exposing child output or diagnostics.</summary>
    private static async Task<JsonElement> ReadConnectionReplyAsync(Process process, int expectedId, CancellationToken cancellationToken)
    {
        const int maximumCharacters = 64 * 1024;
        var line = new StringBuilder();
        var character = new char[1];
        while (true)
        {
            if (await process.StandardOutput.ReadAsync(character.AsMemory(), cancellationToken) == 0)
                throw new InvalidOperationException("AgentInbox stopped before replying to the connection check. Repair or reinstall the app.");
            if (character[0] == '\n') break;
            if (line.Length >= maximumCharacters)
                throw new InvalidOperationException("AgentInbox sent too much connection information. Repair or reinstall the app.");
            line.Append(character[0]);
        }
        using var document = JsonDocument.Parse(line.ToString());
        var reply = document.RootElement;
        if (reply.ValueKind != JsonValueKind.Object || !reply.TryGetProperty("jsonrpc", out var version)
            || version.ValueKind != JsonValueKind.String || version.GetString() != "2.0"
            || !reply.TryGetProperty("id", out var identity) || identity.ValueKind != JsonValueKind.Number
            || !identity.TryGetInt32(out var id) || id != expectedId || reply.TryGetProperty("error", out _)
            || !reply.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("AgentInbox did not accept the connection check. Update the connection and try again.");
        return result.Clone();
    }
}
