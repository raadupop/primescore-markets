# Classifier Architecture — Three Questions (Chief Architect Review)

**Date:** 2026-04-26
**Skill invoked:** `/chief-architect`
**Context:** Follow-up to ADR-0001 / ADR-0002 reconciliation work and the
"per-class vs per-symbol" semantic untangling. Operator surfaced three
architectural concerns about the Python classifier.

---

## Question

> Three questions:
> 1. Why a registry and not a database?
> 2. Why this decentralization with a ping-pong between a Python app and a .NET ingestion job?
> 3. Is this classifier a truly stateless app? I'm having some concerns on scaling.

---

## Answer

### Conclusion up front

1. **Registry vs. database:** A YAML registry is correct *for now*. A database
   is correct *if and when* parameters change at runtime, multi-writer
   coordination matters, or audit-of-who-changed-what becomes operational
   concern. None of those apply to a single-operator research vehicle. Don't
   migrate prematurely.
2. **Python ↔ .NET split:** It's not decentralization — it's a deliberate
   language boundary justified by a single concern (Python's statistics
   ecosystem). The "ping-pong" is an HTTP call per event, not a coordination
   problem. The architectural cost is real but bounded.
3. **"Stateless" is wrong as currently described.** The classifier holds
   in-memory rolling windows. It is **state-bearing in process memory**. This
   is the scaling concern you're sensing. It's the most important of the three
   questions.

---

### 1. Why a registry, not a database?

#### The forces

A database (SQLite, Postgres, even DynamoDB) buys you:
- Concurrent writes with ACID guarantees
- Query flexibility (ad-hoc filters, joins, aggregations)
- Hot reload of changes without restart
- Audit log of mutations
- Centralized access from multiple services

A YAML/JSON registry file buys you:
- Diffable, reviewable, git-versioned changes
- Zero infrastructure (no DB to provision, secure, back up, monitor)
- Trivial bootstrap (read once at process start)
- Atomic deploy semantics — the file in this commit *is* the configuration
- Cross-language readability with zero schema-translation layer

#### Decision dimensions

| Dimension | Registry (YAML) | Database |
|---|---|---|
| Reversibility | Trivial — `git revert` | Requires migration |
| Blast radius if wrong | One commit | Operational incident |
| Change frequency | Quarterly to yearly | Hourly to daily |
| Writers | One human | Many processes |
| Audit need | Git history | Schema-enforced log |
| CoBW | Low | High (state corruption) |

#### Verdict

PrimeScore AI has **one writer** (Radu), parameter changes that are **rare and
reviewed** (each one is a `/trader` decision), and **two readers** (.NET
ingestion, Python classifier) that benefit from reading the *same artifact*
rather than two database client libraries.

A database here is solving problems PrimeScore AI doesn't have while creating problems
it doesn't need (DB lifecycle, connection management, schema migrations,
two-language ORM friction).

The classic indicator that a registry has outgrown YAML:
- Parameters are mutated by automation, not humans
- Multiple humans edit concurrently and need conflict resolution
- Real-time hot reload is required (sub-minute parameter propagation)
- Auditing needs to answer "who changed `N` for `equity_vol_index` on Tuesday
  at 14:32?" — not just "what was the diff?"

None of these apply yet. When two of them apply, revisit.

**Anti-pattern check:** putting this in a database now would be
**resume-driven architecture** — adding infrastructure for the architecture
diagram, not for the operational need.

---

### 2. Why the Python ↔ .NET split?

#### The honest answer

The split exists because **ECDF, rolling-window statistics, and future
calibration work are easier in Python** (numpy, scipy, pandas, statsmodels).
The .NET ecosystem can do all of this, but with more friction and fewer
reference implementations. That's the *only* reason the boundary exists.

