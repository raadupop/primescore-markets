# Volatile Requirements and Multi-Artefact Friction — Strategic Analysis

**Format.** Chief-architect strategic recommendation (Situation / Complication / Recommendation / Trade-offs / Dissenting view / Next actions).

**Audience.** Solo operator building PrimeScore AI (volatility-exploitation engine) inside DeltaFeed (6-iteration research programme measuring AI agent performance across architecture paradigms under a stable external contract).

**Trigger.** Operator observation after SRS v2.3.2 revision work: *"SRS is pretty heavy. When changing appears like this there is a lot of friction — SRS, api contracts, code, tests, claude.md, files, etc. — which is hard to follow (I have limited tokens in my brain as well) and maintain. I'm not a domain expert so every concept you introduce, please add it to session notes."*

---

## 1. Situation

PrimeScore AI is a vol-trading product wrapped by DeltaFeed, a research instrument that holds a stable external contract across 6 iterations while varying the internal architecture. Requirements are **intentionally volatile**: SRS v2.3.1 Insight 7 names spec precision itself as an experimental variable. Changes don't signal design failure — they signal the experiment working as designed.

The artefact surface per requirement change touches, at minimum:

1. SRS (Word docx + PDF export)
2. OpenAPI contract (`PrimeScore-API-v1.yaml`)
3. ADRs (classifier layer, architecture layer)
4. LIMITATIONS.md / HARNESS.md (operational notes)
5. Code (Python classifier, .NET orchestrator)
6. Tests (acceptance fixtures, architecture fitness, contract tests)
7. CLAUDE.md / AGENTS.md (agent-facing conventions)
8. Session-notes (domain concepts introduced en route)

Human attention is the binding constraint. The operator is solo, not a domain expert on every vertical the SRS touches (options vol, statistics, distributed systems, event-study methodology).

---

## 2. Complication

**Manual cross-artefact propagation is O(n) per concept change, and there is no drift detector.**

The ECDF migration just closed a "self-validating-loop" bug class at the classifier layer — where tests and implementation co-evolved from the same wrong assumption. **The same bug class recurs one level up at the documentation layer:** SRS, ADR, LIMITATIONS, code, and tests can all be mutually consistent on an old term while a new requirement has moved past them. Nothing fails.

Concrete recent examples from this work:

- LIMITATIONS.md #5 was written with `CLS-004 fallback shape`; the fallback became CLS-009 mid-revision. Only caught by the operator reading it.
- SRS v2.3.2 draft originally added `SIG-001.1`; operator reviewed and rejected. Without the review, the registry would have been referenced as SIG-001.1 in ADR-0002 and LIMITATIONS.md while the SRS itself anchored it in §3 Definitions — silent drift.
- The `_TANH_SCALE` constant survives in code comments and test fixture names after the formula moved to ECDF. No automated check catches that.

The root cause is the same in every case: **content is duplicated across artefacts, and duplication is a linear function of artefact count**.

Secondary cost: **vocabulary overload**. Each SRS revision introduces terms (ECDF rank, IQR, indicator-class pooling, dispersion floor) that the operator must carry in working memory during the next change. Without an externalised reference, working memory becomes the bottleneck — at which point the cheap mistakes (stale CLS-004 references) become common.

---

## 3. Recommendation

Six patterns, ordered by impact-to-cost ratio. **Not all require doing now.** The first three are solo-affordable and deliver most of the value; the last three amortise over time and should be staged.

### 3.1. Single Source of Truth per concept class

Each concept lives in **exactly one file**. Other artefacts link rather than duplicate.

Example applied to CLS-001:

- **SRS §5.2 CLS-001** — authoritative formula. `severity = ecdf_rank(|deviation|) / N`.
- **ADR-0002** — *cites* SRS CLS-001; does not repeat the formula.
- **Code comment in [apps/classification/app/strategies/base.py](../../apps/classification/app/strategies/base.py)** — *cites* SRS CLS-001; does not repeat the formula.
- **Test fixture names** — reference CLS-001 by number.
- **[LIMITATIONS.md](../../apps/classification/LIMITATIONS.md) #5** — *cites* SRS CLS-001 and ADR-0002; does not repeat the formula.

Moving the formula is then a **one-file edit**. Cross-references stay valid because they are references, not copies.

**This is how OSS specs survive (RFC, W3C, POSIX).** The specification itself is normative; everything else is documentation about the specification. The distinction is maintained rigorously.

**Solo-operator cost.** Zero new tooling. Discipline only. Adopt immediately.

### 3.2. Fitness functions on cross-artefact drift

The existing architecture-test layer ([apps/classification/tests/architecture/](../../apps/classification/tests/architecture/)) already enforces structural rules. Extend it with grep-based pytests that catch documentation drift.

