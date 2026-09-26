# PrimeScore Markets

PrimeScore Markets is a product of PrimeScore AI. This repository owns its .NET engine, login and dashboard, Python classifier, product presentation site and validation harness; see [README.md](README.md) for implemented scope. Active development stays in `D:\Work\invex`; `D:\Work\primescore` owns the brand site and includes this repository at `products/markets` as a Git submodule.

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

# [invex] recent context, 2026-09-27 12:45am GMT+3

Legend: 🎯session 🔴bugfix 🟣feature 🔄refactor ✅change 🔵discovery ⚖️decision 🚨security_alert 🔐security_note
Format: ID TIME TYPE TITLE
Fetch details: get_observations([IDs]) | Search: mem-search skill

Stats: 50 obs (24,276t read) | 379,676t work | 94% savings

### Sep 26, 2026
12664 8:27p ✅ Final report optimized and verified; decision brief trimmed; deliverables finalized
12665 8:28p ✅ Decision brief optimized through targeted editing; final version 754 words (7% reduction)
12666 " ✅ Decision brief final-passed; 720 words, within 3% of 700-word target
12667 " ✅ Decision brief finalized at 704 words (0.6% over 700-word target)
12668 8:29p ✅ Decision brief achieved 700-word target (701 words, 0.1% overage)
12669 11:44p 🔵 Researched product umbrella architecture patterns
12670 11:46p 🔵 Examined existing agent harness architecture and project implementation status
12671 11:47p 🔵 Harness design aligns with Anthropic's published long-running agent patterns
12672 " 🔵 ADR-0003 engine architecture implements Anthropic's published harness design patterns
### Sep 27, 2026
12673 12:04a 🔵 Systematic market screening of 12 premium finance product names
12674 12:05a 🔵 Competitive landscape analysis of 8 candidate fintech product names
12675 12:06a 🔵 Screening of second batch: 10 additional premium finance product name candidates
12676 12:07a 🔵 Competitive landscape deep-dive: 7 second-batch candidate names in fintech ecosystem
12677 12:08a 🔵 Certitude name carries significant financial services brand presence globally
12679 12:09a 🔵 Screening of third batch: 6 additional premium finance product name candidates
12680 " 🔵 Third-batch competitive landscape: Quotient has established fintech competitor
12681 " 🔵 Git Submodules: Architecture and Usage Model
S3132 User rejected "Loupe" as insufficiently premium for finance product name; systematic screening and selection of replacement name from premium-word candidates (Sep 27, 12:10 AM)
12682 12:11a 🔵 Completed systematic product naming research: 28+ premium words screened, Aplomb recommended
S3133 Understanding Git submodules and how changes committed in INVEX synchronize with the primescore markets repository (Sep 27, 12:12 AM)
S3134 Resolve naming strategy challenge: "What connects to PrimeScore?" — determine whether to position a household finance product under the PrimeScore umbrella or as a standalone brand (Sep 27, 12:19 AM)
12686 12:20a 🔵 PrimeScore product variants have no public web presence
12687 " 🔵 Cadence Money identified as privacy-first personal finance app
12688 " 🔵 Multiple Atlas fintech products identified in market
12689 12:21a 🔵 PrimeScore Loan Scheme identified as Peoples Bank Sri Lanka product
S3135 Decision checkpoint: Is the six-architecture measurement study still relevant, or should focus shift to product strategy and customer validation? (Sep 27, 12:22 AM)
12690 12:22a 🔵 Investigated infrastructure noise and harness design relevance to measurement strategy
12691 12:23a 🔵 Anthropic research identifies infrastructure noise as first-class experimental variable; harness complexity should match model capability
S3136 Strategic evaluation of PrimeScore.AI as domain and product umbrella; clarification on forecasting expectations for PrimeScore Cashflow (Sep 27, 12:23 AM)
S3137 User requested strategic guidance to move forward with PrimeScore Markets v1 development; Claude provided a six-step action plan with clear sequencing and done criteria. (Sep 27, 12:27 AM)
12692 12:27a 🔵 PrimeScore Markets v1 Project State and Build Brief Review
S3138 Assess harness framework relevance and determine repository organization strategy for PrimeScore AI and Markets development (Sep 27, 12:28 AM)
12693 12:34a 🔵 Repository restructuring: brand and Markets sites consolidated in invex root
S3139 Assess harness framework relevance for INVEX R&D programme and establish repository organization strategy for Markets product development across PrimeScore and INVEX (Sep 27, 12:35 AM)
12694 12:35a 🔵 Git subtree import and repository consolidation architecture
12695 " 🔵 Markets harness steering architecture with retry budgets and state management
S3140 Implement repository separation infrastructure: remove brand site from INVEX, create umbrella launchers in PrimeScore, establish cross-repository integration patterns (Sep 27, 12:36 AM)
12696 12:36a 🔵 Backup script failure: invalid git diff --output syntax
12697 " ✅ Separate brand site from Markets infrastructure in INVEX; retire port 8090
12699 " 🔵 Confirmed: PrimeScore repository currently has no Markets-specific code or launchers
12698 " 🔵 Backup successful; Git line-ending configuration mismatch across working tree
12700 " 🔵 Development environment fully configured with PostToolUse and Stop hooks wired
12701 12:37a ✅ Brand site and Wrangler configuration migrated from invex to primescore
12702 12:38a 🟣 Add umbrella repository launchers and navigation tests for brand + Markets integration
12704 " ✅ Documentation restructured: Markets product scope clarified, architecture study deferred
12703 " 🔵 Navigation script generates dashboard URL without /login path; tests fail with assertion mismatch
12705 " 🔵 Root cause found: navigation.js generates dashboard URL without /login path suffix
12706 12:39a 🔵 Tool chain complete; umbrella launcher orchestrates brand and Markets together
12708 " 🔵 Website launcher integration test passed: both repositories serve content and shut down cleanly
12709 12:40a 🔵 Code quality and syntax validation passed for repository infrastructure changes
12710 " 🔵 Patch application failed: duplicate file operation validation
12711 " ✅ Umbrella repository (primescore) configured as Git submodule wrapper for Markets
12712 12:42a 🔵 Migration reconciliation complete: invex Markets product and primescore umbrella both configured
12713 12:43a 🔵 Path normalization fails on relative segments within product directories
12714 12:44a ✅ CI workflow extended with Node.js testing; trailing whitespace normalized
12715 " 🔵 Path normalization refactored to use realpath but boundary checking incomplete
12718 " ✅ Markets hook delegator implementation with comprehensive path normalization and test coverage
12716 " 🔵 Markets hook checks for submodule initialization via .git directory
12717 " 🔴 Markets hook path normalization fixed by reordering cygpath conversions
S3141 Fix path delegation between invex workspace and primescore product; ensure markets product references and path handling work correctly across Windows/Git Bash environments (Sep 27, 12:44 AM)
**Investigated**: Markets hook path normalization logic; relative path segment handling; Windows path format conversions (cygpath); product scope boundary validation; submodule initialization checks

**Learned**: Path normalization requires realpath -m to resolve ".." components properly; cygpath format conversions must occur before boundary checks to ensure path format consistency; markets hook includes safety check for missing .git submodule before delegating operations; Windows vs Unix path format conversions need careful ordering to avoid JSON decode errors

**Completed**: Added 6 regression tests to test_markets_hook.py covering absolute paths, relative segments, boundary escaping, product-relative paths, Stop commands, and uninitialized products; refactored .claude/hooks/markets.sh to use realpath -m for path normalization; fixed cygpath conversion ordering; all 6 tests passing with exit code 0

**Next Steps**: Deploy fixed markets hook; validate integration with invex; confirm path delegation working end-to-end between workspaces


Access 380k tokens of past work via get_observations([IDs]) or mem-search skill.
</claude-mem-context>
