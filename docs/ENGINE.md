# Run PrimeScore Markets

.NET 10 and Python 3.12 run two local processes: the engine at
`http://127.0.0.1:5080` and the classifier at `http://127.0.0.1:8000`.
Commands below use PowerShell from the repository root.

## Install and sign in

```powershell
python -m venv apps/classification/.venv
& apps/classification/.venv/Scripts/python.exe -m pip install -r apps/classification/requirements-dev.txt
dotnet restore apps/engine/PrimeScore.Engine.sln
dotnet run --project apps/engine/src/Host/PrimeScore.Engine.Host -- set-operator-password --name operator
```

The last command prompts twice without echoing the password. It stores a salted password
hash in .NET user-secrets outside the checkout and preserves other secrets. Run it again to
change the password. No initial or fallback password exists.

Start the engine, classifier and Markets presentation website from one terminal:

```powershell
.\scripts\start-markets.ps1
```

The [launcher](../scripts/start-markets.ps1) checks dependencies and ports, builds the engine,
and starts the classifier and Markets website with the existing virtual environment. The engine
runs from a copy of its build output in `apps/engine/var/local-run/engine/`, so a running
dashboard does not block the next build or the gate.
Ctrl+C stops all three services. Logs go to `apps/engine/var/local-run/`. Use `-CheckOnly` to check prerequisites
without starting services. Configure the operator password above before signing in.

To run them separately, start the classifier in one terminal:

```powershell
$env:BOOTSTRAP_MODE = 'disabled'
$env:PRIMESCORE_REGISTRY_PATH = (Resolve-Path infra/registry.yaml).Path
& apps/classification/.venv/Scripts/python.exe -m uvicorn main:app --app-dir apps/classification --host 127.0.0.1 --port 8000
```

Start the engine in another:

```powershell
dotnet run --project apps/engine/src/Host/PrimeScore.Engine.Host --launch-profile engine
```

Open `http://127.0.0.1:5080/login`. Development loads user-secrets; other environments read
`Auth__Operator__Name` and `Auth__Operator__PasswordHash` from the environment. Outside
Development and Testing, session cookies require HTTPS. Terminate TLS before exposing the
service; the launch profile binds only to loopback. No deployment is configured by these commands.

Start with **How it works** (`/guide`), then choose a market on **Market overview**.
Follow **Read this analysis** to inspect its conditions and supporting observations.
**Conditions met** is a research result: it opens no position and sends no order.
**Data & health** shows input coverage; **Historical replay** tests rules against recorded events.

The classifier's health can be `503 not_ready` with bootstrap disabled. The engine supplies
each classification request's reference history, so its calls do not require process-local
classifier windows. Stop both processes with Ctrl+C before rebuilding on Windows.

## Presentation websites

The main launcher includes Markets at <http://127.0.0.1:8091>; its dashboard links open
the engine at port 5080. To preview only the Markets presentation website, use
`.\scripts\start-websites.ps1` instead; do not run both launchers together.
The brand site belongs to the sibling [PrimeScore umbrella](../../primescore/README.md)
at `D:\Work\primescore`. Its launcher can also start Markets from this checkout with
`-MarketsRoot D:\Work\primescore-markets`.

## Load observations

Source adapters are configured under `Sources:<Name>` (user-secrets, or environment variables
with `__` separators) and stay off unless `Enabled` is true. **Data & health** lists every
adapter with its schedule, last run, flags and a **Run now** button.

