# PrimeScore Markets — learning module design brief

You are a learning-experience designer working with a product engineer. Design the in-product
learning module for **PrimeScore Markets v2**, the paid product decided in
`doc/briefs/markets-product-study.md` and built per `doc/briefs/markets-v2-build-brief.md`.
The module has one job: get a new customer from "I do
not know what an option is" to acting on their first trade idea with a paper account, correctly
and inside their first session, then keep them reading the track record honestly. A customer who
understands the idea, its max loss and its exit plan stays and pays; one who does not churns after
the first losing trade. Paths are relative to the Markets repository root. Read §1 and §2 first.
Where this brief decides, follow it. Where it says "ask", ask.

---

## 1. What the product does, in the customer's words

| Stage | Where | Customer sees | Module must make them able to |
| --- | --- | --- | --- |
| 01 Observe | Data & health | Observed VIX or OVX level, source, time | Say what the number measures |
| 02 Classify | Signal analysis | Signed severity −1 to +1, confidence, degraded flags | Say how unusual today is and how much to trust that |
| 03 Compare | Market overview | Model scenario, scenario gap, calibration status | Say whether the model sees volatility as under- or over-priced |
| 04 Review | Decision journal | Condition checks; **Conditions met** or **Wait** | Say why an idea was or was not produced |
| 05 Idea | Ideas (P1) | Structure, strikes, expiry, size for their capital, max loss, breakevens, entry window, disclosure block | Place the idea in a paper account and state the max loss |
| 06 Manage | Positions (P2) | Open position, exit rules armed: time stop, catalyst-relative exit, Vega-crush gate, drawdown cooldown | Act on an exit alert within its window |
| 07 Record | Track record (P3) | Backtest, paper and live columns; hit rate, average and worst P&L, max drawdown, exposure days | Read a losing month without concluding the system is broken, or a winning one without over-sizing |
| Feedback | Historical replay | Rerun rules on a past event with costs | Check the record themselves |

Until P1 ships, the module covers stages 01 to 04 and Feedback only, and stage 05 to 07 content
is authored but hidden behind the milestone flag. Do not teach unshipped features as available.

## 2. Ground truth

| Path | Why |
| --- | --- |
| `doc/briefs/markets-product-study.md`, `doc/briefs/markets-v2-build-brief.md` | What is sold and to whom; the stages in which it ships |
| `doc/concepts/trading.md` | Long straddle payoff, breakevens, catalyst-relative exit, Vega-crush gate, Gamma–Vega ledger, from first principles |
| `doc/concepts/statistics.md`, `doc/srs/PrimeScore-SRS.md` §3 | The statistics behind severity, combined score, ECDF rank |
| `apps/engine/src/Host/PrimeScore.Engine.Host/Components/Pages/*.razor`, `Shared/WorkflowGuide.razor` | Exact labels and copy; the existing four-stage "How it works" page you extend |
| `doc/adr/0007-research-workflow-and-decision-presentation.md` | Explain names, units and thresholds in the UI; never rename API fields |
| `apps/engine/LIMITATIONS.md`, `apps/classification/LIMITATIONS.md` | What the numbers cannot establish; source of the "what this is not" content |
| `AGENTS.md` | Brevity rule for everything you add |

## 3. Levels

Self-selected at sign-up, changeable anywhere, with an optional five-question placement.

