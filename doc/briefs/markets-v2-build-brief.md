# PrimeScore Markets v2 — build brief

You are a principal .NET engineer. Build what `doc/briefs/markets-product-study.md` needs, in
two parts. **Part A** is the prototype that lets the study's tests run; start it now. **Part B**
is the paid product; do not start any of it until the founder confirms that tests T4 and T5
passed. Business, pricing and marketing are decided in the study and are not your concern. Paths
are relative to the repository root. Keep `bash harness/check-suite.sh` green, record decisions
as ADRs under `doc/adr/`, route deferred work to `doc/todo/registry.yaml`.

---

## 1. Ground truth

| Path | Why |
| --- | --- |
| `doc/briefs/markets-product-study.md` §4 and §5 | The tests and the prototype table you are building |
| `doc/briefs/markets-v1-build-brief.md` §4 to §6, §10 to §12 | The architecture you extend: modular monolith, ledger, runtime model, UI, contract-first testing |
| `doc/adr/0003` to `0007` | Ledger, composites, decisions, replay, presentation rules |
| `apps/engine/LIMITATIONS.md` | Timing (FRED publishes next morning), coverage (1,260 prior closes needed), replay semantics |
| `doc/PrimeScore-API-v1.yaml` | Contract; extend additively |
| `infra/registry.yaml` | Instruments and classes |
| `doc/concepts/statistics.md`, `.claude/skills/statistician/` | The statistics and the reviewer for the edge test |
| `sites/markets/` | The static product site where the record page and sign-up live |

## 2. Invariants

- Every figure a reader sees is a ledger entry or derived from one by a recorded computation,
  reproducible by a command.
- Point-in-time: forward outcomes use only observations recorded after the decision's time.
- Missing data stays missing. No fill, no smoothing.
- API names, stored values and formulas do not change; presentation explains them.
- Secrets never enter the repository or logs.
- Results are published whatever they show. No figure is removed because it disappoints.

---

## Part A: prototype for the tests (start now)

### A1. Full-history decisions (test T1, first task)

- Backfill FRED history for VIX and OVX and every registry series they depend on; run
  classification and decisions for every trading day for which 1,260 prior closes exist.
- Produce a coverage report: first and last decision date per market, count of trading days,
  count of days without a decision and why.
- Done when every trading day in range has a decision or a recorded reason.

### A2. Forward outcomes and edge statistics (test T1)

**Implemented 28 September 2026** as `GET /analytics/outcomes`, the Outcomes page and the
`outcomes-report` command (ADR-0008). Outcomes are computed on read rather than stored as ledger
entries, so they cannot drift from the ledger.


- Analytics module: for every decision, the reference index change at 1, 5, 10 and 21 NYSE
  trading days after the observation time, stored as ledger entries linked to the decision.
- Baselines: all trading days; days in the same regime state (A3).
- Statistics per market and horizon: sample sizes; median and interquartile range of absolute
  change for signal days and each baseline; hit rate as sign of change matching the combined
  score's sign; bootstrap 95 percent interval for the hit rate and for the ratio of medians.
- Invoke the `/statistician` skill on the design before running; record its findings in the ADR.
- `GET /analytics/outcomes` in the contract; a CLI command prints the same figures from the ledger.
- Done when the command and the endpoint agree and the ADR records the design review.

### A3. Regime states

**Implemented 28 September 2026** as the state gate and state labels (ADR-0008).


- Versioned configuration mapping severity and confidence to Compressed, Normal, Stretched,
  Extreme, with a one-line reason template per state. Shown wherever severity is shown. Stored
  with each decision so history is reproducible after a mapping change.

### A4. Public record page

- A generator that reads the ledger and writes static HTML and JSON: per market, the A2 figures
  with sample sizes, the period covered, the exact query used, the last update time, and the
  worked example of 5 February 2018. Published into `sites/markets/` on every run.
- Every figure shows its sample size next to it. The page states that no returns are measured.
- Done when the site tests in `tests/sites/` cover the page and it regenerates daily.

### A5. Daily brief

- After the FRED pull completes, generate one brief per market: state, score, change since the
  previous close, decision, forward-outcome statistics for this state, next known catalyst if a
  calendar exists, link to the evidence page, disclosure block from configuration.
- Record the brief's text hash as a ledger entry before sending. Record the send time and the
  gap to 09:30 New York; expose both on the health page and in the record JSON.
- Done when a brief is generated for every trading day for two consecutive weeks with no
  missed day, and each brief's hash is verifiable.

### A6. Delivery

