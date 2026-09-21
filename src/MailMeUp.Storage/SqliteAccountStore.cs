using MailMeUp.Core;
using SQLitePCL;

namespace MailMeUp.Storage;

/// <summary>Stores account metadata only. No tokens, message bodies, or OAuth cache blobs are accepted.</summary>
public sealed class SqliteAccountStore(string directory) : IAccountStore
{
    private static readonly Lock InitializationLock = new();
    private static bool _initialized;
    private readonly string _databasePath = Path.Combine(DataDirectory.Resolve(directory), "accounts.db");

    /// <inheritdoc />
    public Task<IReadOnlyList<Account>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // A fresh installation has no registry. Reading it must not create files or directories.
        if (!File.Exists(_databasePath))
        {
            return Task.FromResult<IReadOnlyList<Account>>([]);
        }

        using var database = Open(SqliteOpenMode.ReadWrite);
        using var statement = database.Prepare(
            "SELECT id, provider, display_name, email_address, mail_read_enabled, calendar_read_enabled FROM accounts ORDER BY id COLLATE BINARY");
        var accounts = new List<Account>();
        while (statement.Step() == raw.SQLITE_ROW)
        {
            cancellationToken.ThrowIfCancellationRequested();
            accounts.Add(new(
                statement.Text(0),
                statement.Text(1),
                statement.Text(2),
                statement.Text(3),
                statement.Integer(4) != 0,
                statement.Integer(5) != 0));
        }

