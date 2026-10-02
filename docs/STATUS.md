# PrimeScore AI — repository status

**2 October 2026.** PrimeScore Markets records sourced observations, statistical classifications, volatility states, simulation decisions and their measured outcomes in a local .NET application. No independent volatility forecast or order execution is implemented. [Setup and operation](ENGINE.md).

| Component | Current state | Evidence / remaining work |
| --- | --- | --- |
| Python classifier | Partial product; two executable routes | Signed market/macro scores, explicit fit rejection, horizon-based confidence and readiness checks. Cross-asset and both geopolitical routes remain HTTP 501; [calibration and provider gaps](../apps/classification/LIMITATIONS.md). |
| Replay dashboard | Built | Eight historical VIX/OVX snapshots through real HTTP classification and a labeled scenario; three views, repeat/reset and degraded/error handling. Run `python scripts/demo.py`; [setup](DEMO.md). |
| Agent harness and CI | Built | Both OpenAPI contracts, Python dependency and fitness checks, .NET structural tests and HTTP acceptance tests share [one gate](../harness/ORACLE.md). [Remaining harness limits](../apps/classification/HARNESS.md). |
| Markets presentation page | Built | Static HTML, CSS and JavaScript; [product source](../sites/markets/). The brand source and Cloudflare workspace belong to `D:\Work\primescore`. |
| Markets engine | Slices 1–2 committed; slice 3 (event record) built, awaiting the operator's click-through ([slices](../doc/slices/README.md)); decision gate replaced (ADR-0008) | Cboe index ingestion (off by default), FRED demoted to a read-only cross-check (ADR-0009), release calendars and the Catalysts page (off by default; no prints or consensus yet; ADR-0011, ADR-0012), ledger, classifications, composites, state-gated decisions, replay, outcomes record, versioned settings, Blazor UI and sign-in. The directional scenario was refuted on 3,841 signalled days drawn from 15,849 recorded decisions; decisions now record a volatility state without direction. [Operational and data limits](../apps/engine/LIMITATIONS.md). |
| Predictive signal | None validated | Four pre-registered hypotheses run once on 2019 onward: futures carry, variance premium and move magnitude killed; catalyst crush inconclusive. [Pre-registration and results](../doc/research/preregistration-2026-09-27.md), [redesign](../doc/research/redesign-2026-09-27.md). |
| Positions, exits and risk | Deferred | API slots return 501; no options-price source or broker execution. |
| Contract and credit optimisation | Direction only | No implementation or product specification. |

The four skips are three macro anchors awaiting sourced history and one explicitly opt-in live FRED integration. Passing checks establish the tested software behavior, not predictive accuracy. The detailed audit below records the **starting state**, before these repairs.

<details>
<summary>Initial audit: component inventory, failures and effort estimates</summary>

Audited **22 September 2026**, at `4af489d` plus the existing working tree. This is the pre-rename baseline. **Built** means executable code or an authored artifact exists; **partial** means material behavior or validation is missing; **designed** means requirements, contracts or stubs only.

**Verdict:** a partially implemented event classifier and agent engineering harness exist. An end-to-end volatility platform does not. Two of five classification routes execute; the current suite is red. The README overstates live ingestion, geopolitical AI and trading behavior.

## Component inventory

