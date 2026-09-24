using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Json;

namespace PrimeScore.Ledger.Storage;

/// <summary>
/// Single-writer ledger over SQLite. Appends serialize on an in-process lock and an immediate
/// transaction; storage triggers reject UPDATE and DELETE (SRS AUD-001), and the hash chain
/// detects edits made around them (SRS AUD-002).
/// </summary>
internal sealed partial class SqliteLedger(
    EngineDatabase database,
    IEnumerable<ILedgerProjection> projections,
    IClock clock,
    ILogger<SqliteLedger> logger)
    : ILedger, ILedgerVerifier, ILedgerStatusQuery, IPipelineLogQuery, ILedgerAuditQuery, IDisposable
{
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";
    private const string EntryColumns =
        "sequence, recorded_at, kind, entity_id, correlation_id, config_version, summary, payload, prev_hash, hash";

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _verifyLock = new(1, 1);
    private readonly ILedgerProjection[] _projections = projections.ToArray();

    public async Task<IReadOnlyList<LedgerRecord>> AppendAsync(IReadOnlyList<LedgerAppend> entries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            return [];
        }

        var prepared = entries.Select(Prepare).ToArray();
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = database.CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var (headSequence, headHash) = await ReadHeadAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            var records = new List<LedgerRecord>(prepared.Length);
            await using (var scope = new ProjectionScope(connection, transaction))
            {
                foreach (var entry in prepared)
                {
                    var record = await InsertAsync(connection, transaction, entry, headSequence + 1, headHash, cancellationToken).ConfigureAwait(false);
                    await InsertLogAsync(connection, transaction, record, cancellationToken).ConfigureAwait(false);
                    foreach (var projection in _projections.Where(projection => projection.Handles(record.Kind)))
                    {
                        await projection.ProjectAsync(record, scope, cancellationToken).ConfigureAwait(false);
                    }

                    await scope.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    headSequence = record.Sequence;
                    headHash = record.Hash;
                    records.Add(record);
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return records;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<LedgerVerification> VerifyAsync(CancellationToken cancellationToken)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        long checkedCount = 0;
        long expected = 1;
        var previousHash = LedgerHash.Genesis;
        long? firstInvalid = null;
        string? reason = null;

        await _verifyLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // One read snapshot for the chain and the last verified head, so a verification
            // recorded concurrently (here or in another process) cannot look like truncation.
            await using (var snapshot = connection.BeginTransaction(deferred: true))
            {
                await using (var command = connection.CreateCommand())
                {
                    command.Transaction = snapshot;
                    command.CommandText = $"SELECT {EntryColumns} FROM ledger ORDER BY sequence";
                    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var row = ReadRow(reader);
                        (firstInvalid, reason) = CheckRow(row, expected, previousHash);
                        if (firstInvalid is not null)
                        {
                            break;
                        }

                        previousHash = row.Hash;
                        expected++;
                        checkedCount++;
                    }
                }

                if (firstInvalid is null)
                {
                    // Truncation leaves a valid shorter chain; compare with the last verified head.
                    var lastHead = await ReadLastVerifiedHeadAsync(connection, snapshot, cancellationToken).ConfigureAwait(false);
                    if (lastHead is { } head
                        && (head.Sequence > expected - 1 || !await HashAtAsync(connection, snapshot, head.Sequence, head.Hash, cancellationToken).ConfigureAwait(false)))
                    {
                        (firstInvalid, reason) = (Math.Min(head.Sequence, expected), $"previously verified head {head.Sequence} is missing or changed");
                    }
                }

                await snapshot.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            var verification = new LedgerVerification(
                firstInvalid is null, checkedCount, expected - 1, firstInvalid, reason, clock.UtcNow);
            await RecordVerificationAsync(connection, verification, previousHash, cancellationToken).ConfigureAwait(false);
            if (!verification.Ok)
            {
                LogVerificationFailed(verification.FirstInvalidSequence, verification.Reason);
            }

            return verification;
        }
        finally
        {
            _verifyLock.Release();
        }
    }

    private static (long? FirstInvalid, string? Reason) CheckRow(LedgerRow row, long expected, string previousHash)
    {
        if (row.Sequence != expected)
        {
            return (expected, $"sequence gap: expected {expected}, found {row.Sequence}");
        }

        if (!string.Equals(row.PrevHash, previousHash, StringComparison.Ordinal))
        {
            return (row.Sequence, "prev_hash does not match the preceding entry's hash");
        }

        // Appends reject control characters in summaries; one here means the row was edited
        // to shift the field boundary that U+001F marks in the hash preimage.
        if (row.Summary.Any(char.IsControl))
        {
            return (row.Sequence, "summary contains a control character");
        }

        var recomputed = LedgerHash.Compute(
            row.PrevHash, row.Sequence, row.RecordedAt, row.Kind, row.EntityId, row.CorrelationId,
            row.ConfigVersion, row.Summary, row.Payload);
        return string.Equals(recomputed, row.Hash, StringComparison.Ordinal)
            ? (null, null)
            : (row.Sequence, "stored hash does not match the recomputed hash");
    }

    public async Task<LedgerStatus> GetAsync(CancellationToken cancellationToken)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var (headSequence, headHash) = await ReadHeadAsync(connection, null, cancellationToken).ConfigureAwait(false);
        long entries;
        await using (var count = connection.CreateCommand())
        {
            count.CommandText = "SELECT COUNT(*) FROM ledger";
            entries = Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }

        return new LedgerStatus(entries, headSequence, headHash, await ReadLastVerificationAsync(connection, cancellationToken).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<PipelineLogEntry>> ByCorrelationAsync(Guid correlationId, CancellationToken cancellationToken)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT correlation_id, stage, timestamp, message, kind, entity_id, sequence, config_version " +
            "FROM obs_log WHERE correlation_id = $correlation ORDER BY sequence";
        command.Parameters.AddWithValue("$correlation", LedgerHash.FormatId(correlationId));
        var entries = new List<PipelineLogEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(new PipelineLogEntry(
                new CorrelationId(Guid.Parse(reader.GetString(0))),
                reader.GetString(1),
                ParseTimestamp(reader.GetString(2)),
                reader.GetString(3),
                reader.GetString(4),
                Guid.Parse(reader.GetString(5)),
                reader.GetInt64(6),
                new ConfigVersion(reader.GetInt32(7))));
        }

        return entries;
    }

    public async Task<LedgerRecord?> BySequenceAsync(long sequence, CancellationToken cancellationToken)
    {
        var segment = await SegmentAsync(sequence, sequence, cancellationToken).ConfigureAwait(false);
        return segment.Count == 0 ? null : segment[0];
    }

    public async Task<IReadOnlyList<LedgerRecord>> SegmentAsync(long fromSequence, long toSequence, CancellationToken cancellationToken)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {EntryColumns} FROM ledger WHERE sequence BETWEEN $from AND $to ORDER BY sequence";
        command.Parameters.AddWithValue("$from", fromSequence);
        command.Parameters.AddWithValue("$to", toSequence);
        var records = new List<LedgerRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            records.Add(ToRecord(ReadRow(reader)));
        }

        return records;
    }

    public void Dispose()
    {
        _writeLock.Dispose();
        _verifyLock.Dispose();
    }

    private static LedgerAppend Prepare(LedgerAppend entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!LedgerKinds.All.Contains(entry.Kind))
        {
            throw new ArgumentException($"Unknown ledger kind '{entry.Kind}'.", nameof(entry));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Summary);
        if (entry.Summary.Any(char.IsControl))
        {
            // U+001F separates hash fields; control characters would make the preimage ambiguous.
            throw new ArgumentException("Ledger summaries must not contain control characters.", nameof(entry));
        }

        return entry with { Payload = CanonicalJson.Canonicalize(entry.Payload) };
    }

    private async Task<LedgerRecord> InsertAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LedgerAppend entry,
        long sequence,
        string prevHash,
        CancellationToken cancellationToken)
    {
        var row = new LedgerRow
        {
            Sequence = sequence,
            RecordedAt = LedgerHash.FormatTimestamp(clock.UtcNow),
            Kind = entry.Kind,
            EntityId = LedgerHash.FormatId(entry.EntityId),
            CorrelationId = LedgerHash.FormatId(entry.CorrelationId.Value),
            ConfigVersion = entry.ConfigVersion.Value,
            Summary = entry.Summary,
            Payload = entry.Payload,
            PrevHash = prevHash,
        };
        row.Hash = LedgerHash.Compute(
            row.PrevHash, row.Sequence, row.RecordedAt, row.Kind, row.EntityId, row.CorrelationId,
            row.ConfigVersion, row.Summary, row.Payload);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"INSERT INTO ledger ({EntryColumns}) VALUES " +
            "($sequence, $recorded_at, $kind, $entity_id, $correlation_id, $config_version, $summary, $payload, $prev_hash, $hash)";
        command.Parameters.AddWithValue("$sequence", row.Sequence);
        command.Parameters.AddWithValue("$recorded_at", row.RecordedAt);
        command.Parameters.AddWithValue("$kind", row.Kind);
        command.Parameters.AddWithValue("$entity_id", row.EntityId);
        command.Parameters.AddWithValue("$correlation_id", row.CorrelationId);
        command.Parameters.AddWithValue("$config_version", row.ConfigVersion);
        command.Parameters.AddWithValue("$summary", row.Summary);
        command.Parameters.AddWithValue("$payload", row.Payload);
        command.Parameters.AddWithValue("$prev_hash", row.PrevHash);
        command.Parameters.AddWithValue("$hash", row.Hash);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return ToRecord(row);
    }

    private static async Task InsertLogAsync(SqliteConnection connection, SqliteTransaction transaction, LedgerRecord record, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO obs_log (sequence, correlation_id, stage, timestamp, message, kind, entity_id, config_version) " +
            "VALUES ($sequence, $correlation_id, $stage, $timestamp, $message, $kind, $entity_id, $config_version)";
        command.Parameters.AddWithValue("$sequence", record.Sequence);
        command.Parameters.AddWithValue("$correlation_id", LedgerHash.FormatId(record.CorrelationId.Value));
        command.Parameters.AddWithValue("$stage", LedgerKinds.Stage(record.Kind));
        command.Parameters.AddWithValue("$timestamp", LedgerHash.FormatTimestamp(record.RecordedAt));
        command.Parameters.AddWithValue("$message", record.Summary);
        command.Parameters.AddWithValue("$kind", record.Kind);
        command.Parameters.AddWithValue("$entity_id", LedgerHash.FormatId(record.EntityId));
        command.Parameters.AddWithValue("$config_version", record.ConfigVersion.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(long Sequence, string Hash)> ReadHeadAsync(SqliteConnection connection, SqliteTransaction? transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT sequence, hash FROM ledger ORDER BY sequence DESC LIMIT 1";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? (reader.GetInt64(0), reader.GetString(1))
            : (0, LedgerHash.Genesis);
    }

    private static async Task<bool> HashAtAsync(SqliteConnection connection, SqliteTransaction snapshot, long sequence, string hash, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = snapshot;
        command.CommandText = "SELECT hash FROM ledger WHERE sequence = $sequence";
        command.Parameters.AddWithValue("$sequence", sequence);
        var stored = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return string.Equals(stored, hash, StringComparison.Ordinal);
    }

    private static async Task<(long Sequence, string Hash)?> ReadLastVerifiedHeadAsync(SqliteConnection connection, SqliteTransaction snapshot, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = snapshot;
        command.CommandText =
            "SELECT head_sequence, head_hash FROM ledger_verifications WHERE ok = 1 AND head_sequence > 0 ORDER BY id DESC LIMIT 1";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? (reader.GetInt64(0), reader.GetString(1))
            : null;
    }

    private static async Task<LedgerVerification?> ReadLastVerificationAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT ok, entries_checked, head_sequence, first_invalid_sequence, reason, verified_at " +
            "FROM ledger_verifications ORDER BY id DESC LIMIT 1";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new LedgerVerification(
            reader.GetBoolean(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.IsDBNull(3) ? null : reader.GetInt64(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            ParseTimestamp(reader.GetString(5)));
    }

    private static async Task RecordVerificationAsync(SqliteConnection connection, LedgerVerification verification, string headHash, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO ledger_verifications (verified_at, ok, entries_checked, head_sequence, head_hash, first_invalid_sequence, reason) " +
            "VALUES ($verified_at, $ok, $entries_checked, $head_sequence, $head_hash, $first_invalid, $reason)";
        command.Parameters.AddWithValue("$verified_at", LedgerHash.FormatTimestamp(verification.VerifiedAt));
        command.Parameters.AddWithValue("$ok", verification.Ok);
        command.Parameters.AddWithValue("$entries_checked", verification.EntriesChecked);
        command.Parameters.AddWithValue("$head_sequence", verification.HeadSequence);
        command.Parameters.AddWithValue("$head_hash", headHash);
        command.Parameters.AddWithValue("$first_invalid", (object?)verification.FirstInvalidSequence ?? DBNull.Value);
        command.Parameters.AddWithValue("$reason", (object?)verification.Reason ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static LedgerRow ReadRow(SqliteDataReader reader) => new()
    {
        Sequence = reader.GetInt64(0),
        RecordedAt = reader.GetString(1),
        Kind = reader.GetString(2),
        EntityId = reader.GetString(3),
        CorrelationId = reader.GetString(4),
        ConfigVersion = reader.GetInt32(5),
        Summary = reader.GetString(6),
        Payload = reader.GetString(7),
        PrevHash = reader.GetString(8),
        Hash = reader.GetString(9),
    };

    private static LedgerRecord ToRecord(LedgerRow row) => new(
        row.Sequence,
        ParseTimestamp(row.RecordedAt),
        row.Kind,
        Guid.Parse(row.EntityId),
        new CorrelationId(Guid.Parse(row.CorrelationId)),
        new ConfigVersion(row.ConfigVersion),
        row.Summary,
        row.Payload,
        row.PrevHash,
        row.Hash);

    private static DateTimeOffset ParseTimestamp(string text) =>
        DateTimeOffset.ParseExact(text, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    [LoggerMessage(Level = LogLevel.Error, Message = "Ledger verification failed at sequence {Sequence}: {Reason}")]
    private partial void LogVerificationFailed(long? sequence, string? reason);
}
