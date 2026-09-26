# Basel III/IV Frameworks — Note for PrimeScore AI Relevance

Short reference compiled while exploring overlap between a financial-services
JD (Risk Management, Securities Lending, Collateral/Margin, RWA, CCAR,
Basel III/IV, credit/counterparty risk) and PrimeScore AI's event-driven options
pipeline. The methodologies closest to PrimeScore AI's measurement → calibration →
sizing loop are flagged below.

## Scope of "Basel III/IV"

Basel III/IV is a bundle of frameworks, not a single rulebook. Basel IV is
informal shorthand for the 2017 finalisation plus FRTB.

### Market Risk
- **FRTB (Fundamental Review of the Trading Book)** — Basel IV market-risk overhaul.
  - **SBA (Sensitivity-Based Approach)** — standardised: delta / vega /
    curvature sensitivities per risk factor, aggregated with prescribed
    correlations across 7 risk classes (GIRR, CSR, equity, commodity, FX, …).
  - **IMA (Internal Models Approach)** — bank's own model, gated by
    **P&L Attribution (PLA)** and **backtesting** tests per desk.
    Replaces VaR with **Expected Shortfall at 97.5%**, adds **liquidity
    horizons** per risk factor, plus a **Default Risk Charge (DRC)**.
  - **Trading-book / banking-book boundary** — strict classification to
    prevent regulatory arbitrage.

### Credit Risk
- **SA-CR** — standardised, prescribed risk weights by exposure class.
- **IRB (Foundation / Advanced)** — internal PD, LGD, EAD estimates.
  Basel IV introduces **input floors** (e.g. PD ≥ 0.05%) and an
  **output floor**: RWA cannot fall below 72.5% of the standardised result.

### Counterparty Credit Risk (CCR)
- **SA-CCR** — standardised EAD for derivatives, replaces CEM/SM.
- **IMM** — internal PFE/EAD models.
- **CVA Risk Framework** — capital for credit valuation adjustment
  (SA-CVA, BA-CVA).

### Operational Risk
- **SMA (Standardised Measurement Approach)** — single Basel IV method
  combining a Business Indicator with an Internal Loss Multiplier;
  replaces AMA and the older standardised/basic-indicator approaches.

### Capital Structure & Ratios
- **CET1 / Tier 1 / Tier 2** capital definitions.
- **Minimum ratios** — CET1 4.5%, Tier 1 6%, Total 8%, plus
  **Capital Conservation Buffer 2.5%**, **Countercyclical Buffer**,
  **G-SIB / D-SIB surcharges**.
- **Leverage Ratio** — Tier 1 / total exposure ≥ 3% (higher for G-SIBs).
- **Output Floor (72.5%)** — marquee Basel IV constraint capping internal-model optimism.

### Liquidity
- **LCR** — 30-day stressed outflows covered by HQLA.
- **NSFR** — 1-year structural funding adequacy.

### Governance Pillars
- **Pillar 1** — minimum capital requirements.
- **Pillar 2** — supervisory review, ICAAP, ILAAP.
- **Pillar 3** — public disclosure.

## PrimeScore AI-Relevant Subset

Most of Basel is banking-book / institutional plumbing and does not transfer.
The pieces with real conceptual overlap to PrimeScore AI's pipeline
(event → classification → severity → exploitability → sizing):

1. **FRTB — SBA mechanics and ES vs VaR.** Sensitivity-based aggregation
   under prescribed correlations is the same shape as severity-driven sizing
   under a calibrated distribution. Expected Shortfall at 97.5% is a tail-aware
   alternative to the percentile-rank severity mapping currently used for
   RULE_BASED strategies (see ADR-0001 Failure 2 and obs 143).
2. **SA-CCR / PFE intuition.** "Forward-looking worst-case exposure" is the
   same primitive as exploitability under uncertainty.
3. **Output Floor philosophy.** Capping internal-model optimism with a
   standardised reference mirrors the harness oracle-contract principle:
   the validating signal must be independent of the implementation
   (cf. Failure 2 tautology, fixtures derived from implementation output).
4. **CCAR-style stress methodology** (sibling to Basel stress tests) —
   prescribed adversarial scenarios applied uniformly. Imports cleanly as
   harness fixture discipline.

## Lower-Priority for PrimeScore AI
RWA accounting mechanics, credit risk (PD/LGD/EAD for lending), operational-risk
SMA, LCR/NSFR liquidity ratios, capital-structure tiers. Useful vocabulary for
the JD, not load-bearing for the system.

## Suggested Study Order
FRTB SBA → Expected Shortfall and backtesting (Kupiec POF, Christoffersen) →
SA-CCR / PFE → Output Floor concept → CCAR stress methodology.