| Component / evidence | State | What would make it demonstrable |
| --- | --- | --- |
| [FastAPI HTTP boundary](../apps/classification/main.py), [models](../apps/classification/app/models/), [classifier OpenAPI](../apps/classification/doc/openapi.yaml) | Built | `/classify` and `/health` exist; classifier OpenAPI validates. Supply reproducible history and example HTTP requests. |
| [Market-data classification](../apps/classification/app/strategies/market_data.py) | Partial | ECDF severity, certainty and reasoning execute. Restore signed severity, zero-deviation behavior and `N_L`-based history sufficiency. |
| [Macroeconomic classification](../apps/classification/app/strategies/macroeconomic.py) | Partial | Surprise ranking executes. Signed scores and configured parametric fallback/goodness-of-fit gate are missing; historical acceptance fixtures are skipped. |
| [Cross-asset classification](../apps/classification/app/strategies/cross_asset.py) | Designed | Stub returns HTTP 501. Implement correlation history, deviation calculation and reference scenarios. |
| [Geopolitical structured and unstructured classification](../apps/classification/app/strategies/geopolitical.py) | Designed | Both routes return HTTP 501. Implement model calls, response validation, failure handling and independent evaluation cases. |
| LLM/RAG dependency and corpus | Designed | Anthropic is a dependency, not an implemented integration. LangChain/Chroma dependencies are commented out; no RAG corpus exists. |
| [Indicator registry](../infra/registry.yaml), [loader](../apps/classification/app/registry.py), [rolling windows](../apps/classification/app/state.py) | Built | 13 symbols across four classes; typed configuration and in-memory windows work. Demonstrate restart/reset; parameter calibration and multi-symbol validation remain open. |
| [Historical bootstrap](../apps/classification/app/bootstrap/) | Partial | Only FRED fetcher exists. VIX/OVX/CPI paths exist; claims is skipped, CPI consensus ends December 2024, and fetch/seed code uses short `N` despite configured `N_L`. Complete horizon handling and supported-series readiness. |
| [Health/readiness and logging](../apps/classification/main.py) | Partial | Health exposes window age and count. Bootstrap sets ready even with zero windows; distinguish usable data from completed startup. Basic logging exists; monitoring/metrics do not. |
| Live market/macro/news ingestion | Designed | No scheduler, Twelve Data WebSocket, GDELT poller or Finnhub adapter. Implement an adapter with timestamps, validation, retry and source-health reporting. FRED startup history is not live ingestion. |
| [Composite scoring, CLS-002](../doc/srs/PrimeScore-SRS.md#cls-002-must) | Designed | No aggregation code. Implement corroboration, signed weighting, source availability and formula examples; resolve the range contradiction below. |
| [Signal-implied IV/dislocation, CLS-006](../doc/srs/PrimeScore-SRS.md#cls-006-must) | Designed | No executable forecast or signal output. An equation demonstration is small; an independently validated forecast requires calibration and outcome data. |
| Decision, positions, exits, portfolio risk | Designed | [SRS](../doc/srs/PrimeScore-SRS.md) and API schemas only. No options construction, broker integration, execution or risk-enforcement code. |
| Persistence, point-in-time replay, audit trail | Designed | No database or engine replay service. Implement ordered event storage, deterministic replay and exclusion of future information. |
| Engine authentication, authorization, observability | Designed | Contract/security requirements only. No engine server implementing them. |
| Six .NET architecture iterations | Designed | No C# source, project or solution files; ignored `apps/core/` is empty. Build Iteration 1 before claiming an implemented engine or architecture comparison. |
| [SRS](../doc/srs/PrimeScore-SRS.md), [engine API contract](../doc/PrimeScore-API-v1.yaml), ADRs | Partial | Substantial design artifacts exist. Engine OpenAPI fails validation; formulas and prose need reconciliation. Documents are not implementation evidence. |
| [Context, domain skills, permissions, ADR discipline](../doc/adr/0001-agent-harness-architecture.md) | Built | Root/component agent context, eight local skills, Claude settings and decision records exist. Demonstrate a constrained edit and inspect the resulting validation report. |
| [Hook adapters](../.claude/hooks/), [steering manager](../harness/steer.sh), [oracle scripts](../harness/) | Partial | Claude hooks are wired; retry budget, failure fingerprints and escalation exist. Five cognitive reminders remain unwired. Change detection misses untracked files and registry/harness-only edits; scripts lack executable Git modes for nested Linux invocation. |
| [Acceptance and fitness tests](../apps/classification/tests/) | Partial | Contract/health tests, axioms and four fitness wrappers exist; current failures listed below. Tests use HTTP assertions but inject in-memory state for setup. Missing tools can silently skip checks. |
| [Dependency-boundary contracts](../apps/classification/pyproject.toml) | Built | Six import-linter contracts pass when invoked directly. Neither pytest nor the current hooks invoke them; connect them to CI. |
| [Historical anchor fixtures](../apps/classification/tests/acceptance/fixtures/) | Partial | Eleven source-tagged snapshots: six passing market anchors, two expected failures, three skipped macro anchors. All retain pending trader signoff; descriptions contain stale formula explanations. |
| Independent classification outcome backtest and architecture measurements | Designed | No market-outcome oracle, calibrated predictive results or six-iteration benchmark results. [Limitations](../apps/classification/LIMITATIONS.md) describe the missing validation. |
| [Research visualization](../doc/research/phase1_convexity_analysis.tsx) | Partial | Standalone React/Recharts component with manually entered event and exploitability estimates. No package manifest, runnable app or classifier connection; unsuitable as performance evidence. |
| [Research, archives, conventions and task registry](../doc/) | Built | Authored material exists. Correct stale inventories and broken relative links; distinguish historical research assumptions from verified product behavior. |
| CI, licence, release/changelog, reproducible deployment | Designed — assets absent | `.github/` contains Copilot instructions only. Add workflow, licence decision, run instructions and release history. Requirements pin direct dependencies; no complete dependency lock exists. |
| Brand site, product site and runnable dashboard | Designed — assets absent | Author static pages and a separate replay dashboard; no deployable website exists in this tree. |
| Contract and credit optimisation product | Designed — direction only | Founder-supplied roadmap direction; no implementation or product specification in this repository. |

## Validation baseline

Python **3.12.10**, existing development dependencies. The Stop-hook oracle, `bash harness/check-suite.sh`, stops at the compression failure. A complete diagnostic run with provider keys disabled **inside the Python process** collected **32 tests: 22 passed, 4 failed, 4 skipped, 2 xfailed**.

| Failing test | Observed failure |
| --- | --- |
| `tests/acceptance/test_signed_score_axioms.py::test_vol_compression_returns_negative_score` | Expected negative score; returned `1.0`. |
| `tests/acceptance/test_signed_score_axioms.py::test_parametric_gate_failure_returns_degraded_certainty` | Expected certainty at most `0.5`; returned `0.9672`. |
| `tests/acceptance/test_signed_score_axioms.py::test_zero_deviation_returns_near_zero_score` | Expected magnitude below `0.2`; returned `0.2`. |
| `tests/architecture/test_code_hygiene.py::test_ruff_reports_zero_violations` | `app/registry.py`: `PLR2004` at line 47 and `PLR5501` at line 57. |

The four skips are three macro anchors awaiting migration and one credential-dependent FRED integration test. The two strict expected failures cover calm/normal VIX regimes. Fixing signed scores may turn them into unexpected passes; reconcile their assertions against the SRS rather than suppressing failures or retuning bands to current output.

Separate checks: **6/6 import contracts pass**; classifier OpenAPI passes; engine OpenAPI fails with `ExtraParametersError: Required list has not defined properties: ['baseline_value']` in `CrossAssetFlowPayload`. This contract failure is outside the current pytest gate.

## Design limits that affect the demo

- **A severity score is not a forecast.** CLS-006 defines `scenario_iv = observed_iv × (1 + composite × sensitivity)`. Its gap therefore equals `observed_iv × composite × sensitivity`; there is no independently estimated fair value or demonstrated predictive edge. Show it as an **experimental scenario**, with multiplier and equation visible.
- **CLS-002 can violate its stated range.** One category with conviction `1`, weight `1` and dropout factor `0.5` gives composite `2`, despite the specified `[-1, 1]` bound. The first demo should use explicitly healthy sources; document this gap before generalizing the engine.
- **Negative scenarios need a domain bound.** Composite `-1` and sensitivity `1.5` yield negative implied volatility. Constrain demo inputs so `1 + composite × sensitivity >= 0`; calibrated sensitivity remains future research.
- **Replay snapshots are not an event stream.** Existing market fixtures contain prior levels and an event timestamp, not a timestamp for every level. Plot history by observation index; reset before each independent snapshot. Do not invent intraday timing, corroboration or post-event returns.
- **Validation is incomplete.** Current tests do not establish trading usefulness. [Calibration and outcome-oracle gaps](../apps/classification/LIMITATIONS.md) remain separate work even after CI becomes green. The implemented temporal certainty is an age/cadence measure, not evidence that markets have not priced an event.

## Shortest path to an end-to-end demonstration

Scope: **historical VIX/OVX snapshot → real HTTP classifier → experimental volatility scenario → local dashboard**. A small Python demo adapter remains separate from the six planned .NET iterations. It must identify any simplified aggregation explicitly; a single classified event must not masquerade as corroborated CLS-002 engine output.

Estimated engineering effort from the audit baseline: A/B cover classifier and CI repairs; C–G cover the replay dashboard.

| Step | Work and completion criteria | Hours |
| --- | --- | ---: |
| A | Repair signed/zero severity and parametric-gate failures, horizon/sufficiency handling and readiness; reconcile strict xfails against requirements; retain independent checks. | 5–9 |
| B | Repair invalid engine schema; run both spec validators, all fitness tools and pytest from CI; prevent silent missing-tool passes; fix shell portability and report intentional skips. | 4–6 |
| C | Explicit offline snapshot loader/reset, source provenance, serialized HTTP replay and no provider credentials required. Use existing sourced market history. | 2–3 |
| D | Separate demo adapter deriving transparent signed conviction and scenario/gap; parameter validation and unavailable/degraded states. | 2 |
| E | Three-view dashboard in plain HTML/CSS/JavaScript with native SVG, served by FastAPI; selection, replay/reset and visible status. No frontend build system. | 4–5 |
| F | Hand-calculated boundary tests, HTTP/browser smoke checks and replay repeatability/error checks. | 2–3 |
| G | One-command local startup, instructions and contingency. | 3 |
| **Total from this checkout** | **Working local demonstration, including existing blockers** | **22–31** |
| **Incremental dashboard after CI repairs** | **C–G** | **13–16** |

Three views: **event tape** (source, symbol, event time, replay status); **classification** (signed severity, certainty dimensions, history and reasoning); **volatility scenario** (observed index, scenario index, signed gap and parameters). The demo uses `conviction = severity × certainty` and `scenario_iv = observed_iv × (1 + conviction × k)`, with visible, uncalibrated `0 <= k <= 1`. Label conviction as single-event output: the same market observation drives classification and observed IV. Completion requires an actual HTTP classification response, repeatable replay results and explicit error/degraded handling. No fabricated headline classification, orders, returns or independent-forecast claims.

Data source: existing FRED-sourced VIX/OVX fixtures for local replay; [Cboe's historical-data page](https://www.cboe.com/tradable-products/vix/vix-historical-data) provides daily index history for future refresh. [FRED identifies VIX data as copyrighted](https://fred.stlouisfed.org/series/VIXCLS); a source-code licence must not imply ownership or relicensing of provider data. Keep public site copy separate from downloadable raw datasets until reuse terms are established.

</details>
