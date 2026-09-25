using Microsoft.Extensions.Options;
using PrimeScore.Modules.Classification.Classifier;
using PrimeScore.Modules.Classification.Consensus;
using PrimeScore.Modules.Classification.Contracts;

namespace PrimeScore.Modules.Classification.Tests;

public sealed class AnswerAndConsensusTests
{
    private const string Valid = """
        {"score":0.9992,"score_type":"ANOMALY_DETECTION","certainty":1.0,"history_sufficiency":1.0,"temporal_relevance":1.0,
         "event_taxonomy":null,"classification_method":"RULE_BASED","reasoning_trace":"VIX=37.32 vs median",
         "computed_metrics":{"window_degenerate":true,"unknown_indicator":false}}
        """;

    [Fact]
    public void A_contract_valid_answer_is_accepted_with_its_true_markers_as_flags()
    {
        var outcome = ClassifierClient.Validate(Valid);

        Assert.NotNull(outcome.Answer);
        Assert.Equal(0.9992, outcome.Answer.Score);
        Assert.Equal(["window_degenerate"], outcome.Answer.Flags);
    }

    [Theory]
    [InlineData("\"score\":0.9992", "\"score\":1.5", "score")]
    [InlineData("\"certainty\":1.0,", "\"certainty\":-0.1,", "certainty")]
    [InlineData("\"reasoning_trace\":\"VIX=37.32 vs median\"", "\"reasoning_trace\":\"\"", "reasoning_trace")]
    [InlineData("\"classification_method\":\"RULE_BASED\"", "\"classification_method\":\"GUESS\"", "classification_method")]
    [InlineData("\"score_type\":\"ANOMALY_DETECTION\",", "", "score_type")]
    public void A_contract_violation_rejects_the_whole_answer_as_invalid(string original, string replacement, string field)
    {
        var outcome = ClassifierClient.Validate(Valid.Replace(original, replacement, StringComparison.Ordinal));

        Assert.Null(outcome.Answer);
        Assert.Equal(UnavailableReason.InvalidResponse, outcome.Reason);
        Assert.Contains(field, outcome.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_negative_event_assessment_is_invalid_because_those_scores_lie_in_0_to_1()
    {
        var outcome = ClassifierClient.Validate(Valid
            .Replace("\"score\":0.9992", "\"score\":-0.4", StringComparison.Ordinal)
            .Replace("ANOMALY_DETECTION", "EVENT_ASSESSMENT", StringComparison.Ordinal));

        Assert.Equal(UnavailableReason.InvalidResponse, outcome.Reason);
    }

    [Fact]
    public void A_body_that_is_not_a_json_object_is_invalid() =>
        Assert.Equal(UnavailableReason.InvalidResponse, ClassifierClient.Validate("[1,2]").Reason);

    [Fact]
    public void Consensus_rows_need_a_source_a_url_and_a_retrieval_time()
    {
        var (rows, status) = ConsensusBook.Parse("CPI_YOY", "CPI_YOY.csv",
        [
            "release_date,actual_source,consensus,consensus_source,consensus_url,retrieved_at",
            "2022-07-13,BLS,8.8,Reuters poll,https://example.org/cpi-2022-07,2026-09-25T09:00:00Z",
            "2022-08-10,BLS,8.7,,https://example.org/cpi-2022-08,2026-09-25T09:00:00Z",
            "2022-09-13,BLS,8.1,Reuters poll,not-a-url,2026-09-25T09:00:00Z",
            "2022-10-13,BLS,8.1,Reuters poll,https://example.org/cpi-2022-10,yesterday",
            "2022-07-13,BLS,9.0,Reuters poll,https://example.org/dup,2026-09-25T09:00:00Z",
            "\"2022-11-10\",\"BLS, CPI release\",7.9,\"Reuters poll\",https://example.org/cpi-2022-11,2026-09-25T09:00:00Z",
        ]);

        Assert.Equal([new DateOnly(2022, 7, 13), new DateOnly(2022, 11, 10)], rows.Keys.Order());
        Assert.Equal(8.8, rows[new DateOnly(2022, 7, 13)].Consensus);
        Assert.Equal("BLS, CPI release", rows[new DateOnly(2022, 11, 10)].ActualSource);
        Assert.Equal(4, status.RejectedRows.Count);
        Assert.Contains(status.RejectedRows, row => row.Contains("consensus_source is required", StringComparison.Ordinal));
        Assert.Contains(status.RejectedRows, row => row.Contains("consensus_url", StringComparison.Ordinal));
        Assert.Contains(status.RejectedRows, row => row.Contains("retrieved_at", StringComparison.Ordinal));
        Assert.Contains(status.RejectedRows, row => row.Contains("duplicate release_date", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{"history_length":3,"ecdf_rank":null}""", true)]
    [InlineData("""{"history_length":300,"ecdf_rank":0.97}""", false)]
    [InlineData("""{"ecdf_rank":null}""", false)]
    public void Insufficient_history_is_flagged_only_when_the_classifier_reports_a_length_and_no_rank(string metrics, bool flagged)
    {
        var outcome = ClassifierClient.Validate(Valid.Replace("""{"window_degenerate":true,"unknown_indicator":false}""", metrics, StringComparison.Ordinal));

        Assert.Equal(flagged, outcome.Answer!.Flags.Contains("insufficient_history"));
    }

    [Fact]
    public void A_consensus_file_that_cannot_be_read_keeps_its_last_good_rows_and_says_why()
    {
        var directory = Path.Combine(Path.GetTempPath(), "primescore-consensus-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "INITIAL_CLAIMS.csv");
        const string header = "release_date,actual_source,consensus,consensus_source,consensus_url,retrieved_at";
        var release = new DateOnly(2026, 4, 9);
        try
        {
            File.WriteAllLines(path, [header, "2026-04-09,DOL,210000,Survey,https://example.org/a,2026-09-25T09:00:00Z"]);
            var book = new ConsensusBook(Options.Create(new ConsensusOptions { Directory = directory }));
            Assert.Equal(210000, book.Find("INITIAL_CLAIMS", release)!.Consensus);

            File.WriteAllLines(path, [header, "2026-04-09,DOL,212000,Survey,https://example.org/a,2026-09-25T10:00:00Z"]);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.Equal(210000, book.Find("INITIAL_CLAIMS", release)!.Consensus);
                Assert.Contains("could not be read", Assert.Single(book.Status()).Error, StringComparison.Ordinal);
            }

            Assert.Equal(212000, book.Find("INITIAL_CLAIMS", release)!.Consensus);
            Assert.Null(Assert.Single(book.Status()).Error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("../INITIAL_CLAIMS")]
    [InlineData("..\\INITIAL_CLAIMS")]
    [InlineData("C:INITIAL_CLAIMS")]
    [InlineData("")]
    public void Only_a_symbol_shaped_name_can_select_a_consensus_file(string indicator)
    {
        var book = new ConsensusBook(Options.Create(new ConsensusOptions { Directory = Path.GetTempPath() }));

        Assert.Null(book.Find(indicator, new DateOnly(2026, 4, 9)));
    }

    [Fact]
    public void A_file_with_the_wrong_header_contributes_no_rows()
    {
        var (rows, status) = ConsensusBook.Parse("CPI_YOY", "CPI_YOY.csv", ["date,consensus", "2022-07-13,8.8"]);

        Assert.Empty(rows);
        Assert.Contains("header must be", Assert.Single(status.RejectedRows), StringComparison.Ordinal);
    }
}
