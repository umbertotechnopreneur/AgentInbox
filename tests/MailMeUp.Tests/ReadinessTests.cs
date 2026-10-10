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
using MailMeUp.Providers.Google;
using MailMeUp.Providers.Microsoft;
using MailMeUp.Storage;
using Xunit;

namespace MailMeUp.Tests;

public sealed class ReadinessTests
{
    [Fact]
    public void FoundationNeverAdvertisesUnimplementedMailCapabilities()
    {
        var application = new MailMeUpApplication(new SqliteAccountStore(Path.GetTempPath()),
            new IProviderModule[] { new GoogleProviderModule(), new MicrosoftProviderModule() });

        var status = application.GetStatus();
        Assert.Equal("foundation", status.Stage);
        Assert.True(status.ReadOnly);
        Assert.False(status.CanConnectAccounts);
        Assert.Equal(2, status.Providers.Count);
        Assert.All(status.Providers, provider =>
        {
            Assert.False(provider.AuthenticationAvailable);
            Assert.False(provider.MailReadAvailable);
            Assert.False(provider.CalendarReadAvailable);
        });
    }
}
