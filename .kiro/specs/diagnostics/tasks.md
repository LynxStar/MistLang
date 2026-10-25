# Implementation Plan

Carbonek project MIST is the only record of execution. This plan is the breakdown the specification implies, in dependency order, with the requirement each item satisfies; it is not a work queue and its boxes are not status. Packet numbers match `design.md`.

## Phase 0: pin and probe

- [ ] 0.1 Re-run the diagnostic inventories against the reissue revision and restate every count in `requirements.md` at that revision.
- [ ] 0.2 Probe a package with no asset for the target framework and record what the resolver reports today. _Lead for the owner; Requirement 15.7._
- [ ] 0.3 Probe the four conditions behind `UnknownAbstractMember`, `UnknownMember`, `OperatorMustHaveBody` and `DuplicateExtensionMember` and record what is reported today. _Leads for the owner; Requirement 15.6._
- [ ] 0.4 Count the nullable-flow Roslyn warnings in the emitted suite at the reissue revision. _Requirement 14.8._

## Phase 1: Common infrastructure

- [ ] 1.1 Registry types and index: `MistDiagnostic`, `DiagnosticNumber`, `DiagnosticCategory`, `PolicyClass`, `Slot`, the reflection index, the tombstone list, the Tier 1 validator. Convert every descriptor field with its old spelling as a temporary number and a positional template. _Requirements 1, 2, 3, 4.1 to 4.4._
- [ ] 1.2 Slot values, `DiagnosticFactory`, the renderer with positional and named substitution, the fallback template, the Tier 1 text-freeze table over every template. _Requirements 4.5, 7._
- [ ] 1.3 The reissue, one commit: apply the merge and the seven retirements, hold back the one descriptor without a producer, run the producer scan, mint numbers, write `crosswalk.md`, rewrite tests, book, specifications and CLI help through the crosswalk, add the supersession notes to the two old specifications, rewrite `full-accessibility-model`'s citations. _Requirements 1, 4.6, 15, 16._
- [ ] 1.4 `Location`, `RelatedLocation`, `Frame`, `SourceIdentity`; the resolver returns frames; bounds with checked arithmetic; the Tier 1 location table. _Requirement 8.1 to 8.10._
- [ ] 1.5 Operation scope and ordinals; `Withheld` with commit, discard and replay; delete `Contains`; Tier 1 tests for nesting, exception restore and ordinal preservation. _Requirement 9.1 to 9.6._
- [ ] 1.6 `DiagnosticPolicy`, the `[diagnostics]` table, `SeverityPolicy.Apply` at admission, every gate on effective severity, the Stage 5 registration hook for `Optional`; Tier 1 over every combination. _Requirement 6._
- [ ] 1.7 `DiagnosticOutput`; `CompileFeedback` over it; the JSON writer with fixed key order; `--diagnostic-file`; Tier 1 conventions; Tier 4 command-output seams. _Requirements 9.7, 9.8, 10, 11._
- [ ] 1.8 The six CLI controls, applicability per command, bad-selector `CFG` findings, help text, README and book sentences, the steering text correction. _Requirement 12._
- [ ] 1.9 The host boundary in `Main`; one Tier 2 test with an injected throwing command. _Requirement 13.4._

## Phase 2: producers

Frontend, after 1.4 and 1.5:

- [ ] 2.1 Source anchors: parser origins before validation; lexer span and unexpected scalar as slots; the parser recovery sites; constraint and duplicate-type anchors. _Requirements 8.11, 8.12._
- [ ] 2.2 Declaration, type, name, conversion and variadic families to typed slots; `ScopeResolver`'s failure protocol intact. _Requirement 7._
- [ ] 2.3 Deferred findings as replay; trial sites as commit or discard; `ExpressionFailures.Resolve` as replay. _Requirement 9.5._
- [ ] 2.4 Stage 5 rules to typed slots; `ReportAtProject` to the `Project` location; related locations instead of text in `DeclarationShapeAnalyzer` and `ContextCalls`; suppression untouched. _Requirements 7.8, 8.12._
- [ ] 2.5 Clone wrappers reference the cause ordinal; containment by `SourceIdentity`. _Requirements 8.10, 9.6._
- [ ] 2.6 Classify the throw sites; `UnsupportedStatement` and `UnresolvedExpression` keep source until the owning stage reports. _Requirement 14._

Backend, after 1.4 and 1.5:

- [ ] 2.7 The 35 bag sites to typed slots; fail-closed `ICE` findings for the emitter fallbacks; source-less refusals to explicit locations. _Requirement 14.1, 14.2._
- [ ] 2.8 The Roslyn adapter; `GeneratedLocation`; the `native` array; delete the text mapper and its property test. _Requirements 5.5, 14.3 to 14.5._
- [ ] 2.9 Typed withholding reasons retained to inspection; the use-site frame; no eager errors. _Requirement 14.7._
- [ ] 2.10 Metadata catches keep their recovery and record typed causes where a finding exists. _Requirement 7.4._
- [ ] 2.11 Backend producer coverage pushed to its owner tier; the fixtures that build Standard replaced by direct model setup. _Requirement 17._

Driver and CLI, after 1.6 and 1.7 (2.12 may start earlier on current descriptors):

- [ ] 2.12 `inspect` and `restore` write every manifest occurrence regardless of configuration nullness and stop on an error; `restore` fails on an error-bearing input. _Requirements 9.7, 10.5._
- [ ] 2.13 The sixteen argument failures, the seven installer failures and the raw operational failures become descriptors. _Requirement 5.6._
- [ ] 2.14 Lex mode, `TypeInspector` and `restore` drain every severity; the test-framework warning is admitted before the summary. _Requirements 5.4, 10.5._
- [ ] 2.15 The twenty direct driver constructions go through the factory with full dynamic detail. _Requirement 5.3._
- [ ] 2.16 The NuGet adapter; child output left as child output. _Requirements 7.11, 11.8._
- [ ] 2.17 Expected operational failures normalized; benign cache and cleanup fallbacks stay silent. _Requirement 13._
- [ ] 2.18 Copied predicate tests replaced by production decisions with direct inputs; tier traits on everything touched. _Requirement 17._

Storm boundary, conditional:

- [ ] 2.19 Only if 2.1 cannot meet Requirement 8 with committed runtime entries: a separately pinned Storm change adding span and kind to runtime entries, keeping the current overloads and rollback.

## Phase 3: closure

- [ ] 3.1 Inventory scan: zero `new Diagnostic(` and `new DiagnosticId(` outside the registry and factory; every descriptor has a producer; no positional template remains; the validator requires named slots. _Requirements 4.6, 5.3._
- [ ] 3.2 The catalog generator script, the committed page, the Tier 1 staleness check. _Requirement 18.1._
- [ ] 3.3 Every touched test carries exactly one tier trait; no new Tier 5 or Tier 6. _Requirement 17._
- [ ] 3.4 Zero warnings, `dotnet format Mist.slnx --verify-no-changes`, full ledger including `AllFeaturesV2`. _Requirement 17.7._
