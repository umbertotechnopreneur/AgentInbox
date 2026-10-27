/* VBWR B
 *
 * Project: AgentInbox
 * Repository: https://github.com/umbertotechnopreneur/AgentInbox
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * File: Observable assistant detection, setup and local verification state.
 * Modified with AI: OpenAI Codex; code readability and documentation, 2026-10-27.
 * Human guidance: Umberto Giacobbi; document parameters and simplify maintenance.
 * Evidence: docs/VALIDATION.md (source review; execution checks pending).
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 * SPDX-License-Identifier: MIT
 *
 * VBWR E */

using System.Text.Json;
using MailMeUp.Desktop.Services;

namespace MailMeUp.Desktop.ViewModels;

/// <summary>Owns Codex configuration snapshots and verification results independently of their controls.</summary>
/// <param name="engine">Shared adapter for the local Python management contract.</param>
/// <param name="notice">Observable owner notification used for explicit connection outcomes.</param>
internal sealed class AssistantConnectionModel(PythonEngine engine, NotificationModel notice) : ObservableModel
{
    private AssistantSetupService? _service;
    private AssistantClientStatus? _client;
    private bool _detailsExpanded;
    public bool DetailsExpanded { get => _detailsExpanded; set => Set(ref _detailsExpanded, value); }
    public string Configuration { get; private set; } = "";
    public string Command { get; private set; } = "";
    public string Arguments { get; private set; } = "";
    public string DataDirectory { get; private set; } = "";
    public string DisplayName => _client?.DisplayName ?? "Codex";
    public bool RequiresConfirmation => _client?.RequiresReplacement ?? false;
    public bool CanConnect => _client?.CanConnect ?? false;
    public bool IsConfigured => _client?.IsConfigured ?? false;
    public bool ShowStatus => !RequiresConfirmation;
    public bool ShowConnect => !IsConfigured;
    public string Message => _client?.Message ?? "Checking Codex on this computer…";
    public string ConfigurationPath => _client?.ConfigurationPath is { Length: > 0 } path ? path : "Not available";
    public string ConnectText => RequiresConfirmation ? "Update connection" : "Connect to Codex";
    public string ConnectName => RequiresConfirmation ? "Update AgentInbox connection in Codex" : "Connect AgentInbox to Codex";
    public string StatusText => IsConfigured ? "AgentInbox is enabled in Codex settings" : _client?.IsDetected != true ? "Codex not found"
        : CanConnect ? "Ready to add AgentInbox" : "A setup issue needs your attention";
    public int StatusTone => IsConfigured ? 1 : CanConnect ? 0 : 2;
    public string CheckStatus { get; private set; } = "";
    public int CheckTone { get; private set; }
    public string AppLabel => IsConfigured ? "App saved in Codex" : "App this setup will add to Codex";
    public string DetailsHint => IsConfigured
        ? "These launch settings match the enabled AgentInbox entry saved in Codex. The folder below contains the email accounts you configure in this app. Passwords are kept separately by Windows."
        : "These are the launch settings this app will add. They are not yet saved as a matching, enabled connection in Codex.";
    public string TryPrompt => "Which email accounts can you access through AgentInbox?";

    /// <summary>Refresh local assistant detection without starting a mail operation.</summary>
    /// <param name="cancellationToken">Cancellation for the requested operation and any owned child process.</param>
    internal async Task DetectAsync(CancellationToken cancellationToken)
    {
        Configuration = engine.CreateClientConfiguration();
        _service = new AssistantSetupService(Configuration);
        using var description = JsonDocument.Parse(Configuration);
        var server = description.RootElement.GetProperty("mcpServers").GetProperty("agentinbox");
        Command = server.GetProperty("command").GetString()!;
        Arguments = server.GetProperty("args").ToString();
        DataDirectory = server.GetProperty("env").GetProperty("AGENTINBOX_DATA_DIR").GetString()!;
        var clients = await _service.DetectAsync(cancellationToken);
        _client = clients.Single(client => client.ClientId == "codex");
        ResetCheck();
        Notify();
    }

    /// <summary>Apply the explicitly requested assistant configuration and report its committed outcome.</summary>
    /// <param name="replaceExisting">Whether the user explicitly confirmed replacing the existing AgentInbox entry.</param>
    /// <param name="cancellationToken">Cancellation for the requested operation and any owned child process.</param>
    internal async Task ConnectAsync(bool replaceExisting, CancellationToken cancellationToken)
    {
        if (_service is null || _client is null) throw new EngineException("Check Codex before connecting it.");
        AssistantSetupResult result;
        try { result = await _service.ConnectAsync(_client, cancellationToken, replaceExisting); }
        catch (InvalidOperationException exception) { throw new EngineException(exception.Message); }
        _client = result.Status;
        ResetCheck();
        Notify();
        notice.Show(result.ConfigurationWritten ? "Settings saved in Codex" : "Settings already saved in Codex",
            result.Message, result.CleanupPending ? 2 : 0);
    }

    /// <summary>Clear transient connection-check state without changing saved settings.</summary>
    private void ResetCheck()
    {
        CheckStatus = IsConfigured ? "Not checked on this PC yet."
            : "Add or update the connection in Codex before testing it.";
        CheckTone = 0;
    }

    /// <summary>Check the configured local MCP launcher without reading or sending mail.</summary>
    /// <param name="cancellationToken">Cancellation for the requested operation and any owned child process.</param>
    internal async Task TestAsync(CancellationToken cancellationToken)
    {
        if (_service is null || _client is null) throw new EngineException("Check Codex before testing its connection.");
        CheckStatus = "Checking the saved settings and starting AgentInbox…";
        CheckTone = 0;
        Notify();
        try
        {
            var count = await _service.TestConnectionAsync(_client, cancellationToken);
            CheckStatus = $"Local check passed. AgentInbox replied with {count} mail tools.";
            CheckTone = 1;
            notice.Show("Local connection passed", "Now try AgentInbox in a new Codex chat. This check did not open a mailbox.", 1);
        }
        catch (InvalidOperationException exception)
        {
            CheckStatus = "Local connection failed. " + exception.Message;
            CheckTone = 3;
            throw new EngineException(exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            const string message = "Could not read Codex's settings file. Open Connection details to check its location, then press Check Codex again.";
            CheckStatus = "Local connection failed. " + message;
            CheckTone = 3;
            throw new EngineException(message);
        }
        finally { Notify(); }
    }
}