| Level | Already knows | Must be able to do after the module | Misconception to kill |
| --- | --- | --- | --- |
| **L0 Beginner** (the founder's starting point) | Nothing about options or this product | Explain call, put, premium, max loss; read one idea card; place it in a paper account; recognise an exit alert; read the track record's three columns | An idea is a guarantee; **Conditions met** alone is a reason to buy anything; a losing month means fraud |
| **L1 Options-literate trader** | Calls, puts, IV, VIX, has traded before | Explain why this structure, strike and expiry; size to their own limits; explain each exit rule and when it fires; compare the idea's cost with the breakevens | Holding through the catalyst "because it is going my way" |
| **L2 Quant / sceptic** | Options, volatility regimes, statistics | Audit the record from the ledger; explain the calibration and held-out period; explain ECDF rank, corroboration, degraded markers; run a replay with costs | The backtest column is the live column |
| **L3 Desk / API user** | The product as a trader | Use the API, connect a paper broker, set team limits, act on DEC-004 approvals | Approval equals recommendation |

Each level is a delta from L0: skip, compress to one line, or add. L0 is specified in full.

## 4. Concept ladder

Introduce each term once, in plain words, before it appears on a screen. A lesson may not use a
term from a rung it has not introduced.

| Rung | Concept | Build from | Appears |
| --- | --- | --- | --- |
| 1 | Option, call, put, premium, strike, expiry, max loss = premium paid | `doc/concepts/trading.md` §1 | Idea card |
| 2 | Volatility, implied volatility, VIX and OVX; why being long volatility into a catalyst can pay and why the crush after it hurts | VIX 37.32 on 5 Feb 2018 as the worked example | Market overview |
| 3 | Long straddle payoff, breakevens, cost of the trade | Payoff is the distance from the strike minus both premiums; strike-25 example in `doc/concepts/trading.md` §1 | Idea card |
| 4 | Observation, source, time; severity, confidence, degraded flags | "How unusual is today against five years" | Signal analysis, Data & health |
| 5 | Combined score, sensitivity, model scenario, gap, calibration status | `20 × (1 + 0.5 × 0.5) = 25` | Market overview |
| 6 | Conditions; **Conditions met** / **Wait**; why an idea follows one and not the other | Every configured check passed | Decision journal |
| 7 | Idea anatomy: structure, strikes, expiry, size from capital and limit, entry window, disclosure block | P1 output | Ideas |
| 8 | Exit rules: time stop, catalyst-relative exit with `T_safety`, Vega-crush gate, profit target; drawdown cooldown | `doc/concepts/trading.md` §2 to §4 | Positions, alerts |
| 9 | Track record: backtest versus paper versus live; hit rate, average and worst P&L, max drawdown, exposure days; costs and slippage | P3 record page | Track record |
| 10 | Ledger, replay, in-sample: how to audit any figure yourself | Every number links to its ledger entry | Decision analysis, Historical replay |
| 11 | What is still research-grade or planned | `LIMITATIONS.md` files, product brief milestones | Everywhere the flag applies |

L0 completes rungs 1 to 9 and 11. L1 starts at rung 3. L2 starts at rung 4 and adds `SRS §3`
depth plus rung 10 in full. L3 adds API, broker connection and approvals.

## 5. Interaction model

"Live" means anchored to the customer's own current idea, position and record, not a course
beside the product. "Efficient" means the next screen's vocabulary and nothing more.

1. **Contextual explain.** Every figure and label on Ideas, Positions, Track record, Market
   overview, Signal analysis, Decision journal and Historical replay has an "explain this" card:
   one plain sentence for L0, the rule or formula for L1 and L2, the API field or setting for
   L3. Cards quote the customer's own value when one exists.
2. **First-idea walk.** Triggered when a customer's first idea arrives, or on demand with the
   most recent idea. Steps: what this structure is; your max loss in your currency; breakevens;
   the entry window and what happens if it closes; the exit rules armed; how to place it in your
   broker's paper account (screens for Interactive Brokers and Tradier); what alert you will get
   and when. Ends with the check in 4. Under 15 minutes for L0.
3. **First-exit walk.** Fires with the first exit alert: which rule fired, what to do, how long
   the window is, what the ledger recorded.
4. **Retrieval checks.** Two to four questions per stage from the customer's live data ("What is
   your max loss on this idea?" "Which exit rule fires first if the catalyst is in 6 hours?").
   A wrong answer opens the card. No scores shown to others.
5. **Record reading.** A guided read of the track record after the customer's first closed
   position and again after their first losing one: which column they are in, what the drawdown
   figure means, what would have to happen for the operator to stop the strategy.
6. **Placement and level switch.** Five questions: what an option is; what VIX measures; what a
   max loss is; whether **Conditions met** means you must trade; what the paper column means.
   Level visible and switchable on every card and step; progress kept on switch.

All six work with authored static content in the Blazor Server app and the customer's ledger
data. Specify separately an AI-assisted tutor that answers free questions grounded in the
customer's idea, position and the authored content, with these guardrails: no personal advice
beyond the customer's own set limits, no return promises, no invented prices, "planned" for
unshipped features, and a logged refusal list. The base module must not depend on it.

## 6. Content rules

- Use the product's exact wording: **Conditions met**, **Wait**, "Model scenario", "Scenario
  gap", "Max loss", the exit rule names, the three record columns.
- Every idea lesson shows max loss before anything else and the disclosure block last.
- Forbidden: guaranteed, risk-free, will profit, expected return without its column and period,
  any sentence that tells a specific customer what to do with money beyond applying their own
  limits, and any statement that an unshipped feature exists.
- Every number in a lesson links to its ledger entry or record query.
- L0 gets the "this can lose the whole premium" line on every idea step; L2 and L3 get it once.
- Brevity per `AGENTS.md`: a sentence names a mechanism, number, rule or check, or it is cut.

## 7. Constraints

- Blazor Web App, interactive server render mode, Razor components, native SVG; no frontend
  build toolchain; design tokens from `sites/markets/`.
- Multi-tenant hosted product from P4; progress and level stored per account; no third-party
  analytics beyond what the compliance checklist allows.
- One operator maintains everything: content in one authored source, a label change in one
  place, milestone flags hide unshipped stages.
- Never rename API fields or change formulas; presentation only.

## 8. Deliverables

1. Curriculum map: rungs × levels, each cell full / one line / skip, with its milestone flag.
2. Screen-by-screen interaction spec for the seven pages: every explain anchor with L0 to L3
   text; the walk step it belongs to; empty, loading and degraded states.
3. L0 first-idea walk in full, using a recorded idea, with broker paper-account screens.
4. First-exit walk and record-reading scripts.
5. Placement questions and mapping.
6. Glossary: L0 sentence, L2 definition, source path, for every term in §4.
7. AI-tutor spec with 20 customer questions and acceptable answers, 5 of them refusals.
8. Measurement plan: first-idea walk completion, time to first paper order, retrieval-check
   pass rate, retention after first losing position, all from account events in the ledger.
9. Open questions (§10) with your recommended default.

## 9. Acceptance

- An L0 tester with no options knowledge completes the first-idea walk in under 15 minutes and
  then states the idea's max loss, its breakevens, which exit rule fires first, and what the
  paper column means.
- Every figure in a lesson exists on the referenced page or ledger entry.
- No lesson, card or tutor answer contains a §6 forbidden term.
- Unshipped stages are hidden by flag and labelled "planned" where referenced.
- Level switch changes content and keeps position on every step.
- Completion, time to first paper order and retention after a loss are measurable from the ledger.

## 10. Ask, do not assume

1. Which brokers' paper accounts to document first (default: Interactive Brokers, Tradier).
2. Whether the record-reading walk may show other customers' aggregate outcomes.
3. Whether the AI tutor is in the first paid release or specified only.
4. Which tier includes L3 content.
5. English only, or English and Romanian.
