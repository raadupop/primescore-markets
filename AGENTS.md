# PrimeScore Markets

PrimeScore Markets is a product of PrimeScore AI. This repository owns its .NET engine, login and dashboard, Python classifier, product presentation site and validation harness; see [README.md](README.md) for implemented scope. Active development stays in `D:\Work\primescore-markets`; `D:\Work\primescore` owns the brand site and includes this repository at `products/markets` as a Git submodule.

The architecture research programme formerly called DeltaFeed is deferred. Preserve its design and existing product checks; do not start architecture iterations or expand the generic harness without an explicit research task. There are no comparative results.

## Single-operator constraint

PrimeScore AI has one operator with finite attention. Every artifact and every agent turn competes for the same budget. Default to ruthless brevity over completeness.

- **Shortest correct answer wins.** No restatement, no warm-up paragraph, no exhaustive enumeration. If a link suffices, use the link.
- **One destination per fact.** Don't write the same thing in two places. If it already lives in `HARNESS.md`, `doc/todo/registry.yaml`, a component ADR, or a contract file under `harness/`, link to it.
- **No filler turns.** Don't narrate "I will now do X" before doing X. Don't summarize unless asked. End-of-turn output is one or two sentences: what changed, what's next.
- **Cut, then cut again.** Default to deleting. If a sentence does not name a constraint, mechanism, number, or interface, drop it.

This rule overrides verbosity defaults in any other doc or skill. When in doubt, write less.

## Repository Structure

- `doc/` — SRS (Markdown-native, see `doc/srs/`), PrimeScore-API-v1.yaml, project-wide ADRs
- `apps/classification/` — Python classification service (constant across all iterations)
- `apps/demo/` — local historical replay dashboard; separate from the engine iterations
- `sites/markets/` — static product page; umbrella branding and Cloudflare workspace belong to `D:\Work\primescore`
- `docs/` — status, demonstration and deployment runbooks
- `apps/engine/` — .NET Markets application; separate from the deferred architecture study

## Document Hierarchy

| Document | Role |
| --- | --- |
| [SRS](doc/srs/PrimeScore-SRS.md) | Requirements — what the system must do |
| [PrimeScore-API-v1.yaml](doc/PrimeScore-API-v1.yaml) | External interface contract — message content, format, schemas |
| [doc/slices/](doc/slices/) | Build order and one spec per demoable slice: SRS IDs, design links, UI tests and click-through. Holds no requirement or decision of its own. |
| [doc/adr/](doc/adr/) | Project-wide architectural decisions. Start with [ADR-0001](doc/adr/0001-agent-harness-architecture.md) — the agent harness architecture (five layers — context, cognitive tools, permissions, feedback oracles, decision durability). |
| AGENTS.md (per component) | Agent context. `CLAUDE.md` exists at each level as a pointer stub so Claude Code's auto-discovery still resolves. |
| Per-component `doc/adr/` and `HARNESS.md` | Component-specific architectural decisions and the regenerable per-component harness inventory. |

