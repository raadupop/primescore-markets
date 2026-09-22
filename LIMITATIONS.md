# Platform design limitations

The implemented classifier's limits live in [its own inventory](apps/classification/LIMITATIONS.md). The .NET engine is not implemented.

- **Composite range:** CLS-002 discounts its denominator for source dropout, so a conviction of 1 with discount 0.5 produces 2 despite its declared signed unit range. The max-per-category selection also does not implement the opposing-signal cancellation promised by its rationale. Resolve the aggregation contract before implementing it; the replay demo uses explicitly named single-event conviction instead.
- **Volatility interpretation:** CLS-006 scales observed IV by a configurable multiplier and composite. There is no independent fair-value estimate or validated forecasting model. Negative composite with sensitivity above 1 can produce negative IV; the demo bounds its multiplier to [0, 1]. Calibrate against out-of-sample outcomes before claiming predictive validity.
- **External API validation gap:** the audit found `baseline_value` required but undefined in the cross-asset payload. The property is now defined, and both OpenAPI files are validated by the repository gate. Structural validity does not resolve the statistical ambiguities above.
- **Public data rights:** historical fixtures include provider-sourced observations. No proposed source-code licence grants rights in provider data or archived third-party documents.
