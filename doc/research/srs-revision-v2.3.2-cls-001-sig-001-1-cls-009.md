# SRS v2.3.2 revision drafts — §3 Definitions, CLS-001, CLS-009, §11.12

- **Target SRS version:** v2.3.2 (to be produced from v2.3.1 by applying the blocks below)
- **Source SRS:** `doc/PrimeScore-SRS-v2.3.1.docx`
- **Status:** Interim text, awaiting paste into Word and save-as v2.3.2
- **Date:** 2026-04-18 (revised same day after user critique — terminology bundled in §3 Definitions; SIG-001 not modified; CLS-001 certainty formula made explicit)
- **Relates to:** [ADR-0002](../../apps/classification/doc/adr/0002-ecdf-severity-and-backtest-harness.md), [CLS-001 SRS annex stub](../../apps/classification/doc/adr/srs-annex-cls-001-severity-formula.md)

## Purpose

The v2.3.1 SRS leaves CLS-001 qualitative ("severity is quantified" with no formula). Per ADR-0002, this underwrites a self-validating-loop bug class that the current acceptance harness cannot catch. This document contains four exact-replacement text blocks for the next SRS revision:

1. **New entries in §3 Definitions** — bundle all new vocabulary (deviation, ECDF rank, indicator class, indicator registry, `N`, `D`, deviation_kind, z-score deprecation) in one place so CLS-001 and CLS-009 stay scannable.
2. **Revised CLS-001 [§5.2]** — inline ECDF formula scoped via the requirement body to MARKET_DATA, MACROECONOMIC, CROSS_ASSET_FLOW; certainty formula made explicit as `source_reliability × temporal_relevance`.
3. **New CLS-009 [§5.2, Must]** — RULE_BASED degraded-confidence fallback (distinct from CLS-004, which is AI-response-specific).
4. **Amended §11.12** — extends hand-calculated acceptance to CLS-001.

**Design choices locked after user critique:**

- **SIG-001 is not modified.** No SIG-001.1. Source-category extensibility (CT-01, CT-08) is preserved verbatim. Strategy scope lives in CLS-001's own body.
- **Every new term is defined in §3 Definitions, not inline in requirements.** The SRS is already heavy; spreading new vocabulary across CLS-001 and CLS-009 would make it heavier. Definitions localise the cognitive load.
- **The indicator registry is anchored in §3 Definitions**, not in a numbered requirement. CLS-001 and CLS-009 reference it by term.

Application procedure is at the bottom.

## Conventions preserved from v2.3.1 §5 preamble

> Each requirement: unique ID, MoSCoW priority, single "shall" statement (what, not how), rationale, verification.

All four blocks follow this form. "Shall" language is normative per ISO/IEC/IEEE 29148:2018.

---

## Block 1 — §3 Definitions additions

**Action:** Insert the following entries into §3 Definitions, alphabetically. Do not remove or reword existing definitions. The existing `[term]`/paragraph formatting of §3 should be used for each entry.

---

**Deviation.** Absolute difference between an observed value and a strategy-specific reference value, computed without rescaling by standard deviation (i.e., not a z-score). By source category:

- **MARKET_DATA:** `deviation = |current_value − rolling_median(history)|`
- **MACROECONOMIC:** `deviation = |actual_value − consensus_expected_value|`
- **CROSS_ASSET_FLOW:** `deviation = |pairwise_correlation − rolling_baseline_correlation|`

`deviation` is the variable consumed by the CLS-001 severity formula. For GEOPOLITICAL signals `deviation` is not defined; severity is produced via CLS-003.

**deviation_kind.** Per-indicator-class enumeration `{ LEVEL_VS_MEDIAN, SURPRISE, CORR_DEVIATION }` selecting which of the three deviation definitions above applies to a given indicator class. Stored in the indicator registry.

**ECDF rank.** For a value `x` and a rolling history window `H` of length `N`, `ecdf_rank(x)` is the count of values `v ∈ H` with `|v| ≤ x`, using the standard left-continuous empirical CDF convention. Used by CLS-001 as `ecdf_rank(|deviation|) / N` to produce a severity value in `[0.0, 1.0]`. The empirical CDF is distribution-free: no assumption about the shape of the underlying distribution is required.

**History-window length (N).** Per-indicator-class integer `N ≥ 1` giving the count of most-recent observations retained for ECDF computation in CLS-001 and for dispersion measurement in CLS-009. Supplied by the indicator registry.

**Indicator class.** A grouping of signal symbols that share calibration parameters (`N`, `D`, `deviation_kind`). Calibration is per-class, not per-symbol: e.g. multiple equity volatility indices may be eligible to share one class, provided their `|deviation|` distributions pool without materially shifting the p95 rank. The empirical criterion for class membership is specified outside this document.