- Telegram: one broadcast channel through a bot; send recorded with message id.
- Email: a transactional provider (SES or the provider the founder names); one list; send
  recorded with provider id. Idempotent on (brief hash, channel).
- A public explanation page: how a reader checks a brief's hash against the record.
- Done when both channels deliver the same brief and the ledger shows both sends.

### A7. Sign-up and metrics

- Email capture on the landing page writing to the list provider; export for the pilot.
- A daily metrics entry in the ledger: sign-ups, channel members, brief opens (from the email
  provider), send time gap. A command prints the T3 and T5 figures.
- No third-party trackers beyond the email provider's open pixel.

### A8. Pilot setup alert

- One named setup in configuration: regime change or **Conditions met**. The alert carries a
  checklist, the A2 statistics filtered to that state, and a long-straddle template illustrated
  for a configurable risk budget using the last close and an illustrative premium labelled as
  such. Sent to a private channel and list for pilot members, added by hand.

### A9. Operation

- The engine runs daily on one machine (the founder's or a small VM); a scheduled task runs pull,
  classify, decide, outcomes, page generation, brief, send. Failure of any step sends the operator
  a message and is recorded. Runbook in `docs/ENGINE.md`.
- Done when the run has completed unattended for ten consecutive trading days.

Part A deliverables: ADRs for outcomes design, regime mapping, delivery; contract extension for
outcomes; `docs/STATUS.md` and `LIMITATIONS.md` updated; the runbook.

---

## Part B: paid product (only after T4 and T5 pass)

Each stage ends runnable and demonstrable, with the gate green and its ADRs written.

### B1. Hosted, multi-tenant, billing

- `Accounts` module: account, membership, preferences, watchlist, risk budget; ASP.NET Core
  Identity; operator stays ADMIN. Model data shared and read-only for customers.
- Storage ADR: whether the ledger store moves to PostgreSQL; append-only and hash chain preserved.
- AWS eu-central-1, Fargate and RDS, HTTPS at `markets.primescore.ai`, Secrets Manager, tested
  backup and restore. Reuse the cost model from 27 September 2026; ask the founder for it.
- Stripe Checkout, Customer Portal, Tax; webhooks update membership; entitlements enforced
  server-side.
- Today screen replaces the overview after sign-in: per watched instrument, state, score, reason,
  change, active setups, next catalysts, evidence link.
- Done when a stranger signs up, pays, receives a brief and cancels with the operator absent.

### B2. Same-day data and instrument expansion

- `MarketDataVendor` adapter beside FRED; provider by ADR after the founder confirms budget and
  redistribution licence. Provenance records vendor, delay class, retrieval time.
- Additional Cboe volatility indices through `infra/registry.yaml`; no classifier code change
  for a symbol in an existing class.
- Licensed release calendar and consensus rows so macro classification switches on.
- Done when a new instrument appears end to end within one trading day of registry entry.

### B3. Setup catalogue

- `Setup` as versioned configuration: name, conditions, scope, catalyst linkage, template,
  checklist, statistics query. Three structure templates (POS-004 without prices), the third
  chosen by the founder.

### B4. Options pricing, priced ideas, exits, P&L record

- `OptionsChainVendor` adapter, live or delayed chains with Greeks plus history to 2018; provider
  by ADR.
- Positions: POS-001 convexity invariant, POS-002 sizing from the customer's risk budget,
  POS-004 priced structures; simulated fill at mid with configurable slippage; each idea a ledger
  entry with strikes, expiry, premiums, max loss, breakevens, entry window.
- Exits: EXT-001 to 004 including catalyst-relative exit and the Vega-crush gate; alerts through
  the B1 dispatcher. Risk: RSK-001 to 003 per account.
- Record page gains Backtest, Paper and Live columns, never merged; fit the sensitivity factor on
  2018 to 2023 and hold out 2024 onward; calibration status shown wherever a scenario appears.

### B5. API tier

- API keys with scopes and rate limits; point-in-time endpoints with `as_of` for scores,
  decisions, setups, outcomes, ideas; replay with custom parameters stored as the account's
  `ReplayRun`. OpenAPI updated; Python client example.

Part B cross-cutting: trigger-to-alert under 15 minutes measured and shown; correlation id from
ingestion to alert; tenancy test proving one account cannot read another's data; billing webhook
and alert idempotency tests; a licence ADR per vendor; vendor call counts on the health page.

---

## 3. Ask before acting

1. Email provider and Telegram bot ownership for A6.
2. The machine or VM that runs A9, and who receives failure messages.
3. Any vendor contract, its cost and its redistribution terms (Part B).
4. Storage move to PostgreSQL (Part B).
5. Anything that changes a formula, an API name or a stored value.
