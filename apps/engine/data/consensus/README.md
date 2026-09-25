# Macro consensus

One CSV per registry indicator (`CPI_YOY.csv`, `INITIAL_CLAIMS.csv`), curated by the operator. A macro print is classified only when its release date has a row here; otherwise it is shown as **awaiting consensus** and never compared with an invented expectation (brief §7).

| Column | Content |
| --- | --- |
| `release_date` | New York date of the release, `YYYY-MM-DD` |
| `actual_source` | Where the actual was published (optional) |
| `consensus` | Median expectation in the indicator's unit (CPI YoY in percent, claims as a count) |
| `consensus_source` | Who published the expectation (required) |
| `consensus_url` | Archived page showing it (required, `http(s)`) |
| `retrieved_at` | When the operator read it, ISO 8601 (required) |

Rows missing a source, URL or retrieval time are rejected and listed on **Sources and health**. Confirm that the publisher's terms permit reuse before adding rows. CPI YoY actuals are derived from the seasonally adjusted index; see [LIMITATIONS.md](../../LIMITATIONS.md).
