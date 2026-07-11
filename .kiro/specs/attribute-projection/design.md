# Design Document: Attribute Projection

## Overview

This feature extends attribute support across three areas: grammar (parameter, property, indexer declarations), ProgramModel (typed `AttributeData` storage with `IHasAttributes` contract), and CLR projection (projecting .NET attributes during assembly type projection). It also removes `ClrCallingConvention` from the ProgramModel in favor of attribute-based calling convention detection.

## Architecture

```
┌────────────────────┐     ┌──────────────────────┐
│  Mist.storm        │     │  CLR Assembly         │
│  (grammar rules)   │     │  (System.Type)        │
└────────┬───────────┘     └──────────┬────────────┘
         │                            │
         ▼                            ▼
┌────────────────────┐     ┌──────────────────────┐
│  Parser (generated)│     │  TypeProjectorV2     │
│  - Parameter node  │     │  - ProjectParameters │
│  - Property node   │     │  - ProjectAttributes │
│  - Indexer node    │     └──────────┬────────────┘
└────────┬───────────┘                │
         │                            │
         ▼                            ▼
┌─────────────────────────────────────────────────┐
│              ProgramModel                        │
│  ┌──────────────┐  ┌─────────────────────────┐  │
│  │ IHasAttributes│  │ AttributeData           │  │
│  │  .Attributes  │  │  .AttributeType: IMistType│ │
│  └──────────────┘  │  .PositionalArguments    │  │
│                     │  .NamedArguments         │  │
│                     └─────────────────────────┘  │
│  Implemented by: Parameter, Method, Property,    │
│  Field, Indexer, Operator, Constructor, MemberType│
└──────────────────────────────────────────────────┘
```

## Components and Interfaces

### 1. Grammar Changes

The following grammar rules are modified to accept `attributes:AttributeList*`:

**Parameter** (currently no attributes):
```storm
rule Parameter = attributes:AttributeList* modifier:READONLY? refModifier:REF? ownershipSpecifier:OwnershipSpecifier? type:TypeReference name:(IDENTIFIER | DISCARD);
```

**PropertyDeclaration** (both alternatives gain attributes):
```storm
rule PropertyDeclaration
    = attributes:AttributeList* modifiers:Modifier* type:TypeReference name:IDENTIFIER
      LEFT_BRACE accessors:AccessorDeclaration+ RIGHT_BRACE
      initializer:(EQUAL Expression SEMICOLON)?                    as BlockProperty : HasModifiers
    | attributes:AttributeList* modifiers:Modifier* type:TypeReference name:IDENTIFIER
      SUCH_THAT body:Expression SEMICOLON                          as ExpressionBodiedProperty : HasModifiers
    ;
```

**IndexerDeclaration** (gains attributes):
```storm
rule IndexerDeclaration : HasModifiers = attributes:AttributeList* modifiers:Modifier* type:TypeReference THIS
      LEFT_BRACKET parameters:ParameterList RIGHT_BRACKET
      constraints:WhereClause*
      body:IndexerBody;
```

### 2. AttributeData Record

Location: `Source/Mist/ProgramModel/Model/AttributeData.cs`

```csharp
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// Represents a single applied attribute with its resolved type and arguments.
/// </summary>
internal sealed class AttributeData(
    IMistType attributeType,
    IReadOnlyList<object?> positionalArguments,
    IReadOnlyList<(string Name, object? Value)> namedArguments)
{
    /// <summary>The resolved attribute class type.</summary>
    public IMistType AttributeType { get; } = attributeType;

    /// <summary>Positional constructor arguments.</summary>
    public IReadOnlyList<object?> PositionalArguments { get; } = positionalArguments;

    /// <summary>Named property/field assignments.</summary>
    public IReadOnlyList<(string Name, object? Value)> NamedArguments { get; } = namedArguments;
}
```

### 3. IHasAttributes Contract

Location: `Source/Mist/ProgramModel/Contracts/IHasAttributes.cs`

```csharp
namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A declaration that can carry applied attributes.
/// </summary>
public interface IHasAttributes
{
    /// <summary>The attributes applied to this declaration.</summary>
    IReadOnlyList<AttributeData> Attributes { get; }
}
```

