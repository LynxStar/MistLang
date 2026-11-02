# Result foundation

This demo exercises target-directed construction, explicit projections and
closed factories, same-type and void alternatives, optional successes, nested
Results, correlated inspection, error unions, explicit error mapping, direct
error return destinations, lambdas, local functions and async Result returns.
Counters expose evaluation count and skipped continuation work. Context output
exposes restoration across propagation. Resource IDs expose ownership transfers
and exactly one drop per acquired token, including fresh and nested carriers
and owned error returns.

The output banked in `AllFeaturesV2` is **V2-ONLY**: this feature is implemented
by the standard pipeline after the classic compiler's retirement. The lines
are captured from this demo and checked against its branches, counters, context
scopes and token IDs. Refusals are covered by the direct construction, binding,
analysis, flow and lowering tests under `Test/Mist.Tests`.
