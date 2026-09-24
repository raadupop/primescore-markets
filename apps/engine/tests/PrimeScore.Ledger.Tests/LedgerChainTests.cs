using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PrimeScore.SharedKernel;

namespace PrimeScore.Ledger.Tests;

public sealed class LedgerChainTests
{
    private static readonly Guid EntityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly CorrelationId Correlation = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));

    // Expected hashes were computed with Python hashlib from the documented byte layout
    // (fields joined by U+001F, UTF-8, SHA-256, lowercase hex), not from this implementation.
    private const string FirstHash = "fd552d827a897a5dace3a69578c46593cb3bb5af8fb9b4eec92be3df7f6b7009";
    private const string SecondHash = "8db401763e232bfba4362617f8e81899f6f1b3f4737d3c182d102cd40a6bda10";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Genesis_entry_hash_matches_the_independently_computed_vector_fd552d82()
    {
        var hash = LedgerHash.Compute(
            LedgerHash.Genesis, 1, "2026-01-02T03:04:05.0000000Z", LedgerKinds.SignalIngested,
            "11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222", 3,
            "VIX close 17.31", """{"instrument":"VIX","value":17.31}""");

        Assert.Equal(FirstHash, hash);
    }

    [Fact]
    public void Second_entry_hash_chains_from_the_first_to_8db40176()
    {
        var hash = LedgerHash.Compute(
            FirstHash, 2, "2026-01-02T03:04:06.0000000Z", LedgerKinds.AssessmentRecorded,
            "33333333-3333-3333-3333-333333333333", "22222222-2222-2222-2222-222222222222", 3,
            "VIX severity +0.9992", """{"score":0.9992}""");

        Assert.Equal(SecondHash, hash);
    }

    [Fact]
    public async Task Stored_entry_carries_the_independently_computed_hash_for_the_same_fields()
    {
        await using var db = await LedgerTestDatabase.CreateAsync();

        // Payload given out of key order: storage canonicalizes it before hashing.
        var records = await db.Ledger.AppendAsync(
            [new LedgerAppend(LedgerKinds.SignalIngested, EntityId, Correlation, new ConfigVersion(3), "VIX close 17.31", """{"value":17.31,"instrument":"VIX"}""")],
            Token);

        var record = Assert.Single(records);
        Assert.Equal(1, record.Sequence);
        Assert.Equal(LedgerHash.Genesis, record.PrevHash);
        Assert.Equal("""{"instrument":"VIX","value":17.31}""", record.Payload);
        Assert.Equal(FirstHash, record.Hash);
    }

    [Fact]
    public async Task Appends_get_contiguous_sequences_and_each_links_to_the_previous_hash()
    {
        await using var db = await LedgerTestDatabase.CreateAsync();

        var first = await db.Ledger.AppendAsync([Entry("a"), Entry("b")], Token);
        var second = await db.Ledger.AppendAsync([Entry("c")], Token);

        var all = first.Concat(second).ToArray();
        Assert.Equal([1L, 2L, 3L], all.Select(record => record.Sequence));
        Assert.Equal(LedgerHash.Genesis, all[0].PrevHash);
        Assert.Equal(all[0].Hash, all[1].PrevHash);
        Assert.Equal(all[1].Hash, all[2].PrevHash);
    }

    private const string ForgedColumns =
        "(sequence, recorded_at, kind, entity_id, correlation_id, config_version, summary, payload, prev_hash, hash)";

    private const string ForgedValues =
        "'2020-01-01T00:00:00.0000000Z', 'SignalIngested', 'x', 'x', 1, 'forged', '{}', 'x', 'x')";

    [Theory]
    [InlineData("UPDATE ledger SET payload = '{}' WHERE sequence = 1")]
    [InlineData("DELETE FROM ledger WHERE sequence = 1")]
    [InlineData("INSERT OR REPLACE INTO ledger " + ForgedColumns + " VALUES (1, " + ForgedValues)]
    [InlineData("REPLACE INTO ledger " + ForgedColumns + " VALUES (1, " + ForgedValues)]
    [InlineData("INSERT INTO ledger " + ForgedColumns + " VALUES (5, " + ForgedValues)]
    [InlineData("UPDATE ledger_verifications SET ok = 1")]
    [InlineData("DELETE FROM ledger_verifications")]
    public async Task Storage_rejects_updates_deletes_overwrites_and_gaps(string sql)
    {
        await using var db = await LedgerTestDatabase.CreateAsync();
        await db.Ledger.AppendAsync([Entry("a")], Token);
        await db.Verifier.VerifyAsync(Token);

        var exception = await Assert.ThrowsAsync<SqliteException>(() => db.ExecuteRawAsync(sql));
        Assert.Contains("append-only", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Summaries_with_control_characters_are_rejected_because_U001F_separates_hash_fields()
    {
        await using var db = await LedgerTestDatabase.CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => db.Ledger.AppendAsync([Entry("left\u001Fright")], Token));

        Assert.Equal(0, await db.CountAsync("ledger"));
    }

    [Fact]
    public async Task Verification_flags_a_stored_summary_edited_to_contain_a_control_character()
    {
        await using var db = await LedgerTestDatabase.CreateAsync();
        await db.Ledger.AppendAsync([Entry("a"), Entry("b")], Token);
        await db.ExecuteRawAsync("DROP TRIGGER ledger_reject_update; UPDATE ledger SET summary = 'a' || char(31) || 'b' WHERE sequence = 2;");

        var result = await db.Verifier.VerifyAsync(Token);

        Assert.False(result.Ok);
        Assert.Equal(2, result.FirstInvalidSequence);
        Assert.Contains("control character", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Concurrent_verifications_during_appends_never_report_a_false_truncation()
    {
        await using var db = await LedgerTestDatabase.CreateAsync();
        await db.Ledger.AppendAsync([Entry("seed")], Token);

        var appends = Task.Run(async () =>
        {
            for (var index = 0; index < 20; index++)
            {
                await db.Ledger.AppendAsync([Entry($"entry {index}")], Token);
            }
        }, Token);
        var verifications = Enumerable.Range(0, 20).Select(_ => Task.Run(() => db.Verifier.VerifyAsync(Token), Token)).ToArray();
        await appends;
        var results = await Task.WhenAll(verifications);

        Assert.All(results, result => Assert.True(result.Ok, result.Reason));
    }

    [Fact]
    public async Task Verification_of_an_intact_chain_of_three_checks_all_three()
    {
        await using var db = await LedgerTestDatabase.CreateAsync();
        await db.Ledger.AppendAsync([Entry("a"), Entry("b"), Entry("c")], Token);

        var result = await db.Verifier.VerifyAsync(Token);

        Assert.True(result.Ok);
        Assert.Equal(3, result.EntriesChecked);
        Assert.Equal(3, result.HeadSequence);
        Assert.Null(result.FirstInvalidSequence);
    }

    [Fact]
    public async Task Verification_detects_a_payload_edited_after_the_triggers_are_dropped_at_entry_2()
    {
        await using var db = await LedgerTestDatabase.CreateAsync();
        await db.Ledger.AppendAsync([Entry("a"), Entry("b"), Entry("c")], Token);
        await db.ExecuteRawAsync("DROP TRIGGER ledger_reject_update; UPDATE ledger SET payload = '{\"note\":\"forged\"}' WHERE sequence = 2;");

        var result = await db.Verifier.VerifyAsync(Token);

        Assert.False(result.Ok);
        Assert.Equal(2, result.FirstInvalidSequence);
        Assert.Contains("hash", result.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("correlation_id", "'99999999-9999-9999-9999-999999999999'")]
    [InlineData("recorded_at", "'2020-01-01T00:00:00.0000000Z'")]
    [InlineData("config_version", "7")]
    [InlineData("summary", "'rewritten'")]
    public async Task Verification_detects_an_edit_to_any_stored_column(string column, string value)
    {
        await using var db = await LedgerTestDatabase.CreateAsync();
        await db.Ledger.AppendAsync([Entry("a"), Entry("b")], Token);
        await db.ExecuteRawAsync($"DROP TRIGGER ledger_reject_update; UPDATE ledger SET {column} = {value} WHERE sequence = 1;");

        var result = await db.Verifier.VerifyAsync(Token);

        Assert.False(result.Ok);
        Assert.Equal(1, result.FirstInvalidSequence);
    }

    [Fact]
    public async Task Verification_detects_a_deleted_middle_entry_as_a_sequence_gap_at_2()
    {
        await using var db = await LedgerTestDatabase.CreateAsync();
        await db.Ledger.AppendAsync([Entry("a"), Entry("b"), Entry("c")], Token);
        await db.ExecuteRawAsync("DROP TRIGGER ledger_reject_delete; DELETE FROM ledger WHERE sequence = 2;");

        var result = await db.Verifier.VerifyAsync(Token);

        Assert.False(result.Ok);
        Assert.Equal(2, result.FirstInvalidSequence);
        Assert.Contains("sequence gap", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verification_detects_truncation_below_a_previously_verified_head_of_3()
    {
        await using var db = await LedgerTestDatabase.CreateAsync();
        await db.Ledger.AppendAsync([Entry("a"), Entry("b"), Entry("c")], Token);
        Assert.True((await db.Verifier.VerifyAsync(Token)).Ok);
        await db.ExecuteRawAsync("DROP TRIGGER ledger_reject_delete; DELETE FROM ledger WHERE sequence = 3;");

        var result = await db.Verifier.VerifyAsync(Token);

        Assert.False(result.Ok);
        Assert.Equal(3, result.FirstInvalidSequence);
        Assert.Contains("previously verified head", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_kind_is_rejected_before_anything_is_written()
    {
        await using var db = await LedgerTestDatabase.CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => db.Ledger.AppendAsync(
            [new LedgerAppend("PositionOpened", EntityId, Correlation, ConfigVersion.None, "not a v1 kind", "{}")], Token));

        Assert.Equal(0, await db.CountAsync("ledger"));
    }

    [Fact]
    public async Task A_failing_projection_rolls_back_the_entries_and_their_log_lines()
    {
        await using var db = await LedgerTestDatabase.CreateAsync(new ThrowingProjection());

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Ledger.AppendAsync([Entry("a"), Entry("b")], Token));

        Assert.Equal(0, await db.CountAsync("ledger"));
        Assert.Equal(0, await db.CountAsync("obs_log"));
    }

    [Fact]
    public async Task Projection_rows_commit_with_the_entry_and_carry_its_sequence()
    {
        await using var db = await LedgerTestDatabase.CreateAsync(new ProbeProjection());
        await db.ExecuteRawAsync("CREATE TABLE test_probe (id INTEGER PRIMARY KEY AUTOINCREMENT, sequence INTEGER NOT NULL, summary TEXT NOT NULL)");

        await db.Ledger.AppendAsync([Entry("a"), Entry("b")], Token);

        await using var connection = db.Database.CreateConnection();
        await connection.OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sequence, summary FROM test_probe ORDER BY sequence";
        await using var reader = await command.ExecuteReaderAsync(Token);
        var rows = new List<(long, string)>();
        while (await reader.ReadAsync(Token))
        {
            rows.Add((reader.GetInt64(0), reader.GetString(1)));
        }

        Assert.Equal([(1L, "a"), (2L, "b")], rows);
    }

    [Fact]
    public async Task Pipeline_log_returns_one_line_per_entry_of_a_correlation_in_sequence_order()
    {
        await using var db = await LedgerTestDatabase.CreateAsync();
        var other = CorrelationId.New();
        await db.Ledger.AppendAsync(
        [
            Entry("ingested", LedgerKinds.SignalIngested),
            Entry("elsewhere", LedgerKinds.SignalIngested, other),
            Entry("assessed", LedgerKinds.AssessmentRecorded),
        ], Token);

        var lines = await db.Services.GetRequiredService<IPipelineLogQuery>().ByCorrelationAsync(Correlation.Value, Token);

        Assert.Equal(["ingestion", "classification"], lines.Select(line => line.Stage));
        Assert.Equal(["ingested", "assessed"], lines.Select(line => line.Message));
        Assert.Equal([1L, 3L], lines.Select(line => line.Sequence));
    }

    private static LedgerAppend Entry(string summary, string kind = LedgerKinds.SignalIngested, CorrelationId? correlation = null) =>
        LedgerAppend.Create(kind, EntityId, correlation ?? Correlation, new ConfigVersion(1), summary, new { summary });

    private sealed class ThrowingProjection : ILedgerProjection
    {
        public bool Handles(string kind) => string.Equals(kind, LedgerKinds.SignalIngested, StringComparison.Ordinal);

        public Task ProjectAsync(LedgerRecord record, ProjectionScope scope, CancellationToken cancellationToken) =>
            record.Sequence == 2 ? throw new InvalidOperationException("projection failed") : Task.CompletedTask;
    }

    private sealed class ProbeProjection : ILedgerProjection
    {
        public bool Handles(string kind) => true;

        public Task ProjectAsync(LedgerRecord record, ProjectionScope scope, CancellationToken cancellationToken)
        {
            var context = scope.Context<ProbeContext>("__ef_history_test_probe", options => new ProbeContext(options));
            context.Rows.Add(new ProbeRow { Sequence = record.Sequence, Summary = record.Summary });
            return Task.CompletedTask;
        }
    }

    private sealed class ProbeContext(DbContextOptions<ProbeContext> options) : DbContext(options)
    {
        public DbSet<ProbeRow> Rows => Set<ProbeRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<ProbeRow>(row =>
            {
                row.ToTable("test_probe");
                row.Property(probe => probe.Id).HasColumnName("id");
                row.Property(probe => probe.Sequence).HasColumnName("sequence");
                row.Property(probe => probe.Summary).HasColumnName("summary");
            });
    }

    private sealed class ProbeRow
    {
        public long Id { get; set; }

        public long Sequence { get; set; }

        public string Summary { get; set; } = "";
    }
}
