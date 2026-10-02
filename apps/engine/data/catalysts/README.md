# Curated catalyst dates

`opec.csv` holds OPEC and JMMC meeting dates curated by the operator. opec.org serves its listings
behind a browser challenge, which the engine does not work around (ADR-0011), so these dates are
typed in by hand. The `OpecCalendar` source reads this file when `Sources:OpecCalendar:Enabled` is
true (`Sources:OpecCalendar:File` points elsewhere). The same format is the back-fill path for any
family: `import-catalysts --file <csv>`.

| Column | Content |
| --- | --- |
| `family` | `FOMC`, `CPI`, `NFP`, `CLAIMS`, `GDP`, `PCE`, `WPSR` or `OPEC` (only `OPEC` in `opec.csv`) |
| `scheduled_date` | New York date of the event, `YYYY-MM-DD` |
| `scheduled_time_new_york` | `HH:mm`, or empty when no time has been announced |
| `originally_scheduled_date` | Empty, or the date first announced when the event later moved |
| `title` | Free text (defaults to the family) |
| `reference_period` | Free text (optional) |
| `source_key` | Key that matches the calendar sources' rows: `CPI:2013-12`, `NFP:2013-09`, `GDP:2026Q3:advance`, `PCE:august 2026`, `WPSR:2026-09-25` (week ending), `CLAIMS:2026-09-26` (Saturday ending the claims week); free text for OPEC; FOMC may stay empty |
| `sep` | `true`, `false` or empty (FOMC only) |
| `source_url` | The page that states the date, `http(s)` (required) |
| `retrieved_at` | When that page was read, ISO 8601 with an offset (required; not in the future) |
| `verified_by` | Who checked the row against the page (required) |
| `note` | Free text |

The file is validated as a whole: one invalid row records nothing, and every problem is listed
with its line number on **Data & health**. Quote a field that contains a comma. Example (in
prose, not in the file): OPEC's press release of 5 April 2026
(<https://www.opec.org/pr-detail/1574596-5-april-2026.html>) states that the 66th JMMC meeting is
scheduled for 7 June 2026, with no time; its row would be `OPEC`, `2026-06-07`, empty time,
`source_key` `JMMC-66`, that URL, the time you read it and your name. Confirm that the publisher's
terms permit reuse before adding rows.
