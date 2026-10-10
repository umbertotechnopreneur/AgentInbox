// SPDX-License-Identifier: MIT

/* VBWR B
 *
 * Project: AgentInbox
 * Repository: https://github.com/umbertotechnopreneur/AgentInbox
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * Modified with AI: OpenAI Codex; added this header on 2026-10-10.
 * Human guidance: Umberto Giacobbi; requested VibeWare branding.
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 * SPDX-License-Identifier: MIT
 *
 * VBWR E */

using MailMeUp.Application;
using MailMeUp.Core;
using Xunit;

namespace MailMeUp.Tests;

public sealed class AccountRecoveryTests
{
    private static readonly Account Existing = new(
        "google:existing", "google", "Existing account", "existing@example.test", true, true);
    private static readonly Account Other = new(
        "google:other", "google", "Other account", "other@example.test", true, true);

    [Fact]
    public async Task ReconnectMetadataFailureKeepsExistingCredentialAndOtherAccounts()
    {
        var store = new MemoryAccountStore([Existing, Other]) { FailSave = true };
        var connector = new RecordingConnector(Existing with { DisplayName = "Reconnected account" }, [Existing.Id, Other.Id]);
        var application = CreateApplication(store, connector);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            application.ConnectAccountAsync("google", new AccountConnectionOptions()));

        Assert.Equal("Synthetic metadata save failure.", failure.Message);
        Assert.Empty(connector.DisconnectedIds);
        Assert.Contains(Existing.Id, connector.Credentials);
        Assert.Contains(Other.Id, connector.Credentials);
        Assert.Equal([Existing, Other], await store.ListAsync());
    }

    [Fact]
    public async Task NewAccountMetadataFailureRemovesOnlyItsOrphanCredential()
    {
        var store = new MemoryAccountStore([Other]) { FailSave = true };
        var connector = new RecordingConnector(Existing, [Other.Id]);
        var application = CreateApplication(store, connector);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            application.ConnectAccountAsync("google", new AccountConnectionOptions()));

        Assert.Equal([Existing.Id], connector.DisconnectedIds);
        Assert.DoesNotContain(Existing.Id, connector.Credentials);
        Assert.Contains(Other.Id, connector.Credentials);
        Assert.Equal([Other], await store.ListAsync());
    }

    [Fact]
    public async Task CleanupFailureDoesNotHideTheOriginalMetadataFailure()
    {
        var store = new MemoryAccountStore([]) { FailSave = true };
        var connector = new RecordingConnector(Existing, []) { FailDisconnect = true };

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateApplication(store, connector).ConnectAccountAsync("google", new AccountConnectionOptions()));

        Assert.Equal("Synthetic metadata save failure.", failure.Message);
        Assert.Equal([Existing.Id], connector.DisconnectedIds);
        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public async Task CancelledMetadataSaveStillCleansUpTheNewCredential()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new MemoryAccountStore([]) { CancelSave = cancellation };
        var connector = new RecordingConnector(Existing, []);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateApplication(store, connector).ConnectAccountAsync(
                "google", new AccountConnectionOptions(), cancellation.Token));

        Assert.Equal([Existing.Id], connector.DisconnectedIds);
        Assert.False(connector.LastDisconnectTokenWasCancelled);
        Assert.Empty(connector.Credentials);
    }

    [Fact]
    public async Task LocalRemovalIsIdempotentAndPreservesTheOtherAccount()
    {
        var store = new MemoryAccountStore([Existing, Other]);
        var connector = new RecordingConnector(Existing, [Existing.Id, Other.Id]);
        var application = CreateApplication(store, connector);

        var removed = await application.RemoveAccountAsync(Existing.Id);
        var repeated = await application.RemoveAccountAsync(Existing.Id);

        Assert.True(removed.Removed);
        Assert.False(repeated.Removed);
        Assert.Equal([Existing.Id], connector.DisconnectedIds);
        Assert.Equal([Other.Id], connector.Credentials);
        Assert.Equal([Other], await store.ListAsync());
    }

    [Fact]
    public async Task CredentialRemovalFailureKeepsMetadataSoTheUserCanRetry()
    {
        var store = new MemoryAccountStore([Existing, Other]);
        var connector = new RecordingConnector(Existing, [Existing.Id, Other.Id]) { FailDisconnect = true };
        var application = CreateApplication(store, connector);

        await Assert.ThrowsAsync<ProviderAuthenticationException>(() => application.RemoveAccountAsync(Existing.Id));
        Assert.Equal([Existing, Other], await store.ListAsync());

        connector.FailDisconnect = false;
        Assert.True((await application.RemoveAccountAsync(Existing.Id)).Removed);
        Assert.Equal([Other], await store.ListAsync());
    }

    private static MailMeUpApplication CreateApplication(IAccountStore store, IAccountConnector connector) =>
        new(store, [], [], [connector], [], []);

    private sealed class MemoryAccountStore(IEnumerable<Account> accounts) : IAccountStore
    {
        private readonly Dictionary<string, Account> _accounts = accounts.ToDictionary(account => account.Id, StringComparer.Ordinal);

        public bool FailSave { get; init; }
        public CancellationTokenSource? CancelSave { get; init; }

        public Task<IReadOnlyList<Account>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Account>>(_accounts.Values.OrderBy(account => account.Id, StringComparer.Ordinal).ToArray());

        public Task SaveAsync(Account account, CancellationToken cancellationToken = default)
        {
            CancelSave?.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            if (FailSave)
            {
                throw new InvalidOperationException("Synthetic metadata save failure.");
            }

            _accounts[account.Id] = account;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(string accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_accounts.Remove(accountId));
    }

    private sealed class RecordingConnector(Account result, IEnumerable<string> credentials) : IAccountConnector
    {
        public string ProviderId => "google";
        public HashSet<string> Credentials { get; } = new(credentials, StringComparer.Ordinal);
        public List<string> DisconnectedIds { get; } = [];
        public bool FailDisconnect { get; set; }
        public bool LastDisconnectTokenWasCancelled { get; private set; }

        public Task<AccountConnectionResult> ConnectAsync(AccountConnectionOptions options, CancellationToken cancellationToken = default)
        {
            Credentials.Add(result.Id);
            return Task.FromResult(new AccountConnectionResult(result));
        }

        public Task DisconnectAsync(Account account, CancellationToken cancellationToken = default)
        {
            DisconnectedIds.Add(account.Id);
            LastDisconnectTokenWasCancelled = cancellationToken.IsCancellationRequested;
            cancellationToken.ThrowIfCancellationRequested();
            if (FailDisconnect)
            {
                throw new ProviderAuthenticationException("Synthetic credential removal failure.");
            }

            Credentials.Remove(account.Id);
            return Task.CompletedTask;
        }
    }
}
