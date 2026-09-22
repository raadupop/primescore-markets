# Local market replay

The dashboard demonstrates **historical observation → real HTTP classification → experimental volatility scenario**. It is a Python replay adapter, separate from the designed .NET engine.

## Run

From the repository root with Python 3.12:

```sh
python -m venv .venv
```

Activate with `.venv\Scripts\Activate.ps1` in PowerShell or `source .venv/bin/activate` in Bash, then:

```sh
python -m pip install -r apps/classification/requirements.txt
python scripts/demo.py
```

Open **http://127.0.0.1:8080**, choose a snapshot and click **Replay selected**. If that port is occupied, use `python scripts/demo.py --port 8081`. Stop with Ctrl+C. The launcher binds to loopback, uses one worker, disables provider bootstrap and selects the repository's registry; no API keys or network data fetches are required after dependency installation.

## Three views

| View | Shows |
| --- | --- |
| Event tape | Eight sourced VIX/OVX historical observations, event date, provider and replay status. |
| Classification | Signed severity, certainty dimensions, prior-close chart, reasoning trace and source provenance from a real `POST /classify` response. |
| Volatility scenario | Observed index, scenario index, signed gap and visibly uncalibrated multiplier `k ∈ [0, 1]`. |

**Full sourced window** loads 1,260 prior closes. **One prior close** deliberately withholds the rest to exercise degraded classification; no scenario is produced. Explicit classifier degradation markers also withhold scenarios even when certainty is positive; the control and contract limitation live in [demo limitations](../apps/demo/LIMITATIONS.md). **Reset** clears the windows. Every replay independently reseeds the selected snapshot, so repeated or concurrent requests do not append across historical events.

The interface uses plain HTML/CSS/JavaScript with native SVG, served by the existing FastAPI/uvicorn dependencies. No frontend build, plotting library, browser API keys or external assets are needed. The adapter holds a lock across seeding and its loopback HTTP request; a process-local token prevents direct classifier calls from interfering with seeded history in this demo.

## Meaning and data limits

```text
single_event_conviction = severity × certainty
scenario_iv = observed_iv × (1 + single_event_conviction × k)
gap = scenario_iv − observed_iv
```

This is arithmetic under a chosen assumption. The same index observation supplies the classifier input and observed IV; no independent fair-value forecast, multi-source CLS-002 aggregation, options pricing, trading recommendation or outcome backtest is implemented. `k` is uncalibrated. The bound keeps scenario IV nonnegative, including zero at the compression boundary. Certainty measures history coverage and update cadence; it does not establish whether a market has absorbed an event.

Data comes from the existing [acceptance fixtures](../apps/classification/tests/acceptance/fixtures/ANCHORS.md), recorded as FRED [VIXCLS](https://fred.stlouisfed.org/series/VIXCLS) and [OVXCLS](https://fred.stlouisfed.org/series/OVXCLS) observations. Each snapshot retains its source URL, retrieval dates and declared historical range; trader signoff remains pending. Charts use observation index because timestamps for individual prior closes are absent. Event names in fixture filenames are historical labels, not results of geopolitical/headline classification. Third-party data retains provider terms; see [licence](../README.md#licence).

## HTTP and checks

`GET /demo/api/events` lists snapshots. `POST /demo/api/replay` accepts `event_id`, optional `sensitivity` (default `0.25`) and `history_mode` (`full` or `one_prior_close`). `POST /demo/api/reset` clears readiness and history. The demo protects direct `POST /classify`; the standalone classifier retains its normal interface. Invalid inputs return 422, unknown snapshots 404, and unavailable/invalid classifier responses 502 without a scenario.

```sh
python -m pytest tests/demo -q
```

These tests launch actual TCP servers and cover all snapshots, repeated/concurrent replay, reset, degraded history, unavailable classification, invalid inputs and independently calculated scenario boundaries. The shared [CI gate](../harness/check-suite.sh) also runs them.
