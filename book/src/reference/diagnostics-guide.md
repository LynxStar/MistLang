Mist treats a diagnostic as a structured report of a condition. Its public code identifies
the condition; its category describes the subject; its severity determines the effect on
compilation. These are separate from the compiler stage that discovered it and from the text
used to explain it.

This page explains that model, the terminal and JSON output, and the controls available to
projects. The generated [catalog](#diagnostic-catalog) below lists individual diagnostics.

## Identity and Categories

A code such as `MIST0479` identifies one diagnostic definition. The four digits carry no
category, severity or pipeline-stage meaning. Codes remain stable when implementation moves
between stages; retired numbers are reserved rather than assigned a new meaning. The
[diagnostic crosswalk](https://github.com/LynxStar/MistLang/blob/main/specs/diagnostics/crosswalk.md)
maps identifiers from before the one-time numbering migration to their replacements.

Every definition has exactly one of thirteen categories. Categories describe the problem,
not the directory or stage that happens to report it.

| Token | Subject | What belongs here |
| --- | --- | --- |
| `SYN` | Syntax | Tokenization and grammatical form |
| `NAM` | Name resolution | Missing or ambiguous names |
| `TYP` | Types and constraints | Type compatibility and generic constraints |
| `DEC` | Declaration contracts | Invalid member or type declarations |
| `FLW` | Control and data flow | Assignment and execution-path requirements |
| `OWN` | Ownership and lifetime | Moves, loans, cleanup and resource lifetimes |
| `EFF` | Effects and context | Purity and structured-context requirements |
| `INT` | Interoperability | Contracts across language boundaries |
| `BCK` | Backend capability | Target support and downstream compilation |
| `CFG` | Project configuration | Manifest and command configuration |
| `DEP` | Dependency resolution | Packages, frameworks and project references |
| `IOX` | Tool and I/O operations | Reading, writing and launching tools |
| `ICE` | Compiler invariants | Broken assumptions inside the compiler |

The catalog contains a section for each category with current entries. A defined category
without entries has no section. The JSON `category` field carries these uppercase tokens;
the `stage` field separately records where an occurrence originated.

For example, `MIST0479`, **Installed value consumed**, is an `OWN` diagnostic with default
severity `Error` and policy `Fixed`. Its named arguments identify the attempted action and
the variable. The code identifies that rule; a particular report adds the affected source
location and actual argument values.

## Definitions and Occurrences

The registry defines each diagnostic once. A definition contains its code, short title,
message template, explanatory paragraph, category, default severity, policy class and named
argument schema. It may also list related diagnostic codes. These are the columns in the
catalog below.

An **occurrence** is one report of that definition during a compiler operation. It records
the actual arguments, location, producing stage, effective severity and supporting context.
The distinction matters: two uses of the same invalid operation can produce two occurrences
of one diagnostic code.

Each admitted occurrence receives an ordinal within its operation. Reports retain their
collection order; they are not sorted by code. Separate findings are not merged merely
because their code, message or location matches. The output layer uses occurrence identity
to avoid printing the same report again when it arrives through another compiler result.
An ordinal is not a persistent identifier to compare across compiler invocations.

Dependency diagnostics can appear early, when dependency resolution ends. They still count
in the final summary and are not printed again there. Internally, tentative reports can be
withheld and explicitly committed, discarded or replayed. They become output through that
decision, rather than through filtering identical message strings after the fact.

## Facts Before Message Text

Compiler rules supply named, typed arguments to a shared renderer. Argument kinds include
names, types, symbols, paths, quantities, booleans, enumerated values, text, sequences and
optional values. The catalog's **Slots** column describes those arguments; the JSON
`arguments` object carries their values.

Type and symbol arguments capture both identity and display spelling when the report is
made. They do not retain a live compiler model that could change before the diagnostic is
displayed. The renderer formats the message using invariant culture.

This separation gives people readable messages and tools structured facts. An editor should
use a type argument or related location directly instead of extracting it from a sentence.
Registry validation checks that templates and arguments agree. Message-text checks preserve
wording through refactoring; intentional wording changes require explicit updates to those
checks.

The **Title** names the condition. The **Description** explains it and how to address it.
The rendered **message** describes the particular occurrence. Terminal output uses the
message; the catalog provides the longer explanation, and JSON includes the title.

## Severity, Policy and Visibility

Severity is `Info`, `Warning` or `Error`. The registry supplies a default, and project or CLI
policy determines the effective severity for each admitted report. Compilation gates and
command success use the effective severity, including when deciding whether to write an
artifact. JSON records both severities and the reason for a promotion.

Policy classes determine which changes a project may request:

| Policy class | What project policy can do |
| --- | --- |
| `Fixed` | Leave the diagnostic's severity and rule unchanged |
| `Promotable` | Raise a warning or informational report to an error |
| `Optional` | Enable or disable the rule, or promote its reports to errors |

Disabling an optional rule means it does not run. It is different from hiding a report that
has already been produced. Policy cannot lower severity or disable a fixed error. Naming an
unknown code, or trying to switch a non-optional rule, is itself a configuration error.

For example, this project promotes the duplicate-global-using warning to an error:

```toml
[diagnostics]
warnings_as_errors = ["MIST0082"]
```

Use `warnings_as_errors = true` to promote all eligible warnings and informational reports.
The command line offers corresponding overrides. See
[project diagnostic policy](../tooling/project-configuration.md#the-diagnostics-table) for
`enable`, `disable` and validation rules.

Display filtering is independent of policy:

```bash
mist build my-project --min-severity error
```

This hides warnings and information from terminal display. It does not change their
severity, remove them from the counts, or change whether compilation succeeds. Errors remain
visible. JSON retains filtered occurrences with `visible: false`. `--quiet` suppresses
progress and the summary, not diagnostics.

## Locations and Supporting Context

A diagnostic points to the most precise location the compiler actually has. It does not
invent a source position when only a filename or external symbol is known.

| JSON location kind | Meaning |
| --- | --- |
| `sourceRange` | A span within a source file |
| `sourcePoint` | A position without a range |
| `fileOnly` | A file without a source position |
| `project` | A manifest, optionally with the relevant key's span |
| `external` | An assembly and symbol outside the source project |
| `generated` | A generated file and native position, with a source mapping only when available |
| `absent` | No usable location, with an explicit reason |

Source offsets and lengths use zero-based UTF-16 units. Lines and columns are one-based;
range ends are exclusive. Location identity includes the file, so equal offsets in different
files are not the same location.

Related locations identify supporting evidence such as a previous declaration, constraint,
clone definition, instantiation or dependency declaration. Frames describe context such as
synthesis, lowering or a reference chain. A wrapper diagnostic can name its underlying
cause by ordinal. These relationships remain available as structured data rather than being
flattened into a longer message. A generated filename alone is not evidence of a mapping
back to Mist source.

## Reading Terminal Output

The usual line has the following form:

```text
path(line,column): severity MISTnnnn: message
```

When only a path is known, the parentheses are omitted. With an external or absent location,
the path is omitted too. The code and message appear in the line; the title, category and
longer description do not. Use the code to find the corresponding catalog entry.

Diagnostics go to stderr. Progress and program output use their existing channels. The
compiler keeps these responsibilities separate so enabling diagnostic output does not
replace the stdout contract of a compiled Mist program.

## Consuming JSON

To request JSON on stderr:

```bash
mist build my-project --diagnostic-format json
```

To write JSON to a UTF-8 file while keeping text diagnostics on stderr:

```bash
mist build my-project --diagnostic-file diagnostics.json
```

The output is one buffered document per compiler operation, not newline-delimited events.
For `run` and `test`, the document is completed before the child process starts. Application
and test-runner output are not captured inside it.

| Document field | Contents |
| --- | --- |
| `schemaVersion` | Version of the diagnostic document contract |
| `compiler`, `command`, `project` | The reporting compiler and operation |
| `outcome` | Compiler-operation outcome, such as `succeeded`, `failed`, `usageError` or `internalFailure` |
| `counts` | Admitted totals, effective error/warning/info counts and the number hidden from text display |
| `diagnostics` | Every admitted Mist occurrence, including filtered ones |
| `native` | Diagnostics from the in-process Roslyn compilation, separate from Mist occurrences and counts |

Each Mist occurrence includes `ordinal`, `code`, `title`, `category`, `policy`, `severity`,
`message`, `arguments`, `location`, `related`, `frames`, `cause`, `native`, `stage` and
`visible`. The `severity` object has `default`, `effective` and `reason` fields. An occurrence's
`native` field identifies a wrapped provider diagnostic; the document's top-level `native`
array preserves the provider's diagnostic records.

Consumers must ignore unknown keys. Additive schema changes increment the minor version;
removals or changes of meaning increment the major version. The current schema is `1.0`.
Use structured fields for matching and navigation; retain the message for display. There is
no CLI filter to suppress individual reported codes or categories, and no SARIF output in
this contract.

A Mist-owned failure exits with code 1. Once `run` or `test` starts a child, the command
returns the child's exit code. Consequently, the completed diagnostic document describes
the compiler operation, not the later execution result. A host failure after that document
is complete is reported as a text compiler-invariant error on stderr; the document is not
rewritten. The `cancelled` outcome is reserved, not a promise of implemented cancellation.

## Compiler Failures and Foreign Diagnostics

`ICE` identifies an internal compiler invariant failure. It stops successful compilation
and carries evidence for diagnosing the compiler. It should not be interpreted as a language
rule the user violated. An unsupported internal emission path must report a failure instead
of silently dropping part of a program.

Roslyn errors remain visible through Mist's backend diagnostic wrapper, retaining their
native code and generated location. Roslyn warnings about generated C# are not promoted into
Mist source warnings; they remain inspectable in the JSON `native` array. Correcting generated
code is the backend's responsibility. Dependency and parser wrappers likewise preserve the
provider's code or message where available instead of parsing provider prose to invent facts.

## Maintaining the Reference

The catalog below is generated from the registry used by the compiler. Its columns describe
definitions, so their severity is the default rather than a particular project's effective
severity. **Related numbers** links diagnostic definitions; it is distinct from an
occurrence's related source locations. **Slots** is the named argument schema, not a list of
extra compiler settings.

After changing registry definitions, contributors regenerate it with:

```bash
tools/diagnostics/catalog.sh
```

The existing catalog check compares the committed output with the registry rendering.
The explanatory guide is maintained in `book/src/reference/diagnostics-guide.md` and included
by the generator, so regeneration preserves it.
