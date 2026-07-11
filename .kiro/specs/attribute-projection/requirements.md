# Requirements Document

## Introduction

This feature adds attribute support to grammar declarations that currently lack it (Parameter, PropertyDeclaration, IndexerDeclaration), introduces fully-typed attribute storage (`AttributeData`) to the ProgramModel, projects CLR attributes during ambient type projection via `TypeProjectorV2`, and removes the `ClrCallingConvention` enum from the ProgramModel path in favor of attribute-based calling convention detection.

## Glossary

- **ProgramModel**: The new compiler frontend semantic model under `Source/Mist/ProgramModel/` representing fully resolved types, members, and relationships.
- **AttributeData**: A record representing a single applied attribute with its resolved type and arguments.
- **IHasAttributes**: A public contract interface providing uniform access to attributes on any declaration.
- **TypeProjectorV2**: The component that converts CLR `System.Type` instances and their members into ProgramModel representations.
- **Grammar**: The Mist language grammar defined in `Mist.storm`, processed by the Storm source generator.
- **CLR Attribute**: A .NET attribute applied to a type, method, property, field, parameter, or return value at the metadata level.
- **AttributeList**: The grammar rule representing a sequence of attributes in square brackets.

## Requirements

### Requirement 1

**User Story:** As a language developer, I want the Mist grammar to support attributes on parameters, properties, and indexers, so that Mist source code can express attribute annotations on these declarations.

#### Acceptance Criteria

1. WHEN the parser encounters `attributes:AttributeList*` before a parameter's modifiers, THE Grammar SHALL parse the attributes and attach them to the Parameter node.
2. WHEN the parser encounters `attributes:AttributeList*` before a PropertyDeclaration's modifiers, THE Grammar SHALL parse the attributes and attach them to the PropertyDeclaration node.
3. WHEN the parser encounters `attributes:AttributeList*` before an IndexerDeclaration's modifiers, THE Grammar SHALL parse the attributes and attach them to the IndexerDeclaration node.
4. WHEN a Parameter, PropertyDeclaration, or IndexerDeclaration has no attributes, THE Grammar SHALL parse the declaration without error and produce an empty attributes list.

### Requirement 2

**User Story:** As a compiler developer, I want a typed `AttributeData` record in the ProgramModel, so that attributes are first-class entities with resolved type references.

#### Acceptance Criteria

1. THE ProgramModel SHALL define an `AttributeData` record with an `IMistType AttributeType` property representing the resolved attribute class.
2. THE ProgramModel SHALL define an `AttributeData` record with an `IReadOnlyList<object?> PositionalArguments` property representing constructor arguments.
3. THE ProgramModel SHALL define an `AttributeData` record with an `IReadOnlyList<(string Name, object? Value)> NamedArguments` property representing named property assignments.

### Requirement 3

**User Story:** As a compiler developer, I want an `IHasAttributes` public contract interface, so that consumers can uniformly query attributes on any declaration.

#### Acceptance Criteria

1. THE ProgramModel SHALL define a public `IHasAttributes` interface in `Contracts/` with a read-only collection of `AttributeData`.
2. THE Parameter class SHALL implement `IHasAttributes`.
3. THE Method class SHALL implement `IHasAttributes`.
4. THE Property class SHALL implement `IHasAttributes`.
5. THE Field class SHALL implement `IHasAttributes`.
6. THE Indexer class SHALL implement `IHasAttributes`.
7. THE Operator class SHALL implement `IHasAttributes`.
8. THE Constructor class SHALL implement `IHasAttributes`.
9. THE MemberType base class SHALL implement `IHasAttributes`.

### Requirement 4

**User Story:** As a compiler developer, I want CLR parameter attributes to be projected into the ProgramModel during ambient type projection, so that attribute metadata from .NET assemblies is available for analysis and emission.

#### Acceptance Criteria