Examples:

```python
# ~20 lines, runs in the existing suite
def test_limitations_md_does_not_reference_cls_004_as_statistical_fallback():
    text = (REPO_ROOT / "apps/classification/LIMITATIONS.md").read_text()
    # CLS-004 is AI-response validation only (per SRS v2.3.2).
    # Statistical fallback moved to CLS-009.
    hits = [line for line in text.splitlines()
            if "CLS-004" in line and "fallback" in line.lower()]
    assert not hits, f"stale CLS-004 fallback references: {hits}"

def test_no_tanh_scale_in_classifier_code_post_ecdf():
    for path in (REPO_ROOT / "apps/classification/app").rglob("*.py"):
        if "_TANH_SCALE" in path.read_text():
            pytest.fail(f"{path}: _TANH_SCALE survives post-ECDF migration")
```

**Solo-operator cost.** ~1–2 hours per rule. Runs in the existing CI suite. Catches exactly the bug class §2 describes.

**Key insight.** Mature orgs do this. Solo operators skip it because the *perceived* setup cost feels high — the real cost is small. Each rule pays for itself the first time it catches a drift the operator would otherwise have merged.

### 3.3. Concept-onboarding as a first-class deliverable

The [doc/concepts/statistics.md](../../doc/concepts/statistics.md) pattern is already the right answer. Make it **mechanical rather than relied-upon**.

Steps:

1. Create `doc/concepts/INDEX.md` — a table mapping every term defined in SRS §3 Definitions to its concepts-doc entry (file + section number).
2. Add a fitness function: parse SRS §3 Definitions, parse INDEX.md, fail on any term in §3 that has no concepts-doc entry.
3. When a new SRS revision adds a term to §3, the fitness test fails until the operator writes the concepts entry. The failing test **is** the concept-onboarding trigger.

**Solo-operator cost.** ~2 hours for the INDEX + parser. Eliminates the class of risk the operator named ("a risk I can accept is something I don't know") at the point where it would otherwise compound.

### 3.4. Volatility labels

Tag every requirement / ADR / section:

- `Experimental` — actively being iterated. Free to churn. No ADR required for changes.
- `Draft` — worked example exists. Changes should leave an audit trail but don't require ADR.
- `Stable` — load-bearing across iterations. Changes require an ADR.
- `Locked` — part of the stable external contract (DeltaFeed EVO-001). Changes require an ADR *and* a superseding SRS revision.

Applied to the current state:

- CLS-001 → `Stable` (formula landed, matches CLS-002/CLS-006 pattern).
- CLS-009 → `Draft` (exists; will tighten once real degraded-confidence cases surface).
- SIG-001 → `Locked` (source-category contract; DeltaFeed depends on it).
- Insight 7 (§8.9) → `Experimental` (spec precision is the experimental variable).

**Value to the operator and to future AI agents.** Reading a labelled artefact answers "should I touch this without opening an ADR?" in under one second. Unlabelled artefacts require context reconstruction each time.

**Solo-operator cost.** ~2 hours to label the existing artefact set; ~30 seconds per new artefact thereafter. Standard practice in IETF and W3C for exactly this reason.

### 3.5. Delta-first documentation

For every requirement-changing revision, write the **delta doc first** (what changed, why, with references) before touching the SRS body.

This work already does this informally:

- ADR-0002 — why ECDF replaces tanh.
- `doc/research/srs-revision-v2.3.2-cls-001-cls-009.md` — the v2.3.2 deltas themselves in paste-ready form.
- Session-notes for concepts introduced en route.

**Codify as standing policy.** No SRS body edit without a co-located delta doc. The delta doc becomes the review artefact (fast to scan) while the SRS body is the authoritative state (slow to re-read each time).

**Solo-operator cost.** Doubles the documentation surface initially. Pays back on every subsequent revision — the operator reviews the delta, not the 30-page spec.

### 3.6. Distinguish stable vs experimental surface

Heavy artefacts — typed clients, OpenAPI schema validators, generated TypeScript from OpenAPI, exhaustive contract tests — pay off on **stable** surface.

On **experimental** surface (where Insight 7 intentionally churns), those artefacts are pure friction. Every churn means regenerating, rewriting tests, updating typed clients, re-running integration suites.

**Rule.** Keep experimental parts in markdown + schema-free tests until they stabilise. Promote to heavy artefacts only when the volatility label moves to `Stable`.

**Applied to PrimeScore AI.** SIG-001 (Locked) deserves a typed client and exhaustive contract tests. The ECDF formula — which was Experimental two weeks ago and is now Stable — is the right moment to generate typed fixtures. CLS-009 (Draft) is *not yet* — wait until it shakes out.

**Solo-operator cost.** Discipline only, no tooling. Avoids the anti-pattern of over-investing in tooling that has to be torn down on the next iteration.

