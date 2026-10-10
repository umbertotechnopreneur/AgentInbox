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
using MailMeUp.Security;
using MailMeUp.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace MailMeUp.Hosting;

/// <summary>Composes the same application services for local CLI, MCP and desktop adapters.</summary>
public static class ServiceRegistration
{
    /// <summary>Registers provider readers, protected credentials and local metadata in one data directory.</summary>
    public static void AddMailMeUp(this IServiceCollection services, string dataDirectory)
    {
        services.AddSingleton<IAccountStore>(_ => new SqliteAccountStore(dataDirectory));
        services.AddSingleton<IAccountSharingStore>(_ => new JsonAccountSharingStore(dataDirectory));
        services.AddSingleton<IMailSearchPreferencesStore>(_ => new JsonMailSearchPreferencesStore(dataDirectory));
        services.AddSingleton<IProviderConfigurationStore>(_ => new JsonProviderConfigurationStore(dataDirectory));
        services.AddSingleton<ISecretStore>(_ => new OsProtectedSecretStore(dataDirectory));
        services.AddSingleton<FileReadGuardrails>(_ => new FileReadGuardrails(dataDirectory));
        services.AddSingleton<IProviderRequestGovernor>(provider => provider.GetRequiredService<FileReadGuardrails>());
        services.AddSingleton<IReadBudget>(provider => provider.GetRequiredService<FileReadGuardrails>());
        services.AddSingleton<IReadGuardrailManagement>(provider => provider.GetRequiredService<FileReadGuardrails>());
        services.AddSingleton<IProviderModule, GoogleProviderModule>();
        services.AddSingleton<IProviderModule, MicrosoftProviderModule>();
        services.AddSingleton<IProviderSetupService, GoogleProviderSetupService>();
        services.AddSingleton<IProviderSetupService, MicrosoftProviderSetupService>();
        services.AddSingleton<IAccountConnector, GoogleAccountConnector>();
        services.AddSingleton<IAccountConnector, MicrosoftAccountConnector>();
        services.AddSingleton<IMailReader, GoogleMailReader>();
        services.AddSingleton<IMailReader, MicrosoftMailReader>();
        services.AddSingleton<ICalendarReader, GoogleCalendarReader>();
        services.AddSingleton<ICalendarReader, MicrosoftCalendarReader>();
        foreach (var providerId in new[] { "google", "microsoft" })
        {
            services.AddSingleton<IAccountConnectionChecker>(provider => new ReadAccessConnectionChecker(
                provider.GetServices<IMailReader>().Single(reader => reader.ProviderId == providerId),
                provider.GetServices<ICalendarReader>().Single(reader => reader.ProviderId == providerId),
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ReadAccessConnectionChecker>>()));
        }
        services.AddSingleton<MailMeUpApplication>();
        services.AddSingleton<IMailMeUpApplication>(provider => new LoggingMailMeUpApplication(
            provider.GetRequiredService<MailMeUpApplication>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LoggingMailMeUpApplication>>()));
    }
}
