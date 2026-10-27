/* VBWR B
 *
 * Project: AgentInbox
 * Repository: https://github.com/umbertotechnopreneur/AgentInbox
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * File: Observable device-code flow, redacted trace and sign-in outcomes.
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
using System.Text.Json;
using MailMeUp.Desktop.Services;

namespace MailMeUp.Desktop.ViewModels;

/// <summary>Owns one Microsoft sign-in lifecycle and its bounded, redacted diagnostic trace.</summary>
/// <param name="engine">Shared adapter for the local Python management contract.</param>
internal sealed class MicrosoftSignInModel(PythonEngine engine) : ObservableModel
{
    private readonly Stopwatch _clock = new();
    private string _flowId = "";
    private int _interval;
    private DateTimeOffset _deadline;
    private string _status = "";
    private bool _detailsExpanded;

    public string UserCode { get; private set; } = "";
    public Uri? VerificationUri { get; private set; }
    public string VerificationAddress => VerificationUri?.AbsoluteUri ?? "";
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Log { get; private set; } = "";
    public bool DetailsExpanded { get => _detailsExpanded; set => Set(ref _detailsExpanded, value); }
    public bool Connected { get; private set; }
    public bool IsFinished { get; private set; }
    public bool CleanupPending { get; private set; }
    public JsonElement? CleanupResult { get; private set; }
    public string? Failure { get; private set; }
    public string ExpiryHint => $"This code expires at {_deadline.ToLocalTime():t}. You can check reading and sending after signing in.";

    /// <summary>Reset transient sign-in presentation without changing saved account authorization.</summary>
    internal void Clear()
    {
        _clock.Reset();
        _flowId = "";
        UserCode = "";
        VerificationUri = null;
        Log = "";
        Status = "";
        Connected = false;
        IsFinished = false;
        CleanupPending = false;
        CleanupResult = null;
        Failure = null;
        Notify();
    }

    /// <summary>Start timing the explicit sign-in operation and record its first stage.</summary>
    internal void StartLog() { _clock.Restart(); Append("Checking the account settings."); }
    /// <summary>Stop timing the current sign-in operation.</summary>
    internal void StopLog() => _clock.Stop();

    /// <summary>Append bounded local sign-in guidance to the operation log.</summary>
    /// <param name="message">Safe user-facing outcome or guidance; never raw credentials or mail content.</param>
    internal void Append(string message)
    {
        const int maximum = 12000;
        var text = Log + $"[{_clock.Elapsed:mm\\:ss}] {message}\n";
        if (text.Length > maximum)
        {
            var start = text.IndexOf('\n', text.Length - maximum);
            text = start >= 0 ? text[(start + 1)..] : text[^maximum..];
        }
        Log = text;
        Notify(nameof(Log));
    }

    /// <summary>Record the result of an explicit device-code clipboard action.</summary>
    internal void CodeCopied() => Status = "Code copied. Paste it on Microsoft's page.";
    /// <summary>Show a safe explanation when a requested clipboard action fails.</summary>
    /// <param name="message">Safe user-facing outcome or guidance; never raw credentials or mail content.</param>
    internal void ClipboardFailed(string message) => Status = message;
    /// <summary>Record whether Windows opened the requested Microsoft sign-in address.</summary>
    /// <param name="opened">Whether Windows accepted the requested browser launch.</param>
    internal void BrowserOpened(bool opened)
    {
        Append(opened ? "Microsoft's page opened in the browser." : "Windows could not open the browser.");
        Status = opened ? "Enter the code on Microsoft's page and sign in. I'll wait here."
            : "Windows could not open the browser. Open the displayed Microsoft address manually.";
    }

    /// <summary>Runs the shared engine flow; the caller supplies only the native interaction surface.</summary>
    /// <param name="accountId">Saved account ID selected for the explicit Microsoft sign-in.</param>
    /// <param name="showDialog">View adapter that presents sign-in state without owning account policy.</param>
    /// <param name="cancellationToken">Cancellation for the requested operation and any owned child process.</param>
    internal async Task RunAsync(string accountId, Func<MicrosoftSignInModel, CancellationToken, Task> showDialog,
        CancellationToken cancellationToken)
    {
        using var flowCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            var start = await engine.ManageAsync("start_microsoft_signin", new { account_id = accountId },
                cancellationToken, diagnostic: Append);
            _flowId = start.GetProperty("flow_id").GetString() ?? "";
            var address = start.GetProperty("verification_uri").GetString();
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
                || uri.Host.Length == 0 || uri.UserInfo.Length != 0 || uri.Port == 0)
                throw new EngineException("Microsoft did not return a valid secure sign-in address. Try again.");
            VerificationUri = uri;
            var expires = start.GetProperty("expires_in").GetInt32();
            _interval = start.GetProperty("interval").GetInt32();
            if (_flowId.Length == 0 || expires is < 1 or > 3600 || _interval is < 1 or > 300)
                throw new EngineException("Microsoft could not start sign-in. Please try again.");
            UserCode = start.GetProperty("user_code").GetString() ?? "";
            _deadline = DateTimeOffset.UtcNow.AddSeconds(expires);
            Status = "Waiting for you to sign in on Microsoft's page.";
            Append("Ready. Open Microsoft and enter the displayed code. Codes are excluded from these details.");
            Notify();
            var polling = PollAsync(flowCancellation.Token);
            try { await showDialog(this, flowCancellation.Token); }
            finally
            {
                flowCancellation.Cancel();
                await polling;
            }
            if (Failure is not null) throw new EngineException(Failure);
            if (!Connected) Append("Sign-in cancelled.");
        }
        catch (Exception) when (Connected)
        {
            Append("Sign-in is saved, but the sign-in window could not finish updating.");
        }
        finally
        {
            if (_flowId.Length > 0 && !Connected && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    Append("Removing the temporary sign-in data.");
                    var cleanup = await engine.ManageAsync("cancel_microsoft_signin", new { flow_id = _flowId }, cancellationToken);
                    CleanupResult = cleanup;
                    CleanupPending = cleanup.GetProperty("cleanup_pending").GetBoolean();
                    Append(CleanupPending ? "Temporary-data cleanup is still pending. Use Retry cleanup." : "Temporary sign-in data removed.");
                }
                catch (Exception exception)
                {
                    CleanupPending = true;
                    Append("Temporary-data cleanup could not finish. Use Retry cleanup."
                        + (exception is EngineException ? " " + exception.Message : ""));
                }
            }
            _clock.Stop();
            UserCode = "";
            VerificationUri = null;
            Notify();
        }
    }

    /// <summary>Poll the protected Microsoft flow at its returned interval until completion.</summary>
    /// <param name="cancellationToken">Cancellation for the requested operation and any owned child process.</param>
    private async Task PollAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var remaining = _deadline - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero) throw new EngineException("This code has expired. Press Sign in with Microsoft to get a new one.");
                await Task.Delay(remaining < TimeSpan.FromSeconds(_interval) ? remaining : TimeSpan.FromSeconds(_interval), cancellationToken);
                if (DateTimeOffset.UtcNow >= _deadline) throw new EngineException("This code has expired. Press Sign in with Microsoft to get a new one.");
                var result = await engine.ManageAsync("poll_microsoft_signin", new { flow_id = _flowId }, cancellationToken, diagnostic: Append);
                switch (result.GetProperty("status").GetString())
                {
                    case "connected":
                        CleanupResult = result;
                        Connected = true;
                        CleanupPending = result.GetProperty("cleanup_pending").GetBoolean();
                        Append("Sign-in saved securely." + (CleanupPending ? " Temporary-data cleanup is still pending." : " Temporary code consumed."));
                        IsFinished = true;
                        Notify();
                        return;
                    case "pending":
                        _interval = result.GetProperty("interval").GetInt32();
                        if (_interval is < 1 or > 300) throw new EngineException("Microsoft returned an invalid polling interval.");
                        Append($"Waiting for Microsoft. Checking again in {_interval} seconds.");
                        break;
                    default: throw new EngineException("Microsoft sent a reply I couldn't read. Please try signing in again.");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Failure = exception is EngineException ? exception.Message : "Microsoft sign-in could not finish. Please try again.";
            IsFinished = true;
            Notify();
        }
    }
}