---

## 4. Trade-offs given up

Each pattern costs something the operator should name explicitly before adopting.

| Pattern | Cost |
|---|---|
| SSoT per concept | Forces discipline. Every temptation to "just copy this sentence into the ADR" has to be resisted. Solo operators often lose this discipline under pressure. |
| Fitness functions | Tooling debt. Each rule is 1–2 hours that isn't going into product. Rules can themselves rot if the spec language changes and the regex doesn't. |
| Concept-onboarding INDEX | One more file to maintain. If the parser breaks, the signal breaks silently. |
| Volatility labels | Vocabulary overhead on every artefact. If labels aren't enforced, they rot into decoration. |
| Delta-first | Doubles the documentation surface initially. Worth it if there's a second revision; pure cost if there isn't. |
| Stable-vs-experimental distinction | Requires an honest self-assessment per artefact. Easy to mis-label `Experimental` as `Stable` to avoid the bookkeeping of the former. |

**Common failure mode of all six.** Solo operators adopt the patterns in a burst, then let them rot because no one is watching. The fitness-function discipline matters most *because* it's the one that watches the others.

---

## 5. Dissenting view

DeltaFeed has a **finite 6-iteration lifespan**. Most of the infrastructure described above amortises over years, not iterations.

The defensible counter-recommendation:

- Skip the tooling.
- Rely on dense commit messages + the existing session-notes habit.
- Accept documentation drift as part of the experimental setup — the experiment cares about agent performance under a stable external contract, not about the internal artefact-consistency posture.
- Revisit the infrastructure question at the end of iteration 1, once the actual per-iteration cost of drift is measured rather than estimated.

**When this counter-recommendation is right.** If iteration 1 turns out to be close to the full project lifespan (research question answered early, or operator priorities shift), the tooling investment is pure waste. The operator should be honest about that probability.

**When it's wrong.** If the project runs its full 6 iterations, the tooling pays for itself several times over. The drift in iteration 1 compounds into iteration 2, which compounds into iteration 3. By iteration 3 the operator is spending more time on documentation archaeology than on the experiment.

**Best single heuristic for the solo operator.** Adopt the three cheapest patterns immediately (SSoT, fitness functions, concept-onboarding INDEX — total ~5 hours). Defer the other three until the end of iteration 1, then re-decide based on measured drift cost.

---

## 6. Next actions

Concrete. Assignable. Time-bound. Ordered by effort.

- **[Zero-cost, do now.]** Add `Status:` markers to CLS-001 (`Stable`) and CLS-009 (`Draft`) in the next SRS v2.3.2 paste-into-Word pass.
- **[Zero-cost, do now.]** Adopt SSoT discipline for the ongoing ECDF work: ADR-0002 cites SRS CLS-001 by number; no duplicate formulas.
- **[~1 hour.]** Write a fitness test over [LIMITATIONS.md](../../apps/classification/LIMITATIONS.md), [HARNESS.md](../../apps/classification/HARNESS.md), and [ADR-0002](../../apps/classification/doc/adr/0002-ecdf-severity-and-backtest-harness.md) that fails on stale `CLS-004` references; generalise to a cross-artefact-ref assertion taking `(stale_term, exclusion_pattern)` pairs.
- **[~1 hour.]** Write a fitness test that fails if `_TANH_SCALE` or `tanh` appears in non-deprecated classifier code post-ECDF migration.
- **[~2 hours.]** Create `doc/concepts/INDEX.md` cross-referencing SRS §3 Definitions to concepts-doc entries; make it a fitness-test target.
- **[Deferred to post-iteration-1.]** Volatility labels across the full artefact set.
- **[Deferred to post-iteration-1.]** Delta-first documentation as standing policy.
- **[Deferred to post-iteration-1.]** Contract-generation tooling (OpenAPI → typed clients, etc.) for `Locked` surface only.

---

## Links

- [apps/classification/doc/adr/0002-ecdf-severity-and-backtest-harness.md](../../apps/classification/doc/adr/0002-ecdf-severity-and-backtest-harness.md) — ECDF decision
- [apps/classification/LIMITATIONS.md](../../apps/classification/LIMITATIONS.md) — known weaknesses
- [apps/classification/HARNESS.md](../../apps/classification/HARNESS.md) — three-layer test model
- [doc/concepts/statistics.md](../../doc/concepts/statistics.md) — domain-concept reference (the pattern §3.3 generalises)
- [doc/research/srs-revision-v2.3.2-cls-001-sig-001-1-cls-009.md](../../doc/research/srs-revision-v2.3.2-cls-001-sig-001-1-cls-009.md) — v2.3.2 delta deliverable (example of §3.5 delta-first in practice)