**Indicator registry.** Persistent configuration mapping each admissible signal symbol to an indicator class, and each indicator class to its calibration parameters `{ N, D, deviation_kind }`. The registry is the single source of truth for (a) which indicator symbols the classifier accepts on the normal CLS-001 path and (b) the per-class parameters used by CLS-001 and CLS-009. Ownership, file format, storage, and reload semantics are specified outside this document. Additions follow the operational approval workflow documented in the deployment specification. Symbols absent from the registry are handled per CLS-009.

**Minimum-informative dispersion (D).** Per-indicator-class non-negative real-valued threshold on the rolling interquartile range (IQR) of the history window. When `IQR(history) < D`, the history is treated as insufficiently informative for precise ECDF severity (a flat history would inflate modest moves to high percentiles), and the response falls under CLS-009. Supplied by the indicator registry.

**z-score.** Not used in this specification. Earlier classifier drafts used `z = (x − μ) / σ` as an intermediate severity measure; v2.3.2 replaces z-score-based severity with ECDF rank for all RULE_BASED strategies (MARKET_DATA, MACROECONOMIC, CROSS_ASSET_FLOW). z-score remains admissible inside source-reliability or temporal-relevance sub-components if a future indicator class needs it, but is not part of any requirement's normative formula.

---

## Block 2 — Revised CLS-001 [§5.2]

**Action:** Replace the existing CLS-001 block in §5.2 with the following text.

---

[CLS-001] [Must]

The system shall independently assess each signal for severity and certainty.

For signals in the source categories MARKET_DATA, MACROECONOMIC, and CROSS_ASSET_FLOW, severity shall be computed as:

> severity = ecdf_rank(|deviation|) / N

where `ecdf_rank`, `deviation`, and `N` are defined in §3, and `N` is supplied by the indicator registry (§3) for the signal's indicator class. Severity shall lie in `[0.0, 1.0]`.

For signals in the source category GEOPOLITICAL, severity shall be produced by the AI language model per CLS-003; the formula above does not apply.

Each assessment shall include a quantified measure of certainty composed of two independent dimensions:

- **source_reliability** — accuracy and trustworthiness of the source (e.g. fraction of expected values present in the history window).
- **temporal_relevance** — degree to which the market has not already priced the signal (e.g. exponential decay from last update, relative to the expected update frequency for the source).

Both dimensions shall be quantified independently in `[0.0, 1.0]` and combined as:

> certainty = source_reliability × temporal_relevance

When the signal's indicator class cannot be resolved (symbol absent from the indicator registry per §3) or when `IQR(history) < D` for the resolved class, the response shall be produced per CLS-009 in place of the ECDF formula above. Verification of those branches is specified in CLS-009.

Rationale: Ranking within the indicator's own history is distribution-free and regime-adaptive, eliminating the fixed-scale mapping previously used. The per-strategy `deviation` table (§3) makes severity paper-computable per indicator class, which resolves the self-validating-loop bug class identified in ADR-0002 by giving the acceptance suite an oracle independent of the implementation. Multiplicative combination of the two certainty dimensions preserves the property that either dimension approaching zero drives the composite toward zero — a thin-window signal or a stale signal cannot be rescued by the other dimension.

Verification:

1. Submit two signals with identical content but different source characteristics. Verify different certainty values (unchanged from v2.3.0).
2. Submit two signals with identical source characteristics but different time-since-last-update. Verify different temporal_relevance values and different certainty values.
3. For a reference event (VIX close on 2018-02-05 against the preceding 60-trading-day window of `|VIX − rolling_median|`), hand-compute `ecdf_rank(|deviation|) / N` per §3. Verify classifier output matches the hand-computed value within a configurable tolerance (default 0.001).

---

## Block 3 — New CLS-009 [§5.2, Must]

**Action:** Insert the following block in §5.2 after CLS-008 (the current last CLS-* requirement). Numbering as CLS-009 is the next free slot; CLS-007 (classifier-type extensibility) and CLS-008 (self-monitoring) are unchanged.

---

[CLS-009] [Must]

For signals in the source categories MARKET_DATA, MACROECONOMIC, and CROSS_ASSET_FLOW, the system shall emit a well-formed degraded-confidence response rather than a normal-path severity when either of the following statistical guard conditions holds:

1. The rolling interquartile range of the CLS-001 history window is below the minimum-informative dispersion `D` (§3) for the signal's indicator class. The response shall set `computed_metrics.dispersion_below_floor = true` and shall carry the severity computed per CLS-001.
2. The signal's symbol is absent from the indicator registry (§3). The response shall set `computed_metrics.unknown_indicator = true` and `score = 0.0`.

