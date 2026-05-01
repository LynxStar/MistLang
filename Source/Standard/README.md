# Mist Standard Library

This directory is the `Stdlib_Source_Root`. Every file under `Source/Standard/`
is a `.mist` source file that compiles as part of the Mist Standard Library.

The stdlib lives under a single root namespace: **`Standard`** (Requirement 1.1).
Every file in this tree starts with `namespace Standard;`.

## Tier layout

The stdlib has two tiers, organized by directory.

### `Bootstrap/` — Backend_Backed_Types

Files under `Bootstrap/` declare types whose storage layout and intrinsic
operations are provided by the backend rather than by Mist source. Each type
declaration carries a type-level `intrinsic` modifier, and its operators,
methods, and constructors carry member-level `intrinsic` modifiers with no
bodies (terminated with `;`). The backend supplies the runtime representation
via its private Backing_Type_Map and emits operator expressions from its
emission-template registry.

Members of this tier:

- Numeric primitives: `Int32`, `UInt32`, `Int64`, `UInt64`, `Int16`, `UInt16`,
  `Byte`, `SByte`, `Single`, `Double`, `Decimal`
- `Boolean`
- `Char`
- `String` (class, reference type)
- `Array<T>` (generic class)

See Requirements 2, 3, 5, and 9 for the full specification.

### `Source/Standard/` root — Pure-Mist tier

Files placed directly under `Source/Standard/` (not in `Bootstrap/`) are
Pure_Mist_Tier types: ordinary Mist source with no `intrinsic` markers. They
compose bootstrap tier types into higher-level abstractions.

v1 ships a single demo: `Optional.mist` (see Requirement 1.3 and Requirement
13.6).

## File naming convention

One public type per file; the file name matches the type's simple name (e.g.,
`Int32.mist` declares `Standard.Int32`). Requirement 1.4 requires Bootstrap
tier files to reside under `Bootstrap/`; Requirement 1.5 requires Pure-Mist
tier files to reside outside any `Bootstrap/` subdirectory.

## How the stdlib is loaded

The stdlib is loaded via **Approach B — source prelude**: the `CompilerDriver`
reads every `.mist` file under this directory at the start of every
compilation and prepends them to the user's source list. See design document
§ *Stdlib Loading Strategy* for details.
