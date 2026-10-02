# Acceptance catalyst fixtures

`CatalystTests` serves these from a local stub server or passes them to the CLI.

- `VIX_History.csv`, `VIX9D_History.csv`: the real column layout of Cboe's daily index history files
  (`DATE,OPEN,HIGH,LOW,CLOSE`, dates `MM/dd/yyyy`, six decimals) with **invented values**; no Cboe data is in this
  repository (Cboe terms: internal use, no redistribution). One row per NYSE trading day from 2026-01-02 to
  2026-09-24. VIX closes 20 on even days of the month and 25 on odd days. VIX9D closes 1.2 × VIX (ratio +0.20),
  except on these read dates, where the ratio is written by hand: 2026-01-22 −0.15, 02-05 −0.13, 03-05 −0.11,
  03-12 −0.09, 04-23 −0.07, 05-07 −0.05, 06-04 −0.03, 06-11 −0.01, 07-23 +0.01, 08-06 +0.03, and 09-24 −0.055
  (the as-of date for FOMC-2026-10-28). OPEN = close, HIGH/LOW = close ± 0.5.
- `SPX_History.csv`: the real layout of Cboe's SPX file (`DATE,SPX`) with **invented values** on the same dates:
  5000 except on the six past 2026 FOMC decision days and their window ends (read date + 9 calendar days), whose
  values `EventRecordTests` derives by hand.
- `VIX3M_History.csv`, `VIX6M_History.csv`: the same layout and dates with **invented** constant closes, 22 and 23,
  for the volatility term structure of `PositionScenarioTests`.
- `fomccalendars.htm`: excerpt of <https://www.federalreserve.gov/monetarypolicy/fomccalendars.htm> fetched
  2026-09-29 (public domain; Federal Reserve Board), the 2026 panel only, markup as served.
- `import-2014.csv`: one curated CPI row in the `import-catalysts` format (the 16 January 2014 release as the BLS
  2014 archived schedule lists it). `import-invalid.csv`: the same header and a row dated month 13.
