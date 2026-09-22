# Shared indicator configuration

[registry.yaml](registry.yaml) maps symbols to indicator classes, history lengths (`N`, `N_L`), deviation methods, fallback families, cadences and bootstrap providers. The classifier validates it at startup; the .NET consumer is planned.

- `N` controls the short-window degeneracy check; `N_L` controls ECDF history where available.
- Classes with `N_L: null` select their declared parametric fallback.
- Only verified, implemented provider mappings can bootstrap. Registered symbols are not a claim that their data provider works.
- Runtime histories live in memory per symbol. Configuration changes require restart.

Calibration and new class/symbol decisions require trader review; do not tune registry values to make acceptance anchors pass. [Decision](../apps/classification/doc/adr/0002-ecdf-severity-and-backtest-harness.md) · [Validation limits](../apps/classification/LIMITATIONS.md)