The SRS references the OpenAPI spec, not the other way around. Component-specific ADRs (e.g. classification's [ADR-0003](apps/classification/doc/adr/0003-test-oracle-architecture.md)) instantiate the project-wide harness layers at each component; they do not re-decide the harness shape.

## Architecture Iterations (deferred)

1. Transaction Script (baseline) — all Must requirements
2. Vertical Slice Architecture (refactor from 1)
3. Clean Architecture with Rich Domain Model (rewrite)
4. Clean Architecture + Event Sourcing (refactor from 3)
5. Modular Monolith (sourced from Iteration 3)
6. Service Extraction (Decision Engine)

## Python Classification Service

Lives in `apps/classification/` with its own `AGENTS.md`. The Markets engine calls `POST /classify` over HTTP. The study design treats it as constant infrastructure across all six iterations.

## Conventions

- Contract-first: PrimeScore-API-v1.yaml is the single source of truth for the .NET API
- API acceptance tests are strictly black-box (EVO-001)
- Product structural tests enforce the current engine architecture; study structural tests are iteration-specific
- Fixed agent context (ACX-001, ACX-002) applies to study runs, not ordinary product development
- Python naming: see [doc/conventions/python-naming.md](doc/conventions/python-naming.md). Functions and modules describe their *output*, not the discriminator that dispatched to them.

## Agent runtime behavior

- When a hook surfaces a skill-invocation reminder, flag it explicitly in the end-of-turn output so the operator can decide whether to invoke the skill before commit.
- Before claiming a code change complete, rely on the harness Stop-hook pytest result; if red, address it or hand off to the operator with the failing test named.
- On pytest red (Case A per [ADR-0001](doc/adr/0001-agent-harness-architecture.md) §"Steering loop"): fix the implementation if the failing test correctly states the requirement; flag the test as a harness bug if it encodes a wrong assumption.
- On a Case B bug raised by an external observer (per [ADR-0001](doc/adr/0001-agent-harness-architecture.md) §"Steering loop"): respond with an ADR plus a new control in the same PR, or a `LIMITATIONS.md` entry — never a silent fix.


<claude-mem-context>
# Memory Context

# [invex] recent context, 2026-09-27 4:27pm GMT+3

Legend: 🎯session 🔴bugfix 🟣feature 🔄refactor ✅change 🔵discovery ⚖️decision 🚨security_alert 🔐security_note
Format: ID TIME TYPE TITLE
Fetch details: get_observations([IDs]) | Search: mem-search skill

Stats: 50 obs (33,251t read) | 446,686t work | 93% savings

### Sep 27, 2026
S3164 Define social media account strategy for PrimeScore AI and its products across platforms, balancing resource constraints with audience presence (Sep 27, 2:17 AM)
S3167 Repository structure and cross-project references: understanding how to organize the markets product alongside PrimeScore, and how PrimeScore should reference the markets product repo (Sep 27, 2:19 AM)
12759 3:08p 🔵 Repository inventory: M0-M6 milestones implemented with modular engine architecture
S3168 Inventory current state of PrimeScore products, platforms and naming strategy; evaluate ChatGPT brand analysis against prior research and decision record (Sep 27, 3:09 PM)
12760 3:09p 🔵 Engine modules follow 7-file consistent architecture; demo app Python-based; separate PrimeScore umbrella project
12761 " 🔵 V1 scope complete (M0-M6); positions/exits/risk deferred to M7+ pending options pricing data; geopolitical/LLM classifier out of scope
S3169 Technical documentation prose review for PrimeScore Markets README.md diff using chief-architect-grade discipline (Sep 27, 3:09 PM)
12762 3:16p 🔵 Repository architecture: Umbrella vs. product separation with Git submodule integration
12763 3:17p ⚖️ PrimeScore product portfolio naming finalized with trademark evidence and legal framework
12764 " 🔵 Stop hook steering loop implements Case A/Case B validation protocol from ADR-0001
12765 3:18p ✅ Brand trademark research and decision documents migrated from invex to primescore umbrella
12766 " ✅ Documentation updated to establish clear navigation and ownership for brand vs. product work
12767 " 🔵 Documentation changes validated; brand decision record discoverable and cross-linked
12768 3:19p 🔵 Pre-commit hook activated on README.md during brand research migration commit
S3170 How to organize and synchronize a prompt/product (Markets) that runs on D:\Work\invex with umbrella brand decisions in D:\Work\primescore; solution to keep them coordinated without duplication. (Sep 27, 3:19 PM)
12769 " 🔵 Brand research documents staged in primescore; invex pre-commit hook signaled to complete
12770 3:20p 🔵 Invex pre-commit hook still active; AGENTS.md has uncommitted working-tree changes
12771 " 🔵 Pre-commit hook session blocked; repeated EOF signals not completing hook
12772 " 🔵 Pre-commit hook timed out; invex commit succeeded with fail-safe allow
12773 " ✅ Cross-repository synchronization completed; primescore submodule pinned to brand research migration
12774 3:21p 🔵 Architecture migration validated; single source of truth established for brand research
S3171 Determine appropriate repository name for invex codebase following integration as PrimeScore Markets product into primescore umbrella (Sep 27, 3:21 PM)
S3172 Rename PrimeScore Markets repository from primescore-ai to primescore-markets and update all workspace references from D:\Work\invex to D:\Work\primescore-markets (Sep 27, 3:39 PM)
12777 3:41p 🔵 PrimeScore Markets architecture and single-operator constraints documented
12778 3:43p 🔵 PrimeScore Markets validation baseline: four failing tests and design gaps documented
12779 " 🔵 PrimeScore Markets product positioning: research-only volatility intelligence with local operator access
12780 " 🔵 Shortest path to end-to-end demonstration: 22–31 hours estimated, with classifier and CI repairs blocking dashboard work
12783 " 🔵 .NET engine modular architecture with eight specialized Claude agent skills and three validation hooks
12781 " 🔵 Primescore-AI host process not running
12782 " 🔵 Primescore-AI repository URL distribution across codebase
12785 " 🔵 PrimeScore Markets v1 build brief defines product scope, honesty rules and architecture decision
12784 " ✅ Repository and working directory renamed for Markets product separation
12787 3:44p ✅ Completed path migration and umbrella repository submodule synchronization
12786 " 🔵 Existing "How it works" guide page and WorkflowGuide component document four-stage research workflow for customers
12788 " 🔵 Test fixtures reference outdated primescore-ai repository URL
12789 3:45p ✅ Navigation test fixture updated to reference primescore-markets repository
12790 3:46p 🔵 Test fixture setup fails due to missing scipy dependency
12791 3:48p 🔵 Repository check suite hangs or exceeds 120-second timeout
12792 " 🔵 Repository check suite completed successfully despite initial timeout appearance
S3173 Rename project from INVEX to primescore-markets; update all references across umbrella and active development repositories; resolve brand/trademark investigation and prepare for directory/repository rename (Sep 27, 3:49 PM)
12793 3:49p 🔵 PrimeScore Markets platform architecture and requirements landscape
12794 3:50p 🔵 Engine validation results against ten historical market events
12795 3:53p 🔵 Repository structure confirms INVEX-to-PrimeScore rebranding timeline and submodule setup
12797 3:54p ⚖️ Customer learning module architecture: Four explicit domain-literacy levels with honesty constraints tied to product scope
12796 " 🔵 Brand decision document updated with 2026-09-27 edits; HARNESS_PYTHON environment variable unset
12798 " 🔵 No Cloudflare Pages deployment configured; CI/CD limited to Python 3.12 checks on two platforms
12799 3:55p 🔵 Existing in-product teaching surfaces, constraints and gaps for customer education
12800 3:56p 🔵 Directory rename D:\Work\invex → D:\Work\primescore-markets blocked by active process lock
12801 " 🔵 Markets migration subtree→submodule completed 2026-09-27; validation passed; pre-commit review timed out and needs rerun
12802 3:57p 🔵 11 hardcoded GitHub repository references to raadupop/primescore-ai across umbrella; mostly documentation and footer/tests
12803 " 🔵 Concept inventory: what exists in the codebase, what is missing, and what contradicts between sources
12804 3:58p ✅ Updated umbrella repository for directory rename D:\Work\invex → D:\Work\primescore-markets; all tests pass
12805 " 🔵 Navigation test suites passing in both umbrella and INVEX after URL updates
S3174 Create a comprehensive prompt for designing an interactive, customizable learning module for the markets product that educates customers from beginner to advanced levels. (Sep 27, 3:59 PM)
12806 4:02p ✅ Markets Learning Module Brief Created
12807 4:08p 🔵 PrimeScore Engine V1 Requirements and Known Limitations Reviewed
12808 4:10p ⚖️ PrimeScore Markets v2 Product Brief: Transform Research to Paid Offering
12809 4:11p ✅ Learning Module Brief Redesigned for v2 Paid Product Onboarding
S3175 Transform PrimeScore Markets from research workspace to paid product: create product and learning module briefs with customer value proposition, five-milestone roadmap, and onboarding strategy (Sep 27, 4:12 PM)
12810 4:27p ⚖️ Product strategy for PrimeScore Markets: three-tier SaaS with phased rollout

Access 447k tokens of past work via get_observations([IDs]) or mem-search skill.
</claude-mem-context>