        statement.ExpectDone();
        return Task.FromResult<IReadOnlyList<Account>>(accounts);
    }

    /// <inheritdoc />
    public Task SaveAsync(Account account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(account.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(account.Provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(account.DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(account.EmailAddress);
        cancellationToken.ThrowIfCancellationRequested();

        var parent = Path.GetDirectoryName(_databasePath)!;
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(parent);
        }
        else
        {
            Directory.CreateDirectory(parent, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        using var database = Open(SqliteOpenMode.ReadWriteCreate);
        using var statement = database.Prepare("""
            INSERT INTO accounts (id, provider, display_name, email_address, mail_read_enabled, calendar_read_enabled)
            VALUES (?1, ?2, ?3, ?4, ?5, ?6)
            ON CONFLICT(id) DO UPDATE SET
                provider = excluded.provider,
                display_name = excluded.display_name,
                email_address = excluded.email_address,
                mail_read_enabled = excluded.mail_read_enabled,
                calendar_read_enabled = excluded.calendar_read_enabled
            """);
        statement.BindText(1, account.Id);
        statement.BindText(2, account.Provider);
        statement.BindText(3, account.DisplayName);
        statement.BindText(4, account.EmailAddress);
        statement.BindInteger(5, account.MailReadEnabled ? 1 : 0);
        statement.BindInteger(6, account.CalendarReadEnabled ? 1 : 0);
        statement.ExpectDone(statement.Step());
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string accountId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_databasePath))
        {
            return Task.FromResult(false);
        }

        using var database = Open(SqliteOpenMode.ReadWrite);
        using var statement = database.Prepare("DELETE FROM accounts WHERE id = ?1");
        statement.BindText(1, accountId);
        statement.ExpectDone(statement.Step());
        return Task.FromResult(database.Changes == 1);
    }

    private Database Open(SqliteOpenMode mode)
    {
        EnsureInitialized();
        var flags = raw.SQLITE_OPEN_FULLMUTEX | mode switch
        {
            SqliteOpenMode.ReadOnly => raw.SQLITE_OPEN_READONLY,
            SqliteOpenMode.ReadWrite => raw.SQLITE_OPEN_READWRITE,
            SqliteOpenMode.ReadWriteCreate => raw.SQLITE_OPEN_READWRITE | raw.SQLITE_OPEN_CREATE,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        var database = new Database(_databasePath, flags);
        try
        {
            var version = database.ExecuteScalarInteger("PRAGMA user_version");
            if (version == 0 && mode == SqliteOpenMode.ReadWriteCreate)
            {
                database.Execute("""
                    BEGIN IMMEDIATE;
                    CREATE TABLE IF NOT EXISTS accounts (
                        id TEXT PRIMARY KEY NOT NULL,
                        provider TEXT NOT NULL,
                        display_name TEXT NOT NULL,
                        email_address TEXT NOT NULL,
                        mail_read_enabled INTEGER NOT NULL DEFAULT 0,
                        calendar_read_enabled INTEGER NOT NULL DEFAULT 0
                    );
                    PRAGMA user_version = 2;
                    COMMIT;
                    """);
            }
            else if (version == 1 && mode != SqliteOpenMode.ReadOnly)
            {
                database.Execute("""
                    BEGIN IMMEDIATE;
                    ALTER TABLE accounts ADD COLUMN mail_read_enabled INTEGER NOT NULL DEFAULT 0;
                    ALTER TABLE accounts ADD COLUMN calendar_read_enabled INTEGER NOT NULL DEFAULT 0;
                    PRAGMA user_version = 2;
                    COMMIT;
                    """);
            }
            else if (version != 2)
            {
                throw new InvalidOperationException("Unsupported account database schema. Upgrade AgentInbox or select a new data directory.");
            }

            return database;
        }
        catch
        {
            database.Dispose();
            throw;
        }
    }

    private static void EnsureInitialized()
    {
        lock (InitializationLock)
        {
            if (_initialized)
            {
                return;
            }

            // Microsoft.Data.Sqlite probes Windows.Storage.ApplicationData during static initialization.
            // The packaged WinUI process can fail inside that WinRT activation before application code can recover.
            Batteries_V2.Init();
            _initialized = true;
        }
    }

    private enum SqliteOpenMode
    {
        ReadOnly,
        ReadWrite,
        ReadWriteCreate
    }

    private sealed class Database : IDisposable
    {
        private sqlite3? _handle;

        internal Database(string path, int flags)
        {
            var result = raw.sqlite3_open_v2(path, out var handle, flags, null);
            _handle = handle;
            if (result != raw.SQLITE_OK)
            {
                var exception = Error("open", result);
                Dispose();
                throw exception;
            }

            Check(raw.sqlite3_busy_timeout(Handle, 15_000), "set busy timeout");
        }

        internal int Changes => raw.sqlite3_changes(Handle);

        private sqlite3 Handle => _handle ?? throw new ObjectDisposedException(nameof(Database));

        internal Statement Prepare(string sql)
        {
            var result = raw.sqlite3_prepare_v2(Handle, sql, out var statement);
            if (result != raw.SQLITE_OK)
            {
                throw Error("prepare statement", result);
            }

            return new Statement(this, statement);
        }

        internal int ExecuteScalarInteger(string sql)
        {
            using var statement = Prepare(sql);
            var result = statement.Step();
            if (result != raw.SQLITE_ROW)
            {
                throw Error("read value", result);
            }

            var value = statement.Integer(0);
            statement.ExpectDone(statement.Step());
            return value;
        }

        internal void Execute(string sql)
        {
            var result = raw.sqlite3_exec(Handle, sql);
            if (result != raw.SQLITE_OK)
            {
                throw Error("execute statement", result);
            }
        }

        internal void Check(int result, string operation)
        {
            if (result != raw.SQLITE_OK)
            {
                throw Error(operation, result);
            }
        }

        internal InvalidOperationException Error(string operation, int result)
        {
            var message = _handle is null ? "Database handle unavailable" : raw.sqlite3_errmsg(_handle).utf8_to_string();
            return new InvalidOperationException($"SQLite could not {operation} ({result}): {message}");
        }

        public void Dispose()
        {
            var handle = Interlocked.Exchange(ref _handle, null);
            if (handle is not null)
            {
                raw.sqlite3_close_v2(handle);
            }
        }
    }

    private sealed class Statement(Database database, sqlite3_stmt handle) : IDisposable
    {
        private sqlite3_stmt? _handle = handle;
        private int? _lastStepResult;

        private sqlite3_stmt Handle => _handle ?? throw new ObjectDisposedException(nameof(Statement));

        internal void BindText(int index, string value) => database.Check(raw.sqlite3_bind_text(Handle, index, value), "bind text");

        internal void BindInteger(int index, int value) => database.Check(raw.sqlite3_bind_int(Handle, index, value), "bind integer");

        internal int Step()
        {
            var result = raw.sqlite3_step(Handle);
            _lastStepResult = result;
            return result;
        }

        internal string Text(int index) => raw.sqlite3_column_text(Handle, index).utf8_to_string()
            ?? throw new InvalidOperationException("SQLite returned an unexpected null account field.");

        internal int Integer(int index) => raw.sqlite3_column_int(Handle, index);

        internal void ExpectDone(int? result = null)
        {
            var actual = result ?? _lastStepResult;
            if (actual != raw.SQLITE_DONE)
            {
                throw database.Error("finish statement", actual ?? raw.SQLITE_MISUSE);
            }
        }

        public void Dispose()
        {
            var statement = Interlocked.Exchange(ref _handle, null);
            if (statement is not null)
            {
                raw.sqlite3_finalize(statement);
            }
        }
    }
}