It is not:
- A microservices decision
- A team-boundary decision (one operator, both sides)
- A scaling decision (the classifier doesn't need independent scaling — see Q3)
- A polyglot-for-its-own-sake decision

#### The cost of the boundary

Real costs you're paying:
- HTTP serialization overhead per event (~ms-scale, irrelevant for this domain)
- Two deploy targets, two test pipelines, two dependency surfaces
- Two-language refactoring tax — schema changes touch both sides
- Contract drift risk — `PrimeScore-API-v1.yaml` and `apps/classification/doc/openapi.yaml` must stay coherent
- A second AGENTS.md, a second set of conventions, a second mental context-switch

These are non-trivial. For a single-operator project they're paid in
cognitive overhead, not infrastructure cost.

#### The benefit

- The classifier becomes **constant infrastructure across six .NET
  iterations** (ACX-001, ACX-002). That's the whole research design — the
  .NET side is the variable, the classifier is the control. Putting the
  classifier in .NET would make it part of the variable.
- Python statistical primitives (ECDF, KDE, rolling quantiles) are
  first-class. Implementing these correctly in .NET is doable but distracts
  from the architecture-measurement objective.

#### "Ping-pong" framing

This is the part to push back on. There is no ping-pong. The flow is:

```
.NET ingestion ──POST /classify──► Python classifier ──response──► .NET ingestion
                                          │
                                          └── (no callback, no polling, no second hop)
```

One request, one response, synchronous. That's the simplest possible service
interaction. If it felt like ping-pong, it's because the *registry* is read
by both sides, which creates the illusion of two-way coupling. It's actually
one-way: both sides read the same file at startup. They don't talk *about*
the registry.

#### Verdict

The split is justified for **this specific reason** (Python's statistics
ecosystem + classifier-as-control-variable across iterations) and would not
be justified otherwise. Document the justification in an ADR if it isn't
already; this is the kind of decision that gets questioned every six months
unless the rationale is written down.

The boundary is acceptable. It is not free. Be honest about the cognitive
cost in the AGENTS.md.

---

### 3. Is the classifier truly stateless? — *the real question*

#### Short answer: **No.**

Look at `state.py`:

```python
state = AppState()  # singleton, in-process

market_data_history: dict[str, RollingWindow]      # keyed by symbol
macro_surprise_histories: dict[str, RollingWindow] # keyed by indicator
cross_asset_windows: dict[str, RollingWindow]      # keyed by ticker
```

This is **process-local mutable state** that accumulates with every request.
The classifier is stateless at the **HTTP-request boundary** (no session,
no cookie, no auth context per request) but **stateful in process memory**
(rolling windows persist across requests).

Calling it "stateless" is a category error and exactly the kind of thing
that bites you when scaling.

#### Scaling failure modes

If you spin up two replicas of the classifier behind a load balancer:

1. **Split-brain rolling windows.** Replica A sees half the VIX prints,
   Replica B sees the other half. Each has a degraded ECDF reference
   distribution. Severity scores diverge across replicas for the same input.
2. **Cold-start asymmetry.** A new replica spawned during a vol shock has an
   empty window for the first `N` events. Its severity scores are unreliable
   until backfilled. The load balancer has no way to know which replicas are
   "warm" for which symbols.
3. **Bootstrap thundering herd.** Each replica independently pulls history
   from FRED / Twelve Data on startup. N replicas = N×provider rate-limit
   pressure.
4. **Window-version drift.** A replica restarted yesterday at 09:00 has
   different window contents from a replica restarted today at 09:00. Same
   input, different outputs.
5. **Loss on restart.** Killing the process loses all rolling-window state.
   Bootstrap re-fetches what it can — but if a recent observation didn't make
   it back to the source API yet (vendor lag), it's gone.

#### What "truly stateless" would require

To be horizontally scalable in the strict sense:

- Move rolling-window state to a shared store (Redis, DynamoDB, Postgres).
  Each request reads-modifies-writes the symbol's window with optimistic
  concurrency or a per-symbol lock.
- Or: make every request **carry its own context** (the .NET side sends not
  just the new observation but the relevant window). Pure function in, score
  out. No process memory.
- Or: pin a symbol to a specific replica (sticky routing on `symbol`) so
  each window has one writer. This is "stateful service with sharding," not
  stateless.

#### What you actually need

Probably none of the above, *because the workload doesn't require horizontal
scaling*. Question to ask:

> What is the request rate the classifier needs to sustain, and is one
> process insufficient?

For PrimeScore AI:
- Macro releases: minutes to hours apart
- Market-data ticks: even at every WebSocket update, single-process Python
  handles thousands per second
- Cross-asset: daily

A single-process classifier is easily sufficient for the throughput. The
scaling concern isn't load — it's **availability** (process crash = lost
windows) and **deployability** (restart = bootstrap window).

#### Recommendation — pragmatic, sized to PrimeScore AI

**Phase 1 (now): single instance, accept the limitation.** Document
explicitly:
- The classifier is not horizontally scalable.
- Restart loses in-memory state and triggers re-bootstrap.
- This is acceptable given the workload (low QPS, single-operator).

**Phase 2 (only if availability becomes a concern):** snapshot rolling
windows to disk on a timer. On startup, load snapshot first, then top up via
bootstrap. Reduces cold-start latency without introducing distributed state.

**Phase 3 (only if multi-instance is genuinely needed):** Redis-backed
state, sticky routing, or per-symbol consistent hashing. This is a real
architectural change and warrants its own ADR.

Do not skip to Phase 3. Most "scaling concerns" turn out to be Phase 1
problems wearing Phase 3 clothing.

#### What goes in the SRS / AGENTS.md

This needs to be **named explicitly**. Right now the architectural reality
(state-bearing in-process) doesn't match the casual framing ("stateless
service"). That gap is exactly where bad scaling decisions get made.

Add to `apps/classification/AGENTS.md` or `LIMITATIONS.md`:

> **The classifier is process-local stateful.** Rolling windows live in
> `state.AppState` and accumulate per request. The HTTP layer is stateless
> (no auth/session) but the application is not. Horizontal scaling requires
> either external state or sharding by symbol; neither is implemented.
> Single-instance deployment is the supported topology.

That sentence is worth more than any future debugging session.

---

## Trade-offs across all three answers

What you're sacrificing by accepting these positions:

- **Registry-not-database:** future hot-reload friction if requirements
  change. Acceptable; reversible.
- **Python/.NET split:** ongoing cognitive tax on cross-cutting changes.
  Acceptable; it pays for the research design.
- **Single-instance classifier:** no HA, no horizontal scaling. Acceptable
  for now; revisit if iteration 6 (service extraction) needs it.

## Dissenting view

The strongest case against this recommendation: if PrimeScore AI's research goal
eventually requires running iteration-3 and iteration-5 against the *same*
live classifier instance for comparative measurement, single-instance state
becomes a single point of failure for the entire experiment. A 30-minute
classifier outage blanks both iterations' data. That's an
experimental-validity risk, not just an availability risk.

If that scenario is plausible within the project's lifetime, Phase 2 (disk
snapshots) becomes mandatory, not optional. Worth a paragraph in the SRS
risk section.

## Next actions

1. Write the "process-local stateful" disclaimer into
   `apps/classification/AGENTS.md` or `LIMITATIONS.md`. **Today.**
2. Add an ADR (or extend ADR-0002) explicitly stating: registry not
   database, Python boundary justified by ecosystem + control-variable role,
   single-instance topology with documented limitations. **This week.**
3. Defer Phase 2 (disk snapshots) until the first time a restart actually
   hurts. Don't pre-build it.
