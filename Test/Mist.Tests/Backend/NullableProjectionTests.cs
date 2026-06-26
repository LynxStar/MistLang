using Mist.Backends.CSharpTranspiler;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests for nullable projection in <see cref="TypeProjector.ProjectNullableType"/>.
/// Validates Requirements 14.1, 14.2, 14.5, 14.6.
/// </summary>
public class NullableProjectionTests
{
    private readonly TypeProjector projector;
    private readonly FallbackStdlibTypeProvider stdlib = FallbackStdlibTypeProvider.Instance;

    public NullableProjectionTests()
    {
        var intrinsicMap = new IntrinsicTypeMap();
        var filter = new VisibilityFilter();
        projector = new TypeProjector(intrinsicMap, filter);
    }

    // ── Requirement 14.1: Nullable<T> → T | void with NullableValueType origin ──

    [Fact]
    public void ProjectNullableType_NullableInt_ReturnsUnionWithNullableValueTypeOrigin()
    {
        var clrType = typeof(int?);

        var result = projector.ProjectNullableType(clrType, stdlib);

        var union = Assert.IsType<UnionType>(result);
        Assert.Equal(2, union.Constituents.Count);
        Assert.NotNull(union.NullableProjection);
        Assert.Equal(NullableOrigin.NullableValueType, union.NullableProjection!.Origin);
    }

    [Fact]
    public void ProjectNullableType_NullableInt_UnionContainsIntAndVoid()
    {
        var clrType = typeof(int?);

        var result = projector.ProjectNullableType(clrType, stdlib);

        var union = Assert.IsType<UnionType>(result);
        var displayNames = union.Constituents.Select(c => c.DisplayName).ToList();
        Assert.Contains("int", displayNames);
        Assert.Contains("void", displayNames);
    }

    [Fact]
    public void ProjectNullableType_NullableInt_InnerTypeIsInt()
    {
        var clrType = typeof(int?);

        var result = projector.ProjectNullableType(clrType, stdlib);

        var union = Assert.IsType<UnionType>(result);
        Assert.NotNull(union.NullableProjection);
        Assert.Equal("int", union.NullableProjection!.InnerType.DisplayName);
    }

    [Fact]
    public void ProjectNullableType_NullableBool_ReturnsUnionWithNullableValueTypeOrigin()
    {
        var clrType = typeof(bool?);

        var result = projector.ProjectNullableType(clrType, stdlib);

        var union = Assert.IsType<UnionType>(result);
        Assert.NotNull(union.NullableProjection);
        Assert.Equal(NullableOrigin.NullableValueType, union.NullableProjection!.Origin);
        var displayNames = union.Constituents.Select(c => c.DisplayName).ToList();
        Assert.Contains("bool", displayNames);
        Assert.Contains("void", displayNames);
    }

    // ── Requirement 14.2: Reference type → T | void with ReferenceType origin ──

    [Fact]
    public void ProjectNullableType_String_ReturnsUnionWithReferenceTypeOrigin()
    {
        var clrType = typeof(string);

        var result = projector.ProjectNullableType(clrType, stdlib);

        var union = Assert.IsType<UnionType>(result);
        Assert.Equal(2, union.Constituents.Count);
        Assert.NotNull(union.NullableProjection);
        Assert.Equal(NullableOrigin.ReferenceType, union.NullableProjection!.Origin);
    }

    [Fact]
    public void ProjectNullableType_String_UnionContainsStringAndVoid()
    {
        var clrType = typeof(string);

        var result = projector.ProjectNullableType(clrType, stdlib);

        var union = Assert.IsType<UnionType>(result);
        var displayNames = union.Constituents.Select(c => c.DisplayName).ToList();
        Assert.Contains("string", displayNames);
        Assert.Contains("void", displayNames);
    }

    [Fact]
    public void ProjectNullableType_ClassType_ReturnsUnionWithReferenceTypeOrigin()
    {
        // Use a non-intrinsic class type from the fixture assembly
        var clrType = typeof(Mist.Tests.InteropFixtures.SimpleClass);

        var result = projector.ProjectNullableType(clrType, stdlib);

        var union = Assert.IsType<UnionType>(result);
        Assert.NotNull(union.NullableProjection);
        Assert.Equal(NullableOrigin.ReferenceType, union.NullableProjection!.Origin);
    }

