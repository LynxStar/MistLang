# Requirements Document

## Introduction

This document is the contract for every diagnostic the Mist compiler produces: how a condition is identified, categorized and given a severity; how a report carries its arguments and its source position; how reports are collected, ordered and delivered; how they appear as text and as a machine-readable document; which command-line controls act on them; what the exit code and the outcome mean; and which conditions that are silent today become visible. It also specifies the one-time reissue of every public code and names the clauses of earlier specifications it supersedes.

It records eleven owner decisions taken on 2026-10-24 after a read-only review of the diagnostic system. The review was pinned at revision `d9fe430f92cde89735a1c5137d1d737ee7c13b82`; the measurements cited here were retaken at `af6a1f70352869d52958c7979274fb5ca718208a` with the Storm submodule at `86dfd33895ba15ec7a3187e00340b236e666ab19`. At that revision the registry holds 402 public codes declared by 414 descriptors; nine codes are shared by 21 declarations; eight descriptors have no production reporting site; six descriptors default to Warning and one to Info, and one further warning is built inline in the CLI; fourteen gates in the driver, the pipeline and the CLI read a default severity; and the emitted C# of the full sample suite compiles with 32 distinct Roslyn warning sites, 26 of them nullable-flow warnings about the emitted code rather than the Mist program.

