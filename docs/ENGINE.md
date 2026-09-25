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

Start the classifier in one terminal:

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

The classifier's health can be `503 not_ready` with bootstrap disabled. The engine supplies
each classification request's reference history, so its calls do not require process-local
classifier windows. Stop both processes with Ctrl+C before rebuilding on Windows.

## Load observations

Configure `Fred:ApiKey` in the Host's user-secrets with `dotnet user-secrets set`, or supply
`Fred__ApiKey` in the environment. Restart, then use **Sources and health → Pull now**.
The adapter backfills from 2011 on an empty database and subsequently pulls daily at
13:30 UTC. `Fred__DailyRunUtc` changes that schedule; `Fred__Enabled=false` disables pulls.

No FRED key is needed to inspect existing data or to ingest through the API.
Macro consensus CSVs require sourced entries in [data/consensus](../apps/engine/data/consensus/README.md).
Missing consensus stays missing. [Provider and timing limits](../apps/engine/LIMITATIONS.md)
describe unavailable series, stale data and release timestamps.

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

**Configuration → Edit parameters** accepts the displayed configuration as JSON and a reason.
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
