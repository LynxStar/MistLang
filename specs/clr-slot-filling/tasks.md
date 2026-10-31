# Implementation Plan

Carbonek project MIST is the record of this work; this list is the spec's own breakdown, not a queue.
Every task cites the requirement it discharges and the tier its tests declare. Measure the five
"before" rows of the requirements introduction with the CLI built from the starting revision before
touching anything, and keep the outputs for the completion report.

- [ ] 1. Model: `NativeSlotKind` and `SlotKind` on members
  - [ ] 1.1 Add `NativeSlotKind { None, Required, Defaulted, Sealed }` under `ProgramModel/Model/Members/`.
  - [ ] 1.2 Add `SlotKind { get; init; }` to `Method`, `Property` and `Indexer`, default `None`, beside `HasNativeBody`.
  - _Requirements: 1.4_

- [ ] 2. Projection: record the kind, drop the method sentinels
  - [ ] 2.1 `MethodProjector.Project` sets `SlotKind` from `IsAbstract`, `IsVirtual`, `IsFinal` and the declaring type's `IsInterface` per the design table.
  - [ ] 2.2 `PropertyProjector` and `IndexerProjector` set it from `GetMethod ?? SetMethod`.
  - [ ] 2.3 Delete `SentinelAttributes.ApplyMethodAttributes` and the `Virtual`, `Override`, `Abstract`-on-method and `Sealed`-on-method sentinels; keep `ApplyTypeAttributes`.
  - [ ] 2.4 Fixture `Test/Mist.Tests.InteropFixtures/SlotShapes.cs` as the design lists.
  - [ ] 2.5 Tier 2 `AmbientTypeProviderV2SlotKindTests`: every row of the design's projection section, including `IGreeter` members as `None` and `System.Object.ToString` as `Defaulted`.
  - _Requirements: 1.1, 1.2, 1.3, 1.5, 6.1_

- [ ] 3. Resolution: `NativeSlotFills`
  - [ ] 3.1 `OverloadSignature.RepeatsProjected`, lifting the earlier member's declaration-node guard; `Repeats` unchanged.
  - [ ] 3.2 `ProgramAnalysis/NativeSlotFills.cs` with `Of`, `Collisions` and `Unfilled`, walking the chain as `CheckRedeclarations` does and classifying by the nearest projected declaration.
  - [ ] 3.3 Tier 1 `NativeSlotFillsTests` over `HandBuiltModel`: the twelve rows the design lists.
  - _Requirements: 2.1 to 2.7_

- [ ] 4. Stage 5: refusals and the unfilled-slot finding
  - [ ] 4.1 Three descriptors in `ProgramAnalysisDiagnostics` with the design's texts, minted `MIST0459` to `MIST0461`.
  - [ ] 4.2 `NativeSlotAnalyzer` (`native-slot-filling`) registered in `ProgramAnalysisRunner` after `ConformanceAnalyzer`.
  - [ ] 4.3 `DeclarationShapeAnalyzer.CheckRedeclarations`: compare against projected ancestors through `RepeatsProjected`; skip members `NativeSlotFills.Of` reports as fills.
  - [ ] 4.4 Tier 1 `NativeSlotAnalyzerTests` and redeclaration rows: each diagnostic once at the right node, the two silences, the unresolved-base silence.
  - _Requirements: 3.1 to 3.4, 4.1 to 4.6, 2.5_

- [ ] 5. Stage 6: `override`
  - [ ] 5.1 `NativeSlotFills.Enter(model)` scope beside `NativeSlotNullability.Enter`, entered in `CSharpEmitterV2` before writing.
  - [ ] 5.2 `MemberModifierEmitter.Emit` takes `fills`; `MethodEmitter`, `PropertyEmitter`, `IndexerEmitter` pass it; `sealed override` where `Modifiers.IsSealed`.
  - [ ] 5.3 `MethodEmitter.HidesObjectToString` yields to a fill.
  - [ ] 5.4 `NativeSlotNullability.Answering` also collects fill parameters whose slot parameter `ClrDeclaresNullable`.
  - [ ] 5.5 Tier 1 `MethodEmitterOverrideTests` and property/indexer rows as the design lists.
  - _Requirements: 5.1 to 5.5, 6.2, 6.3_

- [ ] 6. Sample and baseline
  - [ ] 6.1 `samples/ClrSlotFilling` with the five shapes of Requirement 7.1, no redeclaration in it.
  - [ ] 6.2 Reference it from `samples/AllFeaturesV2/mist.toml`, call its `RunDemo()` from `samples/AllFeaturesV2/Program.mist`, add its lines to `samples/__baselines__/AllFeaturesV2.out`, record V2-only provenance in `samples/AGENTS.md`.
  - [ ] 6.3 Run the full `AllFeaturesV2` baseline and read every existing sample's output for the two behaviour changes the design names; a changed line in another sample is a finding to report, not a baseline to edit.
  - _Requirements: 7.1, 7.2_

- [ ] 7. Specs and records
  - [ ] 7.1 abstract-type-unification `requirements.md`: a note under Requirement 25 and 26 that this specification delivers 25.2 to 25.4 and 26.5 and that 26.2 to 26.4's `new` keyword is superseded by `MIST0177`.
  - [ ] 7.2 Book: the Abstract Types or Interop chapter gains a short section on filling a CLR base's slot, with a compiled snippet, under `book/AGENTS.md`'s snippet policy.
  - _Requirements: 1 to 6_

- [ ] 8. Verification for the completion report
  - [ ] 8.1 The five "before" probes rerun on the delivered compiler: `inserts=2 count=0`; `MIST0459` naming `GetKeyForItem`; `protected override void BuildRenderTree` in the emitted component; `True` through the `Attribute` variable; `MIST0177` for `Add`.
  - [ ] 8.2 `dotnet build Mist.slnx` zero warnings; `dotnet format Mist.slnx --verify-no-changes`; `git diff --check`.
  - [ ] 8.3 Focused ledgers through `tools/test-ledger/run.sh Test/Mist.Tests --filter` for each new test class, then the full ledger at the delivery checkpoint; ledger paths in the report.
