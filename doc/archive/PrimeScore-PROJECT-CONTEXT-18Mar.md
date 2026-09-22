# PrimeScore AI Project Context — March 18, 2026

Upload this file alongside `PrimeScore AI-SRS-v2.2.1.docx` and `PrimeScore AI-API-v1.yaml` when starting a new conversation.

---

## What PrimeScore AI Is

A volatility-exploitation trading system used as a **research vehicle** to measure how AI agents (Claude Code) handle six architecture patterns. The system is real and designed for eventual production use. The research produces publishable articles with quantifiable findings on AI-driven software architecture.

**Governing thesis:** *"We don't know how to architect systems that are built, maintained, and evolved by AI agents."*

## Current State

| Artifact | Status |
|---|---|
| SRS v2.2.1 | Complete. `.docx` file, edited directly from v2.2 XML. |
| PrimeScore AI-API-v1.yaml | Complete. 20 endpoints, 43 schemas. The external interface contract. |
| API acceptance test design | Complete as traceability map (markdown). Not yet code. |
| CLAUDE.md for Iteration 1 | **Next deliverable** |
| Python classification service | **Next deliverable** (can parallel with CLAUDE.md) |
| Solution structure + NSwag codegen | Not started |
| API acceptance test implementation | Not started |
| Iteration 1 implementation | Not started |


## Six Iterations

1. Transaction Script (baseline) — all Must requirements
2. Vertical Slice Architecture (refactor from 1) — CT-01, CT-02, CT-03
3. Clean Architecture with Rich Domain Model (rewrite) — CT-04, CT-05, CT-06, CT-07
4. Clean Architecture + Event Sourcing (refactor from 3) — CT-08, CT-09
5. Microkernel applied to Classification (localized refactor) — CT-10, CT-11, CT-12, CT-13
6. Modular Monolith (production synthesis) — CT-14, CT-15, CT-16

## Key Architecture Decisions

### Contract-First Design
- `PrimeScore AI-API-v1.yaml` is the single source of truth
- NSwag generates a shared project PrimeScore AI.Api.Contracts with **controller base classes, DTOs and typed HTTP client**
- Test project and WebApi refference PrimeScore AI.Api.Contracts
- The generated controller base class enforces routes, HTTP methods, input/output types
- Each iteration inherits from the generated bases and fills in the logic

### Document Hierarchy
- **SRS v2.2.1** — requirements (what). References the OpenAPI spec.
- **PrimeScore AI-API-v1.yaml** — external interface contract (message content and format per IEEE 830 §3.1)
- **CLAUDE.md per iteration** — agent context (ACX-001, ACX-002)
- Three separate artifacts. Never merged into one document.

### Test Strategy
- API acceptance tests run as **separate process** (own Program.cs, pure HTTP client)
- Test data loaded via `POST /admin/signals` (admin ingestion endpoint) — no DB seeding
- Each test run starts against a **fresh application instance** with clean state
- Validation event data stored as JSON fixture files in the test project
- For CLS-002 and CLS-006, tests include **hand-calculated expected outputs** from the formulas

### Python Classification Service
Constant external dependency across all six iterations. Multi-strategy classification engine:

| Source Category | Strategy | Skills |
|---|---|---|
| GEOPOLITICAL unstructured | LLM + RAG + LangChain | Prompt engineering, RAG, AI APIs |
| GEOPOLITICAL structured | Rule-based + LLM enrichment | Prompt engineering |
| MARKET_DATA | Statistical anomaly detection | ML fundamentals |
| MACROECONOMIC | Surprise magnitude scoring | ML fundamentals |
| CROSS_ASSET_FLOW | Z-score regime detection | Time series analysis |

**HTTP contract**: `POST /classify` → `{ severity, certainty, event_taxonomy?, reasoning_trace }`

.NET iterations still own: composite scoring (CLS-002), IV dislocation (CLS-006), response validation + fallback (CLS-004), plugin routing (Iteration 5), monitoring alerts (CLS-008).

The Python service has its own test suite validated against 10 historical events, independent of the .NET API acceptance tests.

### What .NET Owns (7 bounded contexts)
Signal Ingestion · Classification orchestration · Decision Engine · Position Construction · Exit Management · Risk Management · Analytics

### Signal Source Categories & Data Sources
- **MARKET_DATA** — VIX/options pricing. Sources: CBOE, FRED, Yahoo Finance (free)
- **MACROECONOMIC** — rates, yields, indicators. Source: FRED API (free)
- **GEOPOLITICAL** — intelligence feeds. Sources: GDELT (free, 15min delay), NewsAPI.ai (paid)
- **CROSS_ASSET_FLOW** — correlations, fund flows. Derived from Yahoo Finance price data (free)

## Skill-Building Goals

Radu is positioning for AI engineer / Software Architect / senior big tech roles. PrimeScore AI covers:
- **.NET/C#** — six architecture iterations
- **Python** — classification service
- **RAG, LangChain, Prompt Engineering** — GEOPOLITICAL classifier
- **ML Fundamentals** — anomaly detection, accuracy monitoring, evaluation harness
- **AI APIs** — Claude/OpenAI integration
- **MCP** — Claude Code agent tooling

## Build Sequence

1. CLAUDE.md for Iteration 1 ← **start here**
2. Python classification service (can parallel with step 1)
3. PrimeScore AI solution structure + NSwag contract-first code generation
4. Implement API acceptance tests
5. Iterations 1–6

## Known Decisions That Were Hard-Won

- **SRS doesn't contain schemas/endpoints** — that's the OpenAPI spec's job. Stripe/Google/Amazon model.
- **Tests before implementation only works because contract exists first** — sequence is: SRS → OpenAPI spec → Iterations 1–6.
- **Eventual consistency is not a concern** — Event Sourcing in Iteration 4 will use synchronous projections.
- **The Python service doesn't affect measurements** — it's constant infrastructure. The .NET architectural variation (seven bounded contexts, 16 change tasks) is fully intact and represents ~95% of the codebase complexity.