    [Fact]
    public void ProjectNullableType_Interface_ReturnsUnionWithReferenceTypeOrigin()
    {
        var clrType = typeof(Mist.Tests.InteropFixtures.ISimpleInterface);

        var result = projector.ProjectNullableType(clrType, stdlib);

        var union = Assert.IsType<UnionType>(result);
        Assert.NotNull(union.NullableProjection);
        Assert.Equal(NullableOrigin.ReferenceType, union.NullableProjection!.Origin);
    }

    [Fact]
    public void ProjectNullableType_Array_ReturnsUnionWithReferenceTypeOrigin()
    {
        var clrType = typeof(int[]);

        var result = projector.ProjectNullableType(clrType, stdlib);

        var union = Assert.IsType<UnionType>(result);
        Assert.NotNull(union.NullableProjection);
        Assert.Equal(NullableOrigin.ReferenceType, union.NullableProjection!.Origin);
    }

    [Fact]
    public void ProjectNullableType_ReferenceType_InnerTypeMatchesProjectedType()
    {
        var clrType = typeof(Mist.Tests.InteropFixtures.SimpleClass);

        var result = projector.ProjectNullableType(clrType, stdlib);

        var union = Assert.IsType<UnionType>(result);
        Assert.NotNull(union.NullableProjection);
        Assert.Equal("SimpleClass", union.NullableProjection!.InnerType.DisplayName);
    }

    // ── Requirement 14.5: Non-nullable value type → plain type (no union) ──

    [Fact]
    public void ProjectNullableType_PlainInt_ReturnsNonUnionType()
    {
        var clrType = typeof(int);

        var result = projector.ProjectNullableType(clrType, stdlib);

        Assert.IsNotType<UnionType>(result);
    }

    [Fact]
    public void ProjectNullableType_PlainInt_ReturnsIntrinsicSubstitutedType()
    {
        var clrType = typeof(int);

        var result = projector.ProjectNullableType(clrType, stdlib);

        // int is an intrinsic type, so it should be substituted to the stdlib fallback
        Assert.Equal("int", result.DisplayName);
    }

    [Fact]
    public void ProjectNullableType_PlainBool_ReturnsNonUnionType()
    {
        var clrType = typeof(bool);

        var result = projector.ProjectNullableType(clrType, stdlib);

        Assert.IsNotType<UnionType>(result);
    }

    [Fact]
    public void ProjectNullableType_PlainStruct_ReturnsNonUnionType()
    {
        var clrType = typeof(Mist.Tests.InteropFixtures.SimpleStruct);

        var result = projector.ProjectNullableType(clrType, stdlib);

        Assert.IsNotType<UnionType>(result);
        Assert.Equal("SimpleStruct", result.DisplayName);
    }

    [Fact]
    public void ProjectNullableType_Enum_ReturnsNonUnionType()
    {
        var clrType = typeof(Mist.Tests.InteropFixtures.SimpleEnum);

        var result = projector.ProjectNullableType(clrType, stdlib);

        Assert.IsNotType<UnionType>(result);
    }

    // ── Requirement 14.6: Origin tracking for emitter use ──

    [Fact]
    public void ProjectNullableType_NullableValueType_OriginDistinguishableFromReferenceType()
    {
        var nullableInt = typeof(int?);
        var stringType = typeof(string);

        var nullableResult = projector.ProjectNullableType(nullableInt, stdlib);
        var referenceResult = projector.ProjectNullableType(stringType, stdlib);

        var nullableUnion = Assert.IsType<UnionType>(nullableResult);
        var referenceUnion = Assert.IsType<UnionType>(referenceResult);

        Assert.Equal(NullableOrigin.NullableValueType, nullableUnion.NullableProjection!.Origin);
        Assert.Equal(NullableOrigin.ReferenceType, referenceUnion.NullableProjection!.Origin);
    }

    [Fact]
    public void ProjectNullableType_NonNullableValueType_HasNoNullableProjection()
    {
        var clrType = typeof(int);

        var result = projector.ProjectNullableType(clrType, stdlib);

        // Non-union types don't have NullableProjection metadata
        Assert.IsNotType<UnionType>(result);
    }
}