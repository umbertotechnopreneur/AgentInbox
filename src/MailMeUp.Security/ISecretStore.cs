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

namespace MailMeUp.Security;

/// <summary>OS-protected credential persistence boundary.</summary>
public interface ISecretStore
{
    /// <summary>Exclusively leases a credential reference across processes until disposal; fails if unsupported.</summary>
    /// <remarks>Hold this non-reentrant lease across a complete read, refresh and write sequence. Individual reads and writes use separate locks.</remarks>
    ValueTask<IDisposable> AcquireSessionAsync(string reference, CancellationToken cancellationToken = default) =>
        throw new SecretStoreException("This credential store does not support protected cross-process sessions.");

    /// <summary>Reads a protected credential blob by its opaque reference; returns null when absent.</summary>
    ValueTask<byte[]?> ReadAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Stores a credential blob using OS-backed protection, or fails if protection is unavailable.</summary>
    ValueTask WriteAsync(string reference, ReadOnlyMemory<byte> secret, CancellationToken cancellationToken = default);

    /// <summary>Deletes a stored credential blob without exposing its contents.</summary>
    ValueTask DeleteAsync(string reference, CancellationToken cancellationToken = default);
}

/// <summary>Indicates that the operating system could not safely protect credential material.</summary>
public sealed class SecretStoreException : Exception
{
    /// <summary>Creates a sanitized credential-store failure.</summary>
    public SecretStoreException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a sanitized credential-store failure.</summary>
    public SecretStoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
