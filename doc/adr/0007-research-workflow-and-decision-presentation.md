# ADR-0007: Research workflow and decision presentation

**Status:** Proposed

**Date:** 2026-09-25
**Deciders:** Radu Pop (review pending)

## Context

The operator could not distinguish model decisions from executed deployments, identify a
position or instrument, or follow the analysis in the dashboard. Raw condition names,
unlabelled figures and operational diagnostics occupied the primary reading path. Local
brand navigation also used obsolete preview ports. This is an externally reported Case B
failure under [ADR-0001](0001-agent-harness-architecture.md).

## Decision

Use one linked research workflow: observation, classification, scenario comparison and
condition review. An overview selects a market and presents one completed decision snapshot:
reference instrument, observation time, observed level, model scenario, gap and checks.
Do not combine numeric inputs from different decision snapshots. Newer source observations
are identified separately; coverage and calculation details appear only when their stored
identifiers match that decision.

Render `DEPLOY` as **Conditions met** and `IDLE` as **Wait**. Display absence of positions,
contract selection and execution alongside the result. Explain condition names, units and
thresholds in the presentation layer; keep API names, stored values and calculation formulas
unchanged. Statistical confidence is not a probability of trading profit. Raw identifiers,
provider errors and calculation traces remain available in expandable technical details.

Connect brand, product and dashboard navigation. Public product links describe dashboard
access; recognized local previews route to the local engine. Do not advertise an undeployed
public dashboard. Group actions and filters consistently, and stack panels on narrow screens.

## Controls

- The HTTP acceptance test
  `UI_research_analysis_explains_the_reference_values_and_conditions_without_implying_execution`
  checks instrument scope, no execution, readable conditions and hand-calculated Volmageddon
  figures through the public UI. The existing role test also checks source operation controls.
- [Site navigation tests](../../tests/sites/test_navigation.py) check public access links and
  workflow content. [JavaScript routing tests](../../tests/sites/navigation.test.cjs), run by
  `node --test tests/sites/navigation.test.cjs`, check local and public destinations.
- Review the rendered workflow at desktop and mobile sizes when browser execution is available;
  HTTP assertions cannot establish visual clarity.

## Consequences

The operator can inspect the evidence chain without treating a condition result as an order.
The change does not add a broker, positions, contract selection or predictive calibration.
Historical validation and missing evidence remain visible; clearer presentation does not
change the model's measured results.
