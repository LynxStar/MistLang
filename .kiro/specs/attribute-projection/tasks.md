# Implementation Plan: Attribute Projection

## Overview

Four-phase implementation: grammar changes (regenerate parser), ProgramModel attribute storage (AttributeData + IHasAttributes), CLR attribute projection in TypeProjectorV2, and ClrCallingConvention removal from ProgramModel. The old semantic system remains untouched.

## Tasks

- [x] 1. Grammar changes — add attributes to Parameter, PropertyDeclaration, IndexerDeclaration
  - [x] 1.1 Add `attributes:AttributeList*` to the `Parameter` rule in `Source/Mist/Grammar/Mist.storm`
    - Insert `attributes:AttributeList*` before `modifier:READONLY?` in the Parameter rule
    - _Requirements: 1.1, 1.4_

  - [x] 1.2 Add `attributes:AttributeList*` to both `PropertyDeclaration` alternatives
    - Insert `attributes:AttributeList*` before `modifiers:Modifier*` in both BlockProperty and ExpressionBodiedProperty
    - _Requirements: 1.2, 1.4_

  - [x] 1.3 Add `attributes:AttributeList*` to `IndexerDeclaration`
    - Insert `attributes:AttributeList*` before `modifiers:Modifier*` in IndexerDeclaration
    - _Requirements: 1.3, 1.4_

  - [x] 1.4 Build to regenerate parser and verify no errors
    - Run `dotnet build Mist.slnx` to trigger Storm source generator
    - Fix any compilation errors from the regenerated parser code
    - _Requirements: 1.1, 1.2, 1.3, 1.4_

- [x] 2. ProgramModel — create AttributeData and IHasAttributes
  - [x] 2.1 Create `AttributeData` class in `Source/Mist/ProgramModel/Model/AttributeData.cs`
    - Public sealed class with `IMistType AttributeType`, `IReadOnlyList<object?> PositionalArguments`, `IReadOnlyList<(string Name, object? Value)> NamedArguments`
    - Primary constructor pattern matching existing model classes
    - _Requirements: 2.1, 2.2, 2.3_

  - [x] 2.2 Create `IHasAttributes` interface in `Source/Mist/ProgramModel/Contracts/IHasAttributes.cs`
    - Public interface with `IReadOnlyList<AttributeData> Attributes { get; }`
    - _Requirements: 3.1_

  - [x] 2.3 Add `List<AttributeData> AttributesList` to `Parameter` and implement `IHasAttributes`
    - Add the list property and explicit interface implementation
    - _Requirements: 3.2_

  - [x] 2.4 Add `List<AttributeData> AttributesList` to `Method` and implement `IHasAttributes`
    - Method is the base for Constructor, Operator, CastOperator — so they inherit the implementation
    - _Requirements: 3.3, 3.7, 3.8_

  - [x] 2.5 Add `List<AttributeData> AttributesList` to `Property` and implement `IHasAttributes`
    - _Requirements: 3.4_

  - [x] 2.6 Add `List<AttributeData> AttributesList` to `Field` and implement `IHasAttributes`
    - _Requirements: 3.5_

  - [x] 2.7 Add `List<AttributeData> AttributesList` to `Indexer` and implement `IHasAttributes`
    - _Requirements: 3.6_

  - [x] 2.8 Add `List<AttributeData> AttributesList` to `MemberType` and implement `IHasAttributes`
    - _Requirements: 3.9_

  - [x] 2.9 Build to verify ProgramModel compiles cleanly
    - Run `dotnet build Mist.slnx` and fix any issues
    - _Requirements: 2.1, 2.2, 2.3, 3.1-3.9_

- [x] 3. CLR attribute projection in TypeProjectorV2
  - [x] 3.1 Add `ProjectParameterAttributes` helper method to `TypeProjectorV2`
    - Read `ParameterInfo.GetCustomAttributesData()`, resolve each attribute type via `resolveType`, create `AttributeData` instances, add to parameter's `AttributesList`
    - Skip silently if attribute type cannot be resolved
    - Wrap in try/catch for metadata exceptions
    - _Requirements: 4.1, 4.2, 4.3_

  - [x] 3.2 Call `ProjectParameterAttributes` from `ProjectParameters` for each projected parameter
    - After creating each `Parameter` instance and setting RefKind, invoke the attribute projection
    - Pass the `resolveType` callback through
    - _Requirements: 4.1, 4.4, 4.5_

  - [x] 3.3 Build and verify projection compiles
    - Run `dotnet build Mist.slnx`
    - _Requirements: 4.1-4.5_

- [x] 4. Remove ClrCallingConvention from ProgramModel path
  - [x] 4.1 Delete `Source/Mist/ProgramModel/Model/ClrCallingConvention.cs`
    - _Requirements: 5.1_

  - [x] 4.2 Remove `ClrCallingConvention?` property from `Parameter.cs`
    - Remove the property and its XML doc comment
    - _Requirements: 5.2_

  - [x] 4.3 Update `TypeProjectorV2.ProjectParameters` to stop setting `ClrCallingConvention`
    - Remove all assignments to `parameter.ClrCallingConvention` and `p.ClrCallingConvention`
    - The `RefKind.Ref` assignment remains
    - Attribute projection (task 3.2) now carries the in/out information via attributes
    - _Requirements: 5.3, 5.4, 5.5_

  - [x] 4.4 Build and verify removal compiles cleanly
    - Run `dotnet build Mist.slnx` — fix any remaining references in the ProgramModel path
    - Note: old system references (TypeProjector, Symbol, CSharpEmitter) remain unchanged
    - _Requirements: 5.1-5.5_

- [x] 5. Checkpoint — full build and test
  - Ensure `dotnet build Mist.slnx` succeeds with zero warnings
  - Run `dotnet test Mist.slnx` to verify no regressions
  - Run `dotnet format Mist.slnx --verify-no-changes --include Source/` to verify formatting
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Sample application — extend Attributes sample
  - [x] 6.1 Add parameter attribute demonstration to `samples/Attributes/Program.mist`
    - Add `ParamTagAttribute` custom attribute and `Formatter` class with attribute on parameter
    - Call from `RunDemo()` and print output
    - _Requirements: 6.1, 6.2_

  - [x] 6.2 Add property attribute demonstration to `samples/Attributes/Program.mist`
    - Add `Settings` class with `[Obsolete]` on a property
    - Call from `RunDemo()` and print output
    - _Requirements: 6.1, 6.3_

  - [x] 6.3 Add indexer attribute demonstration to `samples/Attributes/Program.mist`
    - Add `LookupTable` class with `[Obsolete]` on an indexer
    - Call from `RunDemo()` and print output
    - _Requirements: 6.1, 6.4_

  - [x] 6.4 Build and run sample to verify expected output
    - Compile and run `samples/Attributes/` via the Mist compiler
    - Verify the proof code output matches expected
    - Updated `samples/__baselines__/AllFeatures.out` with new output
    - _Requirements: 6.5, 6.6_

- [x] 7. Final checkpoint — full test suite
  - Run `dotnet build Mist.slnx` with zero warnings
  - Run `dotnet test Mist.slnx` to confirm no regressions (4125 passed)
  - Run `dotnet format Mist.slnx --verify-no-changes` on changed files
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- The old semantic system (Symbol.ClrCallingConvention, TypeProjector, CSharpEmitter) remains untouched
- Grammar changes trigger Storm source generator on build — no manual parser code needed
- Book documentation (Requirement 7) is deferred to a separate task after implementation stabilizes
- Tasks marked with `*` are optional and can be skipped for faster MVP
- Property tests validate universal correctness properties