The reissue revision is `97569de18582fa91b2dafdfec0e3c8173ee93235` on `refactor/diagnostics`: the last registry-bearing checkpoint before the reissue commit, whose registry the one-commit reissue is computed from. The reissue commit itself is recorded at the head of `crosswalk.md`. Every count above was retaken at the reissue revision, with the Storm submodule still at `86dfd33895ba15ec7a3187e00340b236e666ab19`: the registry holds 418 distinct codes declared by 430 `MistDiagnostic` fields (the 414 fields of `af6a1f70`, the eight that `0df844d0` added for C# project and assembly references, and eight added by Phase 1 of the migration: the test-framework warning that was built inline in the CLI, two `ICE` descriptors for the factory's argument and span breaches, and five `CFG` descriptors for the `[diagnostics]` table); the same nine codes are shared by 21 declarations; eight descriptors have no production reporting site, which are the seven retired by Requirement 15.6 and the one held back by 15.7; seven descriptors default to Warning and one to Info, and no warning is built inline anywhere; twenty gates in the driver, the pipeline, the backend and the CLI read the effective severity and none reads a default; and the emitted C# of the full sample suite compiles with 28 distinct Roslyn warning sites, 22 of them nullable-flow warnings about the emitted code rather than the Mist program.

The decisions in one sentence each:

- **D01.** Public codes are `MIST` followed by four digits; every current code is reissued once; the compiler identifies a diagnostic by a typed descriptor and never by a string.
- **D02.** Every number carries exactly one of thirteen closed categories, independent of severity and of the producing stage.
- **D03.** Severity stays Info, Warning and Error; every number has a policy class; effective severity is computed once before every gate; display filters never change it.
- **D04.** The CLI gains a format switch, policy overrides and a minimum-severity filter that can never hide an error.
- **D05.** Arguments are typed slots named in the template; one renderer produces text; message wording is frozen through the reissue.
- **D06.** A location is a closed union with explicit absence reasons; the origin chain and typed related locations are kept on the occurrence.
- **D07.** Every admitted occurrence has an ordinal; order is append order; nothing is deduplicated by value; withheld reports commit, discard or replay explicitly.
- **D08.** The machine-readable output is one buffered, versioned JSON document on stderr or in a file, carrying every admitted finding.
- **D09.** The exit-code matrix is unchanged; the distinct outcome lives in the document; Main gains a host boundary.
- **D10.** Compiler-defect fallbacks fail closed; Roslyn warnings are never forwarded as Mist warnings; ambient projection stays quiet.
- **D11.** One template merge, seven retirements, one code held back; a number is minted only for a descriptor with a producer.

The twelfth question the review raised, how work enters the backlog, is not part of this specification. Carbonek project MIST is the only record of execution, and nothing in this document or its companions is a work queue.

## Glossary

- **Registry**: The set of `public static readonly` descriptor fields under `Source/Mist/Common/Diagnostics/`, plus the index built from them once per process for validation, the catalog and JSON.
- **Descriptor**: One registry entry; the C# type is named `MistDiagnostic`. It carries a Number, a Title, a Message_Template, a Description, a Category, a Policy_Class, a Default_Severity and a Slot schema.
- **Number**: The public code of a Descriptor, `MIST` followed by four decimal digits from `0001` to `9999`. The Number is the identity.
- **Title**: A short fixed noun phrase for a Descriptor, such as `Not callable`.
- **Message_Template**: The text of a Descriptor with named placeholders, one per Slot, such as `'{name}' is not a method, and its type '{type}' is not a function type, so it cannot be called.`
- **Description**: A fixed paragraph on a Descriptor saying what the condition means and how to fix it. It appears in the catalog and in JSON, never in the text line.
- **Category**: One of thirteen closed tokens describing what a condition is about. Defined in Requirement 3.
- **Policy_Class**: One of `Fixed`, `Promotable` and `Optional`, saying how project policy may change a Descriptor's severity or applicability. Defined in Requirement 6.
- **Default_Severity**: The severity a Descriptor declares: `Info`, `Warning` or `Error`.
- **Effective_Severity**: The severity an Occurrence has after project policy is applied, together with the reason for any change.
- **Slot**: A named, typed argument position of a Descriptor. Slot kinds are defined in Requirement 7.
- **Occurrence**: One admitted report of a Descriptor: its Ordinal, Slot values, Location, Related_Locations, Frames, cause, producing stage, Default_Severity and Effective_Severity. The C# type is named `Diagnostic`.
- **Ordinal**: A per-operation monotonic integer assigned to an Occurrence at admission.
- **Location**: One of the closed set of position kinds defined in Requirement 8.
- **Related_Location**: A Location with a role, attached to an Occurrence.
- **Frame**: A unit of provenance attached to an Occurrence: a lowering or synthesis reason, or an operation context such as project, reference chain or package.
- **Provider_Code**: A diagnostic identity owned by another tool: Roslyn `CSnnnn`, Storm `STORMnnnn`, NuGet `NUnnnn`. A Provider_Code is never a Number.
- **Factory**: The one validated construction path for an Occurrence from a Descriptor and Slot values.
- **Renderer**: The one component that produces an Occurrence's message text from its Descriptor and Slot values.
- **Bag**: `DiagnosticBag`, the stage-local collection of Occurrences.
- **Output_Owner**: The one component of a command that renders admitted Occurrences to text or to the JSON document and never renders an Ordinal twice.
- **Operation**: One CLI invocation.
- **Outcome**: One of `Succeeded`, `Failed`, `UsageError`, `InternalFailure` and `Cancelled`, recorded in the JSON document.
- **Reissue**: The one-time replacement of every current public code by a Number.
- **Crosswalk**: The committed table mapping every current public code and descriptor field to its Number, merge or retirement.
- **Tombstone**: A registry record that a Number was issued and retired and may not be reused.
- **Fixture**: A diagnostic a test constructs for its own purposes. A Fixture is never a Descriptor.
- **ICE**: A compiler invariant breach, the category of an Occurrence that a valid program can never cause.

## Requirements

### Requirement 1: Numbering and identity

**User Story:** As a Mist developer, I want every diagnostic to have a short stable code I can search for and a compiler that cannot misspell it, so that documentation, tooling and tests all agree on what a condition is called.

#### Acceptance Criteria

1. THE Registry SHALL identify each Descriptor by a Number of the form `MIST` followed by four decimal digits from `0001` to `9999`.
2. THE Number SHALL carry no meaning: no digit encodes a category, a stage, a severity or an area.
3. THE Registry SHALL hold exactly one Descriptor per Message_Template, so that no two Descriptors share a Number and no Number has two templates.
4. WHEN a Number is allocated, THE Registry SHALL allocate the lowest unused value above every Number ever issued, including Tombstones.
5. THE Registry SHALL never reuse a Number: a retired Number becomes a Tombstone that records the Number, the Title it had and the revision at which it was retired.
6. THE compiler SHALL identify a diagnostic by a `MistDiagnostic` value and SHALL expose no public API that constructs a `Diagnostic`, a `DiagnosticId` or a `MistDiagnostic` from a string.
7. THE Factory SHALL be the only construction path for an Occurrence; `DiagnosticBag.Add(Diagnostic)` SHALL remain only for forwarding an Occurrence the Factory already built.
8. A Provider_Code SHALL be carried by its own type, qualified by provider, and SHALL never be converted to a Number or a `DiagnosticId`.
9. Tests SHALL obtain a Fixture through an internal test-only factory, and a Fixture SHALL never appear in the Registry or the catalog.
10. THE Registry SHALL accept no alias for a pre-reissue code: after the Reissue, the compiler SHALL neither emit nor recognize any `MIST_...` spelling.

### Requirement 2: Registry entry contents

**User Story:** As a compiler maintainer, I want every static fact about a diagnostic declared in one place, so that the catalog, the JSON document and the renderer cannot disagree.

#### Acceptance Criteria

1. EACH Descriptor SHALL declare a Number, a Title, a Message_Template, a Description, a Category, a Policy_Class, a Default_Severity and a Slot schema.
2. THE Title SHALL be a noun phrase of at most six words in sentence case with no terminal punctuation.
3. THE Description SHALL be one paragraph that states the condition and the remedy and SHALL not repeat the Message_Template.
4. A Descriptor SHALL declare zero or more related Numbers; a related Number is catalog metadata and never changes identity, severity or gating.
5. A Descriptor SHALL NOT declare a pipeline stage; the producing stage is a property of each Occurrence.
6. THE Registry index SHALL be built once per process from the descriptor fields by reflection, and the catalog, the JSON document and the Renderer SHALL read only that index.
7. THE Registry SHALL be organized in whatever static classes and files its maintainers find readable; no class name is normative, and a Descriptor shared by several areas is declared once wherever it fits.

### Requirement 3: Categories

**User Story:** As a Mist developer, I want diagnostics grouped by what is wrong rather than by which compiler class noticed, so that the catalog and any filter sort by meaning.

#### Acceptance Criteria

1. THE Category SHALL be one of the following thirteen tokens and no other: `SYN` syntax; `NAM` name resolution; `TYP` types and constraints; `DEC` declaration contracts; `FLW` control and data flow; `OWN` ownership and lifetime; `EFF` effects and context; `INT` interoperability; `BCK` backend capability; `CFG` project configuration; `DEP` dependency resolution; `IOX` tool and I/O operation; `ICE` compiler invariant.
2. EACH Descriptor SHALL declare exactly one Category.
3. THE Category SHALL NOT be derived from the producing stage, from the registry class or file that holds the Descriptor, or from the Number.
4. A Descriptor that wraps a Provider_Code SHALL take the Category of the operation it wraps: `BCK` for Roslyn, `DEP` for NuGet, `SYN` for Storm runtime parse and lex entries.
5. THE `ICE` Category SHALL be reserved for conditions no valid program can cause; an `ICE` Occurrence SHALL carry a cause and SHALL never be phrased as a source error.
6. Changing a Descriptor's Category SHALL change nothing but catalog grouping, the JSON field and filter membership.

### Requirement 4: Registry validation

**User Story:** As a compiler maintainer, I want a registry mistake to fail a cheap test rather than reach a user, so that the registry stays trustworthy as it grows.

#### Acceptance Criteria

1. A Tier 1 test over the Registry index SHALL fail when two Descriptors share a Number, when a Number is malformed, or when a Number is a Tombstone.
2. A Tier 1 test SHALL fail when a Descriptor lacks a Title, a Description, a Category or a Policy_Class, or when its Category is not one of the thirteen tokens.
3. A Tier 1 test SHALL fail when a Message_Template names a placeholder that is not a Slot, when a Slot has no placeholder, or when an escaped brace does not parse as a composite-format escape.
4. A Tier 1 test SHALL fail when a Promotable or Optional Descriptor has Default_Severity `Error`, or when an `ICE` Descriptor has a Default_Severity other than `Error`.
5. A Tier 1 test SHALL render every Descriptor through the Renderer with representative Slot values and fail on any exception.
6. At the Reissue revision, a one-time scan SHALL verify that every Descriptor has at least one production reporting site, and a Descriptor with none SHALL not receive a Number.

### Requirement 5: Typed construction and the end of raw strings

**User Story:** As a compiler developer, I want the compiler to refuse a diagnostic that is not in the registry, so that the problem of inline codes cannot come back.

#### Acceptance Criteria

1. THE `DiagnosticId` type SHALL have no public constructor; Numbers SHALL be minted only by registry declarations.
2. THE `Diagnostic` type SHALL have no public constructor; the Factory SHALL build it from a `MistDiagnostic`, a Location and Slot values.
3. AFTER migration, THE production tree SHALL contain zero occurrences of `new Diagnostic(` and zero occurrences of `new DiagnosticId(` outside the Registry declarations and the Factory.
4. THE twenty direct constructions in `Source/Mist/Driver`, the one in `Source/Mist.CLI/Program.cs` for the test-framework warning, the one in `Source/Mist/ProgramAnalysis/AnalysisContext.cs` and the one in the Roslyn text mapper SHALL be migrated or deleted; the test-framework warning SHALL become a Descriptor and SHALL be admitted before the summary is printed.
5. THE Roslyn text mapper that parses formatted lines back into diagnostics SHALL be deleted together with its property test; the JSON document is the only round trip the compiler promises.
6. THE sixteen uncoded CLI argument failures and the seven uncoded steering-installer failures SHALL become Descriptors in Category `CFG` or `IOX` and SHALL be rendered through the Output_Owner.

### Requirement 6: Severity and policy

**User Story:** As a Mist developer, I want to promote warnings to errors for my project and to switch optional rules on and off, without ever being able to hide an error, so that policy serves me and cannot let an invalid program through.

#### Acceptance Criteria

1. THE severity set SHALL remain `Info`, `Warning` and `Error`; there SHALL be no `Fatal`, `Hidden` or `None` severity.
2. EACH Descriptor SHALL declare one Policy_Class: `Fixed` for a violated language or configuration contract, `Promotable` for a Warning or Info that policy may raise to Error, or `Optional` for a rule policy may disable or promote, with a declared default of enabled or disabled.
3. A `Fixed` Descriptor SHALL never be demoted, disabled or hidden by any policy or control.
4. A `Promotable` Descriptor SHALL never be silenced; policy may only raise its severity.
5. Disabling an `Optional` Descriptor SHALL mean its rule does not run; it SHALL NOT mean its Occurrences are collected and hidden.
6. THE compiler SHALL compute Effective_Severity exactly once per admitted Occurrence from the Default_Severity and the project policy, and SHALL record the reason for any change on the Occurrence.
7. EVERY gate that decides whether compilation continues, whether an artifact is written or whether a command fails SHALL read Effective_Severity and SHALL never read a display filter.
8. A project SHALL declare policy in a `[diagnostics]` table in `mist.toml` with the keys `warnings_as_errors`, a boolean or a list of Numbers; `disable`, a list of Numbers; and `enable`, a list of Numbers.
9. A `disable` or `enable` entry naming a `Fixed` or `Promotable` Number, or any entry naming an unknown Number, SHALL produce one `CFG` Occurrence before compilation and SHALL fail the command.
10. THE command-line overrides of Requirement 12 SHALL take precedence over the `[diagnostics]` table for that Operation.
11. An Occurrence of Effective_Severity `Info` SHALL never fail a command; one of `Warning` SHALL fail a command only when promoted; one of `Error` SHALL fail the command whether or not it is visible.
12. Per-source suppression directives SHALL NOT be introduced by this specification.

### Requirement 7: Arguments and rendering

**User Story:** As a tool author, I want the facts a diagnostic is about to be available as typed values, and as a Mist developer I want the message I read today to read the same after the migration.

#### Acceptance Criteria

1. EACH Descriptor SHALL declare its Slots by name and kind, and its Message_Template SHALL refer to each Slot by name; a Slot MAY be referenced more than once.
2. A Slot kind SHALL be one of: `Name`, `Type`, `Symbol`, `Path`, `Integer`, `Boolean`, `Enum`, `Text`, `Sequence` of a kind, and `Optional` of a kind.
3. A `Type` or `Symbol` Slot value SHALL be a snapshot taken at report time, holding the identity and the Mist spelling, so that later model mutation cannot change what was reported.
4. No live AST node, model object or exception object SHALL be stored in an Occurrence.
5. THE Factory SHALL validate the supplied Slot values against the schema; a breach SHALL produce one `ICE` Occurrence rendered through a fixed fallback template, and the Renderer SHALL never throw and SHALL never recurse into its own failure.
6. THE Renderer SHALL be the only component that produces message text, and SHALL format under the invariant culture.
7. Missing, null and empty Slot values SHALL be distinct, and a `Sequence` SHALL preserve source order where the condition has one and a stable ordinal order otherwise.
8. A related declaration's position, a call site's position, a candidate list, a project path and a cause finding SHALL be carried in Related_Locations, Frames, `Sequence` Slots and the cause reference respectively, never serialized into a `Text` Slot.
9. THROUGHOUT the Reissue, THE rendered message text of every existing Descriptor SHALL be identical, character for character, to the text produced at `af6a1f70`, which is the text at the reissue revision; a Tier 1 table built from the current templates SHALL pin this before any producer migrates.
10. A wording change SHALL land only as a separate reviewed change that updates its own assertions on purpose; no change SHALL be accepted by bulk-accepting altered snapshots.
11. A Descriptor that wraps a Provider_Code or a Storm runtime entry SHALL have a single `Text` Slot for the upstream message and SHALL never parse that message to recover structure.

### Requirement 8: Locations and provenance

**User Story:** As a Mist developer, I want every diagnostic that can point somewhere to point there exactly, and every one that cannot to say why, so that an editor can navigate and a reader can trust an absent position.

#### Acceptance Criteria

1. THE Location SHALL be one of: `SourceRange` with file identity, zero-based UTF-16 offset and length; `SourcePoint` with file identity and offset; `FileOnly` with a path; `Project` with the manifest path and an optional key span; `External` with an assembly and symbol; `Generated` with a generated path, a native span and an optional mapped Mist range; and `Absent` with a reason.
2. THE `Absent` reason SHALL be one of `NotYetAttached`, `TestOrigin`, `SyntheticWithoutRoot`, `OutOfBounds` and `Unavailable`.
3. THE offset and length SHALL be authoritative; line and column SHALL be derived, one-based in text, with an exclusive end; a zero-width span at end of file and a span at the first character SHALL both be valid Locations.
4. A span SHALL be valid only when its start is at least zero, its length is at least zero and its end is at most the text length, with the end computed without overflow.
5. WHEN a producer supplies an invalid span, THE Factory SHALL admit the Occurrence with `Absent(OutOfBounds)` and SHALL admit one `ICE` Occurrence naming the producer; no Location SHALL ever be fabricated.
6. THE `<unknown>` path and the default span SHALL not exist in the model; the text formatter alone SHALL render the file-only and location-free line shapes of Requirement 10.
7. THE origin chain of a synthetic node SHALL be kept on the Occurrence as a bounded list of Frames flagged when truncated or cyclic, and SHALL not be appended to the message by default.
8. A Related_Location SHALL carry one role from: `PreviousDeclaration`, `Import`, `Constraint`, `CloneDefinition`, `Instantiation`, `GeneratedOutput`, `DependencyDeclaration`.
9. A Frame SHALL be a lowering or synthesis reason, or an operation context naming the project, the reference chain or the package.
10. EVERY containment, replay or merge comparison between Locations SHALL compare file identity together with the range, never line and column alone.
11. THE parser SHALL attach origins before it reports any node-based finding.
12. Constraint and duplicate-type findings SHALL anchor to the declaring syntax when it exists; project findings SHALL use the `Project` kind with the manifest key's span when known; Roslyn findings SHALL use the `Generated` kind and SHALL gain a mapped Mist range only from a real source map, never from a filename.

### Requirement 9: Collection, identity and order

**User Story:** As a Mist developer, I want two genuinely separate problems to stay two diagnostics, delivered once each in a predictable order, so that nothing I need to fix is folded away.

#### Acceptance Criteria

1. EACH admitted Occurrence SHALL receive an Ordinal, monotonic within the Operation, assigned at admission.
2. Occurrences SHALL be delivered in append order within a stage, with the existing join across stages: loading diagnostics, then the dependency findings streamed at Stage 1.5, then everything after; nothing SHALL be sorted.
3. No Occurrence SHALL be removed because another Occurrence has the same Number, Location or message; `DiagnosticBag.Contains` SHALL be retired.
4. Duplicate detection SHALL be prevented by ownership at the producer, as `AnalysisContext` does with positive failure evidence, never by post-hoc removal.
5. `DiagnosticBag.Withhold` SHALL expose commit, which appends the withheld Occurrences to the parent with their Ordinals intact; discard, which drops them; and replay, which commits an Occurrence withheld earlier; nesting SHALL work and an exception SHALL restore the parent.
6. A wrapper Occurrence SHALL reference its cause by Ordinal and SHALL never quote the cause's message.
7. THE Output_Owner SHALL render each Ordinal at most once across early streaming and the final pass, and the summary SHALL count Effective_Severity over every admitted Occurrence.
8. THE dependency findings SHALL continue to stream as Stage 1.5 ends, before Stage 2 begins, and SHALL not be repeated after the summary.
9. THE Bag SHALL remain stage-local and SHALL NOT be shared across threads; a deterministic aggregate order is deferred to a later decision if parallel reporting is introduced.

### Requirement 10: Text output

**User Story:** As a Mist developer, I want the terminal output I read today to keep its shape, so that my editor's error parser and my habits both keep working.

#### Acceptance Criteria

1. THE text formatter SHALL render an Occurrence with a `SourceRange` or `SourcePoint` Location as `<path>(<line>,<column>): <severity> <Number>: <message>`.
2. THE text formatter SHALL render an Occurrence with a `FileOnly`, `Project` or `Generated` Location as `<path>: <severity> <Number>: <message>`, and one with an `External` or `Absent` Location as `<severity> <Number>: <message>`.
3. THE severity field SHALL be the lowercase `error`, `warning` or `info` of the Effective_Severity.
4. THE Title and the Description SHALL NOT appear in the text line.
5. Text output SHALL go to standard error, SHALL show only Occurrences the minimum-severity filter admits, and SHALL keep early streaming of dependency findings.
6. THE `--quiet` option SHALL continue to suppress progress and the summary only and SHALL never suppress an Occurrence.

### Requirement 11: Machine-readable output

**User Story:** As a tool author, I want one complete, versioned document per invocation with every fact the compiler admitted, so that I never parse prose and never lose a finding to a terminal filter.

#### Acceptance Criteria

1. WHEN `--diagnostic-format json` is given, THE Output_Owner SHALL write one JSON document per Operation, buffered until the Operation ends or, for `run` and `test`, until just before the child process starts.
2. THE document SHALL carry `schemaVersion`, `compiler`, `command`, `project`, `outcome`, `counts` and `diagnostics`, and a `native` array as Requirement 14 describes.
3. EACH entry of `diagnostics` SHALL carry the Ordinal, Number, Title, Category, Policy_Class, Default_Severity, Effective_Severity with its reason, the rendered message, the Slot values by name, the Location, the Related_Locations, the Frames, the cause Ordinal, the Provider_Code where one exists, the producing stage and a `visible` flag.
4. THE document SHALL include every admitted Occurrence; an Occurrence hidden by the minimum-severity filter SHALL have `visible` false, and `counts` SHALL report admitted totals by Effective_Severity and the number hidden.
5. THE document SHALL go to standard error by default, and `--diagnostic-file <path>` SHALL write it to that file instead while standard error keeps text output.
6. THE document SHALL be UTF-8 without a byte-order mark, with camel-case keys in a fixed order, enums as lower-case strings, numbers as JSON numbers and paths spelled exactly as the text line spells them.
7. Consumers SHALL ignore unknown keys; an additive change SHALL raise the minor version and a removal or change of meaning SHALL raise the major version.
8. Program output and test-runner output SHALL never be captured, redirected or represented in the document.
9. Newline-delimited streaming and SARIF SHALL NOT be introduced by this specification.

### Requirement 12: Command-line controls

**User Story:** As a Mist developer, I want to choose the output format, set policy for one run and quiet warning noise, without ever being able to make a failing build look clean.

#### Acceptance Criteria

1. THE CLI SHALL accept `--diagnostic-format text|json`, default `text`, and `--diagnostic-file <path>`.
2. THE CLI SHALL accept `--warnings-as-errors`, promoting every `Promotable` Descriptor, and `--warnings-as-errors <Number>[,<Number>...]`, promoting the listed ones.
3. THE CLI SHALL accept `--disable <Number>[,...]` and `--enable <Number>[,...]` for `Optional` Descriptors only.
4. THE CLI SHALL accept `--min-severity info|warning|error`; `warning` SHALL hide Info, `error` SHALL hide Info and Warning, and no value SHALL hide an Error.
5. THE CLI SHALL NOT accept any control that hides an Occurrence by Number or by Category.
6. `build`, `run` and `test` SHALL accept all controls above; `inspect` and `restore` SHALL accept the format, file, policy and minimum-severity controls; lex mode SHALL accept the format, file and minimum-severity controls; `steering` and `version` SHALL treat them as unknown options.
7. An unknown Number, an unknown or malformed value, or a policy control aimed at a `Fixed` or `Promotable` Number SHALL produce one `CFG` Occurrence before anything runs and SHALL exit 1.
8. THE `--quiet` and `--verbose` options SHALL be unchanged.
9. THE README and the book's CLI page SHALL state that no option hides an error and that `--min-severity` can hide warnings and notes while the summary still counts them; the distributed steering text that says builds are quiet by default SHALL be corrected in the same change.

### Requirement 13: Exit codes and outcomes

**User Story:** As a script author, I want the exit code to stay a single reliable bit, and as a tool author I want to know why a run ended without parsing text.

#### Acceptance Criteria

1. THE exit code SHALL be 0 on success and 1 on any Mist-owned failure; `run` and `test` SHALL return the child's exit code once the child has started and 1 when it could not start.
2. No control, format or policy introduced by this specification SHALL introduce a new exit value.
3. THE Outcome SHALL be one of `Succeeded`, `Failed`, `UsageError`, `InternalFailure` and `Cancelled` and SHALL be recorded in the JSON document.
4. `Main` SHALL catch any unhandled exception once, admit one `ICE` Occurrence carrying the cause and stack, render it through the Output_Owner in the active format, and exit 1; no stage SHALL gain a catch-and-continue.
5. THE `Cancelled` Outcome SHALL be reserved in the schema; cancellation handling and any distinct exit value for it are later decisions.
6. Distinct exit values for usage error and internal failure SHALL NOT be introduced by this specification.

### Requirement 14: Visibility of native and internal conditions

**User Story:** As a Mist developer, I want a compiler bug to stop my build loudly rather than drop a statement quietly, and I want never to see warnings about generated C# I did not write.

#### Acceptance Criteria

1. THE emitter SHALL admit one `ICE` Occurrence, and SHALL still write its comment, whenever it reaches an unhandled statement subclass, an unknown binary or unary operator kind, an `ErrorType` or anonymous type in the type emitter, or a missing default-constraint witness.
2. Transpilation SHALL continue to produce no artifact when the Bag holds an Error, so the Occurrences of criterion 1 gate the build.
3. THE compilation driver SHALL NOT forward any Roslyn diagnostic of severity other than Error as a Mist Occurrence.
4. THE JSON document SHALL carry every Roslyn diagnostic of the in-process compile in a `native` array separate from `diagnostics` and outside `counts`, each with provider, code, severity, message and `Generated` Location.
5. A Roslyn error SHALL continue to be admitted under the Mist wrapper Descriptor, and the Occurrence SHALL carry the Provider_Code, native severity and generated span.
6. A native condition that merits a user-facing rule SHALL be introduced only as a Mist Descriptor with its own Number, Category and Policy_Class, decided in its own task; this specification decides no such rule.
7. Ambient projection SHALL admit no Occurrence for a withheld CLR member unless the program names it, and then the use-site Occurrence SHALL carry the withholding reason as a Frame rather than a second Occurrence.
8. THE nullable-flow warnings in the emitted C#, 26 sites at `af6a1f70` and 22 at the reissue revision, SHALL be tracked as a backend quality item and SHALL NOT be surfaced to users.

### Requirement 15: The reissue and the crosswalk

**User Story:** As the owner, I want every code replaced once, with a record that lets history be read, so that the new scheme is clean from its first release.

#### Acceptance Criteria

1. THE Reissue SHALL happen once, in a single change, after the Registry types of Requirement 2 exist and after the merges and retirements of criteria 5 to 7 are applied.
2. THE Crosswalk SHALL be committed at `specs/diagnostics/crosswalk.md` and SHALL list, for every descriptor field at `af6a1f70` and every field added between it and the reissue revision, marked as such, its old code, its Number or its disposition as merged or retired, its Title and its Category.
3. Book pages, specification documents, test assertions and the CLI help SHALL be rewritten through the Crosswalk; the frozen ledger, Carbonek findings and test-ledger history SHALL never be rewritten.
4. Test assertions on codes SHALL be updated deliberately, one file at a time, and assertions on message text SHALL not change, per Requirement 7.9.
5. THE two not-callable Descriptors for a named member, with and without the unimported-extension hint, SHALL merge into one Descriptor whose template has an `Optional<Name>` namespace Slot, and the Renderer SHALL produce the current text for both cases; the plain-value variant SHALL keep its own Number.
6. THE following seven descriptors SHALL be retired without a Number: `UnresolvedAmbientType`, `NamespaceNotFound`, `TypeAmbiguous`, `UnknownAbstractMember`, `UnknownMember`, `OperatorMustHaveBody` and `DuplicateExtensionMember`; citations of `MIST_NS_NOT_FOUND` and `MIST_TYPE_AMBIGUOUS` in specifications SHALL be rewritten to the Numbers of `UndefinedNamespace` and `AmbiguousType`.
7. THE `PackageNoCompatibleTfm` descriptor SHALL receive no Number until a resolver site reports it; the gap between `csharp-interop-v2` Requirement 1 and the resolver is recorded as a lead for the owner.
8. EVERY other group of descriptors that share a code at `af6a1f70` SHALL receive one Number per template, with related Numbers declared where the review found a shared condition.
9. Storm, Roslyn and NuGet codes cited in documents SHALL remain Provider_Codes; a specification-only historical Mist code with no live twin SHALL be marked historical in place and never resurrected.

### Requirement 16: Supersession

**User Story:** As a compiler maintainer, I want exactly one normative diagnostics contract in the repository, so that implementation acceptance cannot be contradicted by an older clause.

#### Acceptance Criteria

1. THIS specification SHALL supersede `diagnostic-descriptor-system` Requirements 1.2 to 1.5 and 2.1 to 2.12 by Requirements 1, 2 and 3 above; Requirements 3.1 to 3.5 by Requirements 5 and 7; Requirements 4.1 to 4.5 and 5.1 to 5.4 by Requirements 1, 5 and 15; and Requirement 7 in full, there being no steering file.
2. THIS specification SHALL supersede `unified-diagnostic-formatting` Requirements 4.3, 4.4, 7.3 and 7.4 by Requirement 8; Requirement 5.6 and design Property 1 by Requirement 11; and the design's rule of a minimum of 100 iterations for every property by Requirement 17.
3. `diagnostic-descriptor-system` Requirements 6.4 and 6.5 SHALL remain in force as the wording freeze of Requirement 7.9; `unified-diagnostic-formatting` Requirements 1 to 3 and 6 SHALL remain in force for the Storm boundary.
4. THE two superseded specifications SHALL each receive a leading note naming this document and the clauses it supersedes, and SHALL otherwise be left as history.
5. THE `full-accessibility-model` specification, which cites the descriptor specification, SHALL have its citations checked and rewritten to this document where they refer to a superseded clause.

### Requirement 17: Verification constraints

**User Story:** As a compiler maintainer, I want the migration proven at the cheapest tier that owns each invariant, so that the suite stays fast and the proof stays honest.

#### Acceptance Criteria

1. Registry validation, slot and template rules, rendering, Location bounds and derivation, origin chains, policy combination, Ordinal assignment, withhold outcomes and the JSON conventions SHALL be proven at Tier 1 with directly constructed inputs.
2. A rule that walks a body SHALL be proven at Tier 2 with hand-built model input; whole-phase checks SHALL be rare Tier 3 tests with stub providers.
3. THE Stage 4 to Stage 5 no-cascade invariant, the Storm-to-Mist adapter seam and the command output seams SHALL be proven by a handful of Tier 4 tests, not one per Descriptor.
4. No new Tier 5 test and no second Tier 6 orchestrator SHALL be added; `AllFeaturesV2` remains the one full-system check.
5. A property test SHALL run more than 50 iterations only at Tier 1 or Tier 2; the former blanket rule of 100 iterations for every property is withdrawn.
6. EVERY new or migrated test SHALL carry exactly one effective `Tier` trait.
7. EACH migration change SHALL build with zero warnings, pass `dotnet format Mist.slnx --verify-no-changes`, and pass the full ledger run, including `AllFeaturesV2`, before it lands.

### Requirement 18: Documentation

**User Story:** As a Mist developer, I want a catalog I can browse and a book that uses the same codes the compiler prints.

#### Acceptance Criteria

1. A catalog page SHALL be generated from the Registry index, grouped by Category, listing for each Number its Title, Description, Default_Severity, Policy_Class and Slots, and SHALL be regenerated by a checked-in script rather than edited by hand.
2. THE book's `.NET Interop` chapter and every other page that names a code SHALL be rewritten through the Crosswalk in the Reissue change.
3. THE CLI help SHALL list the controls of Requirement 12 with one line each.
4. A command that explains a Number from the Registry, and per-source suppression, are candidates for later specifications and are not required here.
