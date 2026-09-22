# Demo limitations

## Degraded classifier responses (Case B, 22 September 2026)

External review found the adapter treated positive certainty as sufficient for a scenario, overlooking explicit degradation markers. Degenerate windows and rejected fits can retain nonzero certainty under [classifier ADR-0004](../classification/doc/adr/0004-signed-severity-and-fit-rejection.md).

**Control:** the adapter withholds scenarios when `window_degenerate`, `fit_rejected` or `unknown_indicator` is `true`, as well as for zero certainty or deliberately incomplete replay history. The [HTTP response regression](../../tests/demo/test_replay.py) supplies each marker with positive certainty and checks `status=degraded`, `scenario=null`; explicit false markers retain normal behavior.

**Remaining limit:** `computed_metrics` is an open contract field. New classifier degradation markers require an adapter update and consumer regression; the interface has no stable top-level degradation discriminator. Other demo scope and data limits live in [DEMO.md](../../docs/DEMO.md#meaning-and-data-limits).