Note: `AttributeData` must be public (or have a public contract equivalent) since `IHasAttributes` exposes it. The class will be made `public` rather than `internal`.

### 4. IHasAttributes Implementation

Each model class gains a `List<AttributeData>` field and implements the interface:

```csharp
// In Parameter.cs
public List<AttributeData> AttributesList { get; } = [];
IReadOnlyList<AttributeData> IHasAttributes.Attributes => AttributesList;
```

The same pattern applies to `Method`, `Property`, `Field`, `Indexer`, `Operator`, `Constructor`, and `MemberType`.

### 5. CLR Attribute Projection in TypeProjectorV2

The `ProjectParameters` method is extended to read CLR attributes:

```csharp
private static void ProjectParameterAttributes(
    Parameter parameter,
    ParameterInfo clrParam,
    Func<string, string, IMistType?> resolveType)
{
    foreach (var attrData in clrParam.GetCustomAttributesData())
    {
        var attrType = attrData.AttributeType;
        var attrNs = attrType.Namespace ?? string.Empty;
        var attrName = attrType.Name;

        var resolved = resolveType(attrNs, attrName);
        if (resolved is null)
        {
            continue; // Skip unresolvable attribute types
        }

        var positionalArgs = attrData.ConstructorArguments
            .Select(a => a.Value)
            .ToList();

        var namedArgs = attrData.NamedArguments
            .Select(a => (a.MemberName, a.TypedValue.Value))
            .ToList();

        parameter.AttributesList.Add(new AttributeData(
            resolved,
            positionalArgs,
            namedArgs));
    }
}
```

### 6. ClrCallingConvention Removal

- Delete `Source/Mist/ProgramModel/Model/ClrCallingConvention.cs`
- Remove `public ClrCallingConvention? ClrCallingConvention { get; internal set; }` from `Parameter.cs`
- In `TypeProjectorV2.ProjectParameters`, replace the `ClrCallingConvention` assignment with attribute projection:
  - `param.IsOut` → add `OutAttribute` to parameter's attribute list
  - `param.IsIn` → add `IsReadOnlyAttribute` or `InAttribute` to parameter's attribute list
  - Plain ref → no calling-convention attribute, just `RefKind.Ref`

The old semantic system (`Symbol.ClrCallingConvention`, `TypeProjector`, `CSharpEmitter`) remains unchanged.

## Data Models

### AttributeData

| Field | Type | Description |
|-------|------|-------------|
| AttributeType | IMistType | Resolved reference to the attribute class |
| PositionalArguments | IReadOnlyList<object?> | Constructor argument values |
| NamedArguments | IReadOnlyList<(string, object?)> | Named property/field assignments |

### Parameter (updated)

| Field | Type | Change |
|-------|------|--------|
| AttributesList | List<AttributeData> | **Added** |
| ClrCallingConvention | ~~ClrCallingConvention?~~ | **Removed** |

## Error Handling

- **Unresolvable attribute type**: Skip silently. Do not fail the entire parameter/member projection. This mirrors how `ResolveClrType` already handles unresolvable types by returning null.
- **Exception in GetCustomAttributesData**: Wrap in try/catch, skip the attribute. CLR metadata can throw for malformed assemblies.
- **Empty attribute data**: Parameters/members with no attributes have an empty `AttributesList` — no null checks needed.

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system — essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

### Property 1: Attribute projection completeness

*For any* CLR parameter whose custom attributes all have resolvable types via the `resolveType` callback, the projected ProgramModel Parameter's `AttributesList` SHALL contain exactly one `AttributeData` entry per CLR attribute, with matching `AttributeType`.

**Validates: Requirements 4.1, 4.2**

### Property 2: Graceful degradation on unresolvable attributes

*For any* CLR parameter where some attribute types are unresolvable (resolveType returns null), the projected Parameter SHALL still be created successfully, and its `AttributesList` SHALL contain entries only for the resolvable attributes (no crash, no parameter skipping).

**Validates: Requirements 4.3**

### Property 3: Attribute type identity preservation

*For any* projected attribute, the `AttributeData.AttributeType` SHALL be the exact `IMistType` instance returned by the `resolveType` callback for that attribute's namespace and name — preserving referential identity within the type graph.

**Validates: Requirements 4.2**