In either case, `certainty` shall be multiplied by a configurable degradation factor in the open interval (0, 1) documented in the deployment specification, and `classification_method` shall remain RULE_BASED. The response shall not be an error, shall not be silent, and shall not interpolate CLS-004 (which governs AI model response validation and does not apply here).

Rationale: Quiet-regime histories can inflate modest deviations to false-high ECDF ranks (the OVX pathology identified in ADR-0002); unregistered symbols indicate an operational gap that must be visible to downstream composite scoring rather than absorbed as a zero. Both failure modes are statistical, not AI-response failures, and require a dedicated requirement to preserve traceability from CLS-001's formula to its guarded paths. Downstream composite scoring (CLS-002) is able to deweight degraded signals via the certainty factor.

Verification:

1. Construct a history window whose rolling IQR is strictly less than a chosen `D`. Submit a signal. Verify the response carries `computed_metrics.dispersion_below_floor = true`, a degraded certainty, and `classification_method = RULE_BASED`.
2. Submit a signal whose symbol is absent from the registry. Verify `computed_metrics.unknown_indicator = true`, `score = 0.0`, and the degraded certainty envelope.
3. Verify that no CLS-004 fallback-activation log entry is produced by either case (CLS-009 and CLS-004 are disjoint paths).

---

## Block 4 — §11.12 amendment

**Action:** Replace the text of acceptance criterion 12 with the following.

---

12. For CLS-001, CLS-002, and CLS-006, API acceptance tests shall include hand-calculated expected outputs verified against the formulas defined in this specification.

---

## Not modified

- **§8.9 Insight 7 (Spec Precision as AI Pass Rates)** — Insight 7 categorizes examples (CLS-002, CLS-006 under (a) quantitative; CLS-003 under (b) qualitative; POS-001 under (c) structural). CLS-001 is not named in §8.9, so tightening CLS-001 with a formula does not require acknowledgment in §8.9.
- **§8.10 Experimental Variable Registry** — the Spec Precision row ("Mixed") is a summary of the SRS population and does not require edit when a single requirement is tightened.
- **SIG-001 wording** — preserved as-is. The "no fewer than four" phrasing underpins CT-01 (Add new signal source type) and CT-08 (New signal source adapter) and must not be narrowed. **No SIG-001.1 is introduced.** An earlier draft of this document proposed SIG-001.1 to anchor the registry; that was withdrawn after review — the registry is anchored in §3 Definitions instead, with CLS-001 and CLS-009 referencing it by term. Fewer numbered requirements, no SIG-001 scope creep.
- **CLS-004** — scope remains "AI model responses" per its first sentence. Not extended to statistical fallback paths (see CLS-009 instead).
- **CLS-003** — unchanged. Remains the GEOPOLITICAL unstructured-text classification requirement.

## Application procedure

1. Open `doc/PrimeScore-SRS-v2.3.1.docx` in Word.
2. Save as `doc/PrimeScore-SRS-v2.3.2.docx`.
3. Update the header revision table (front of document) with a v2.3.2 entry summarising the four changes (§3 Definitions additions; CLS-001 revision; CLS-009 addition; §11.12 amendment).
4. Paste **Block 1** entries into §3 Definitions alphabetically, using the existing §3 formatting style. Do not remove or reword existing definitions.
5. Paste **Block 2** over the existing CLS-001 body in §5.2. Preserve Word's paragraph styles ("Rationale:" and "Verification:" should adopt the same styles as surrounding requirements).
6. Paste **Block 3** in §5.2 after CLS-008. Assign the same heading style as CLS-008.
7. Apply **Block 4** to acceptance criterion 12 in §11.
8. Verify the document no longer contains any reference to SIG-001.1 (a search for "SIG-001.1" in the new v2.3.2 should return zero hits).
9. Regenerate the PDF via Word → Export → PDF.
10. Update repository cross-references (ADR-0002, LIMITATIONS.md, annex stub) to point at v2.3.2 rather than v2.3.1. Suggested cross-reference patches are in the Plan addendum, Step 8.

## References

- ADR-0002: [`apps/classification/doc/adr/0002-ecdf-severity-and-backtest-harness.md`](../../apps/classification/doc/adr/0002-ecdf-severity-and-backtest-harness.md)
- CLS-001 severity-formula annex stub: [`apps/classification/doc/adr/srs-annex-cls-001-severity-formula.md`](../../apps/classification/doc/adr/srs-annex-cls-001-severity-formula.md)
- SRS v2.3.1 source: `doc/PrimeScore-SRS-v2.3.1.docx` / `.pdf`
- Extracted text used to author this document: `doc/PrimeScore-SRS-v2.3.1.txt` (produced via `pdftotext -layout`)