**Cboe index history** needs no key. `Sources__Cboe__Enabled=true` polls each file every
`PollInterval` (15 minutes) from `WindowStartNewYork` (18:00) to `WindowEndNewYork` (08:00, next
day) New York time and records each new close, stamped 16:15 New York (SPX 16:00) on its data date.
`Symbols` replaces the default list (VIX, VIX9D, VIX3M, VIX6M, VVIX, OVX, GVZ, VXN, RVX, SPX);
`RunOnStartup=true` pulls at start. Other keys: `BaseUrl`, `OverlapRows`, `RetryDelaySeconds`,
`TimeoutSeconds`. An invalid value keeps the adapter disabled and the page names the key. The
first run records the full history, about 55,000 signals, and classification takes minutes.
Do not enable it on the live ledger until the live-start dates are settled (TODO-016 in
[the registry](../doc/todo/registry.yaml); [limits](../apps/engine/LIMITATIONS.md#data-coverage-cboe)).

**FRED** is a read-only cross-check and records nothing ([ADR-0009](../doc/adr/0009-cboe-index-history-and-fred-cross-check.md)).
Set `Sources:Fred:ApiKey` with `dotnet user-secrets set` (or `Sources__Fred__ApiKey`) and
`Sources:Fred:Enabled=true`. Daily at `DailyRunNewYork` (08:15 New York) it compares the last
`CompareDays` (5) Cboe closes per index with FRED's values and flags a difference above
max(`AbsoluteTolerance` 0.005, `RelativeTolerance` 0.0001 × close), or a FRED date Cboe lacks,
under **Cross-check findings**. `ExcludedSymbols` skips indices, for example VVIX, whose FRED
mapping does not exist. The former `Fred:ApiKey` is no longer read; the page says to move it.

Adapter rows dated before `Classification:AdapterHistoryFrom` (default 2011-01-01) are recorded
but not assessed ([ADR-0010](../doc/adr/0010-classification-of-adapter-context-series.md)); an
earlier date needs a research registration first.

Operator steps after this upgrade: move the FRED key and enable the cross-check; delete the old
FRED response cache in `apps/engine/var/cache/fred`; enable `Sources:Cboe` once TODO-016 is settled.

Read-only verbs print stored data as JSON; `--database <absolute path>` selects another database
(the launch profile starts in the project folder, so a relative path is refused if it names no file):

```powershell
dotnet run --project apps/engine/src/Host/PrimeScore.Engine.Host --no-build -- sources
dotnet run --project apps/engine/src/Host/PrimeScore.Engine.Host --no-build -- signals --instrument VIX --source-prefix cboe: --take 5
```

`sources` prints each adapter's last stored run (error, counts, flags, note) and coverage;
enabled state and next run belong to the running engine and appear only on the page. `signals`
filters by `--instrument`, `--source-prefix` and `--provider` (default `--take 100`) and prints
provenance, including the file SHA-256 and the reconstructed marker.

Cboe's terms allow personal non-commercial use only; storing, deriving and publishing need Cboe's
written consent ([ADR-0015](../doc/adr/0015-cboe-data-personal-research-use-until-consent.md),
[licence note](../doc/research/data-licence-cboe-2026-10-02.md)). Until then the engine is the
operator's research tool: issue READ and ADMIN tokens to the operator only, keep the FRED
cross-check off, and publish nothing derived from Cboe files. No public surface serves raw series.

No key is needed to inspect existing data or to ingest through the API.
Macro consensus CSVs require sourced entries in [data/consensus](../apps/engine/data/consensus/README.md).
Missing consensus stays missing. [Provider and timing limits](../apps/engine/LIMITATIONS.md)
describe unavailable series, stale data and release timestamps.

### Catalyst calendars

Six calendar sources record scheduled events with canonical ids such as `FOMC-2026-10-28`
([ADR-0011](../doc/adr/0011-catalyst-calendar-ids-vintages-and-sources.md)). Each runs once per
weekday at `DailyRunNewYork` (06:00 New York) and is off unless `Enabled`:

| Section | Family | Extra keys |
| --- | --- | --- |
| `Sources:FedCalendar` | FOMC | `HistoryFromYear` (2013; 0 = none) |
| `Sources:BlsCalendar` | CPI, NFP | `HistoryFromYear` (2013); needs a contact `UserAgent` |
| `Sources:BeaCalendar` | GDP, PCE | |
| `Sources:EiaCalendar` | WPSR | |
| `Sources:ClaimsCalendar` | CLAIMS (derived by rule, no fetch) | `HorizonDays` (63), `LookbackWeeks` (60) |
| `Sources:OpecCalendar` | OPEC | `File` (default `apps/engine/data/catalysts/opec.csv`) |

Each also takes `RunOnStartup`; the four page sources take `BaseUrl`, `UserAgent`,
`RetryDelaySeconds` (5) and `TimeoutSeconds` (60). BLS refuses robots without contact details, so `BlsCalendar` stays disabled
until `UserAgent` contains an e-mail address or URL; set it in user-secrets or
`Sources__BlsCalendar__UserAgent`, never in a committed file, for example
`PrimeScoreMarkets/0.1 (calendar reader; contact: <your address>)`. The first Fed run reads 8
historical pages (2013–2020) and the first BLS run 13 yearly archives (2013–2025); after that a
page is read again only until one of its rows is recorded. All six together record about 700
ledger entries on first enablement.

A changed schedule appends a vintage; the id never changes. Precedence is listing > archive >
curated > rule: a lower source that disagrees, a catalyst that disappears from its source and a
date replaced by TBD are not recorded but appear as run flags on **Data & health**; decide them
by hand. OPEC dates are typed into the [curated CSV](../apps/engine/data/catalysts/README.md);
confirm the publisher's terms first. The same format back-fills any family, with the engine stopped:

```powershell
dotnet run --project apps/engine/src/Host/PrimeScore.Engine.Host --no-build -- import-catalysts --file <csv>
```

It prints `scheduled`, `rescheduled`, `unchanged`, `flags` and `errors` as JSON; exit 1 means the
file was rejected (every error names its line; nothing recorded). `--force` lets the file replace
a listing or archive date and is recorded as forced; `--database <path>` selects another database.

**Catalyst calendar** (`/catalysts`) lists the next 30 days with the 9-day/30-day ratio and its
weekday-matched baseline percentile and n ([ADR-0012](../doc/adr/0012-pre-catalyst-ratio-against-weekday-matched-placebo-days.md));
the ratio needs `Sources:Cboe`. `GET /api/catalysts?from=&to=&family=` (default: now to 30 days
later) and `GET /api/catalysts/{catalyst_id}` (every vintage) serve the same data to READ tokens.

Operator steps: set the BLS contact `UserAgent`; enable `FedCalendar`, `BlsCalendar`,
`BeaCalendar`, `EiaCalendar` and `ClaimsCalendar`; curate `opec.csv`, then enable `OpecCalendar`;
import earlier history as needed (TODO-019). [Limits](../apps/engine/LIMITATIONS.md#catalyst-calendars).

### Event record

**Event record** (`/event-record?family=CPI`, `GET /api/analytics/catalyst-outcomes`) measures each past
event: the S&P 500 move VIX9D priced for the next 9 days against the move that happened, the
event-day move and the VIX9D change (OVX for WPSR and OPEC) ([ADR-0013](../doc/adr/0013-event-record-priced-against-actual-move.md)).
It needs the Cboe files for VIX9D, VIX and SPX (OVX for oil) and the family's calendar; CPI and NFP
need the BLS contact above. Figures are estimates from indices, not option prices.

**Your trade** (`/your-trade`, `POST /api/analytics/position-scenarios`) values an S&P 500 option position you
type in (up to four legs, one expiry) and replays every past release before its expiry on it
([ADR-0014](../doc/adr/0014-position-event-scenarios.md)). It also needs the VIX3M and VIX6M files.
Nothing is saved; the address bar holds the position, for example `?u=SPX&exp=2026-10-30&legs=1C7675,1P7675`.

**Daily brief** (`/brief?market=equity`) puts the state, the cross-asset grid, the next 10 trading days of releases,
their record, what just passed and new calendar entries on one page ([ADR-0016](../doc/adr/0016-daily-brief-composition.md)).
It has no API and sends nothing: Cboe-derived figures are for your own research (ADR-0015).

## API access

```powershell
dotnet run --project apps/engine/src/Host/PrimeScore.Engine.Host -- new-api-token --role ADMIN
```

This prints a random token once and its SHA-256 hash. Store the token outside the checkout.
Configure `Auth:ApiTokens:0:Name`, `Auth:ApiTokens:0:Role` and `Auth:ApiTokens:0:Sha256` in
user-secrets, or the equivalent environment keys with `__` separators. Use a separate index
for each token. `READ` permits reads; `ADMIN` also permits ingestion, configuration and replay.
Send `Authorization: Bearer <token>` to `/api/*`. Browser cookies do not authenticate that API.
Restart after changing token configuration.

The [OpenAPI contract](../doc/PrimeScore-API-v1.yaml) defines request bodies and routes.
Positions, exits, risk and execution controls return `501`; no order is placed.

## Replay and configuration

**Replay** accepts a date range or one of the ten SRS events. Overrides are temporary;
the run records the effective settings and input ledger boundary. **Evaluate ten events**
stores the baseline runs and displays matches, misses and missing inputs.

```powershell
dotnet run --project apps/engine/src/Host/PrimeScore.Engine.Host --no-build -- evaluate-validation
```

The command writes `apps/engine/var/validation-report.md` against the configured database.
It does not fetch market data or require a running classifier. Results evaluate the equity/VIX
proxy and the SRS timing targets, not returns or predictive performance.

**Model settings → Advanced configuration → Edit parameters** accepts the displayed configuration as JSON and a reason.
Every accepted change records the operator, version and diff. If another edit was saved after
the page loaded, reload before saving. Values tuned against the validation set must be labelled
`in-sample`. Temporary replay overrides use the same field names; see [replay limits](../apps/engine/LIMITATIONS.md#replay-m5).

## Persistence and recovery

The database defaults to `apps/engine/var/engine.db`; `Engine__DatabasePath` overrides it.
Schema migrations run at startup. Stop the engine before copying the database together with
any `-wal` and `-shm` files, or use SQLite's online backup API. Restore the complete backup
while the engine is stopped; never replace one file from a different snapshot.

```powershell
dotnet run --project apps/engine/src/Host/PrimeScore.Engine.Host --no-build -- verify-ledger
```

Exit `0` means the chain verifies, `1` means a mismatch, and `2` means the command failed.
Keep a backup outside the running database's directory. Hash links detect changes relative
to the stored checkpoint; they do not protect against someone who can rewrite both the
database and checkpoint. No automatic restore or retention service is provided.

Operator sessions expire after eight hours. Login and logout require a request-verification
token; login permits ten attempts per IP per minute. Changing the operator password invalidates
existing cookies after restart. Open interactive sessions recheck their credentials each minute;
each mutation also checks the current session. Preserve the service account's ASP.NET data
protection keys if cookies must survive host replacement.

## Verify changes

From Git Bash on Windows, or Bash on Linux:

```sh
bash harness/check-suite.sh
```

The [gate contract](../harness/ORACLE.md) defines all stages and intentional skips. Live provider
access is separate from deterministic acceptance tests. The Python [demo](DEMO.md) remains
available independently; it does not use the engine's database.