1. WHEN TypeProjectorV2 projects a CLR parameter with custom attributes, THE TypeProjectorV2 SHALL create `AttributeData` instances for each resolvable attribute and attach them to the projected Parameter.
2. WHEN TypeProjectorV2 resolves an attribute type, THE TypeProjectorV2 SHALL use the same `resolveType` callback used for all other type references in the projection.
3. IF an attribute type cannot be resolved through the `resolveType` callback, THEN THE TypeProjectorV2 SHALL skip that attribute silently without failing the parameter projection.
4. WHEN TypeProjectorV2 projects a CLR parameter with `[OutAttribute]`, THE TypeProjectorV2 SHALL include an `AttributeData` entry with `AttributeType` resolved to the `OutAttribute` type.
5. WHEN TypeProjectorV2 projects a CLR parameter with `[IsReadOnlyAttribute]` or `[InAttribute]`, THE TypeProjectorV2 SHALL include an `AttributeData` entry with the corresponding resolved attribute type.

### Requirement 5

**User Story:** As a compiler developer, I want to remove `ClrCallingConvention` from the ProgramModel path, so that calling convention information is derived from parameter attributes instead of a separate enum.

#### Acceptance Criteria

1. THE ProgramModel SHALL NOT contain the `ClrCallingConvention` enum in its Model directory.
2. THE Parameter class SHALL NOT have a `ClrCallingConvention?` property.
3. WHEN TypeProjectorV2 projects a CLR `out` parameter, THE TypeProjectorV2 SHALL populate the Parameter's attribute list with `[OutAttribute]` instead of setting a `ClrCallingConvention` property.
4. WHEN TypeProjectorV2 projects a CLR `in` parameter, THE TypeProjectorV2 SHALL populate the Parameter's attribute list with the `[IsReadOnlyAttribute]` or `[InAttribute]` instead of setting a `ClrCallingConvention` property.
5. WHEN TypeProjectorV2 projects a CLR `ref` parameter that is neither `in` nor `out`, THE TypeProjectorV2 SHALL set `RefKind.Ref` on the parameter without adding calling-convention attributes.

### Requirement 6

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating attributes on parameters, properties, and indexers, so that I can see every supported usage pattern and verify correctness.

#### Acceptance Criteria

1. The existing `samples/Attributes/Program.mist` SHALL be extended to demonstrate attributes on parameters, properties, and indexers.
2. The sample SHALL demonstrate attributes on parameters with positional arguments.
3. The sample SHALL demonstrate attributes on properties.
4. The sample SHALL demonstrate attributes on indexer declarations.
5. Each demonstrated permutation SHALL print labeled output to stdout so correctness is observable.
6. The sample SHALL compile and run without errors using the Mist compiler.

#### Proof Code

```mist
// PROOF: Attribute on a parameter - uses a custom attribute targeting parameters
class ParamTagAttribute : Attribute
{
    public string Tag;

    ParamTagAttribute(string tag)
    {
        this.Tag = tag;
    }
}

class Formatter
{
    public string Format([ParamTag("format-input")] string template, int value)
    {
        return $"{template}: {value}";
    }
}

// PROOF: Attribute on a property declaration
class Settings
{
    [Obsolete("Use Timeout instead")]
    public int Delay { get; }

    public int Timeout { get; }

    Settings(int timeout)
    {
        this.Timeout = timeout;
    }
}

// PROOF: Attribute on an indexer declaration
class LookupTable
{
    int[] data;

    LookupTable(int size)
    {
        this.data = new int[size];
    }

    [Obsolete("Use Get method instead")]
    public int this[int index]
    {
        get { return data[index]; }
        set { data[index] = value; }
    }
}
```

**Expected output:**
```
Formatter result: Hello: 42
Settings timeout: 100
LookupTable[0]: 99
```

### Requirement 7

**User Story:** As a Mist developer, I want the language book to document attribute placement on parameters, properties, and indexers, so that I can learn the full attribute syntax from the official reference.

#### Acceptance Criteria

1. THE book SHALL include a section covering attribute placement on parameters, properties, and indexers with compiled code examples.
2. THE book section SHALL demonstrate each declaration position that accepts attributes.
3. All code examples SHALL be compiled snippets following the Book Snippet Policy.
