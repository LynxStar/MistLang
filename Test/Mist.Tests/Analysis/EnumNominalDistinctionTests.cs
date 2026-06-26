// Feature: enums, Task 5.4: Nominal type distinction
// **Validates: Requirement 2.4**
//
// Verifies that two EnumType instances with different names are never
// assignable to each other, even when their underlying types are identical.
// This is enforced naturally by the existing type checker logic:
//   1. Two different EnumType instances are not ReferenceEquals.
//   2. They have different DisplayName values.
//   3. The only enum conversion rule is enum → underlying type (Req 7.1),
//      NOT enum → different enum.
//   4. No implicit cast operators exist between enum types.

using Mist.Analysis.Builtin;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Xunit;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests verifying nominal type distinction for enums.
/// Two EnumType instances with different names must never be assignable
/// to each other, even if their underlying types are identical.
/// </summary>
public class EnumNominalDistinctionTests
{
    private static EnumType MakeEnum(string name, IType underlyingType)
    {
        var scope = new Scope(parent: null);
        var enumType = new EnumType(name, scope, containingNamespace: null)
        {
            UnderlyingType = underlyingType,
            Members = Array.Empty<EnumMemberInfo>(),
        };
        return enumType;
    }

    /// <summary>
    /// Creates a simple struct type to serve as an underlying type for testing.
    /// </summary>
    private static StructType MakeStructType(string name)
    {
        return new StructType(name, new Scope(parent: null), containingNamespace: null);
    }

    [Fact]
    public void DifferentEnums_SameUnderlyingInt_NotAssignable()
    {
        // Arrange: two enums with the same underlying type
        var intType = MakeStructType("Int32");
        var enumA = MakeEnum("Direction", intType);
        var enumB = MakeEnum("Priority", intType);

        // Act & Assert: neither direction is assignable
        Assert.False(
            TypeCheckerAnalyzer.IsAssignable(enumA, enumB),
            "EnumType 'Direction' should NOT be assignable to EnumType 'Priority'");
        Assert.False(
            TypeCheckerAnalyzer.IsAssignable(enumB, enumA),
            "EnumType 'Priority' should NOT be assignable to EnumType 'Direction'");
    }

    [Fact]
    public void DifferentEnums_SameUnderlyingString_NotAssignable()
    {
        // Arrange: two enums wrapping the same string type
        var stringType = MakeStructType("String");
        var enumA = MakeEnum("Color", stringType);
        var enumB = MakeEnum("Season", stringType);

        // Act & Assert
        Assert.False(
            TypeCheckerAnalyzer.IsAssignable(enumA, enumB),
            "EnumType 'Color' should NOT be assignable to EnumType 'Season'");
        Assert.False(
            TypeCheckerAnalyzer.IsAssignable(enumB, enumA),
            "EnumType 'Season' should NOT be assignable to EnumType 'Color'");
    }

    [Fact]
    public void DifferentEnums_SameUnderlyingByte_NotAssignable()
    {
        // Arrange: two name-only enums (both default to byte underlying type)
        var byteType = MakeStructType("Byte");
        var enumA = MakeEnum("Status", byteType);
        var enumB = MakeEnum("Level", byteType);

        // Act & Assert
        Assert.False(
            TypeCheckerAnalyzer.IsAssignable(enumA, enumB),
            "EnumType 'Status' should NOT be assignable to EnumType 'Level'");
        Assert.False(
            TypeCheckerAnalyzer.IsAssignable(enumB, enumA),
            "EnumType 'Level' should NOT be assignable to EnumType 'Status'");
    }

    [Fact]
    public void SameEnum_IsAssignableToItself()
    {
        // Arrange: same enum instance
        var intType = MakeStructType("Int32");
        var enumA = MakeEnum("Direction", intType);

        // Act & Assert: identity is always assignable (ReferenceEquals)
        Assert.True(
            TypeCheckerAnalyzer.IsAssignable(enumA, enumA),
            "An EnumType should be assignable to itself (identity)");
    }

    [Fact]
    public void DifferentEnums_NotDirectlyAssignable()
    {
        // Arrange
        var intType = MakeStructType("Int32");
        var enumA = MakeEnum("Direction", intType);
        var enumB = MakeEnum("Priority", intType);

        // Act & Assert: IsDirectlyAssignable also rejects
        Assert.False(
            TypeCheckerAnalyzer.IsDirectlyAssignable(enumA, enumB, model: null),
            "EnumType 'Direction' should NOT be directly assignable to EnumType 'Priority'");
        Assert.False(
            TypeCheckerAnalyzer.IsDirectlyAssignable(enumB, enumA, model: null),
            "EnumType 'Priority' should NOT be directly assignable to EnumType 'Direction'");
    }

    [Fact]
    public void Enum_IsAssignableToUnderlyingType_ButNotViceVersa()
    {
        // Arrange
        var intType = MakeStructType("Int32");
        var enumA = MakeEnum("Direction", intType);

        // Act & Assert: enum → underlying is allowed (Req 7.1)
        Assert.True(
            TypeCheckerAnalyzer.IsAssignable(enumA, intType),
            "EnumType should be assignable to its underlying type");

        // Act & Assert: underlying → enum is rejected (Req 7.3)
        Assert.False(
            TypeCheckerAnalyzer.IsAssignable(intType, enumA),
            "Underlying type should NOT be assignable to EnumType");
    }

    [Fact]
    public void DifferentEnums_SameUnderlyingInstance_NotAssignable()
    {
        // Arrange: both enums share the exact same underlying type instance
        // This is the strongest test — even reference-equal underlying types
        // don't make different enums assignable to each other.
        var sharedUnderlying = MakeStructType("Int32");
        var enumA = MakeEnum("Weekday", sharedUnderlying);
        var enumB = MakeEnum("Month", sharedUnderlying);

        // Act & Assert
        Assert.False(
            TypeCheckerAnalyzer.IsAssignable(enumA, enumB),
            "EnumType 'Weekday' should NOT be assignable to EnumType 'Month' even with shared underlying instance");
        Assert.False(
            TypeCheckerAnalyzer.IsAssignable(enumB, enumA),
            "EnumType 'Month' should NOT be assignable to EnumType 'Weekday' even with shared underlying instance");
    }
}