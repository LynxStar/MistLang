// Feature: stack-only-types, Property 18: Arbitrary CLR ref structs keep projecting as ref structs
//
// **Validates: Requirements 9.1**
//
// In-process; assert any IsByRefLike CLR type that is not System.Span/System.ReadOnlySpan
// projects to a StructType{IsRefStruct=true} with no Standard.* identity.

using FsCheck;
using FsCheck.Fluent;
using Mist.Backends.CSharpTranspiler;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Property 18: Arbitrary CLR ref structs keep projecting as ref structs.
/// For any IsByRefLike CLR type that is not System.Span`1 or System.ReadOnlySpan`1,
/// projection via TypeProjector.ProjectType yields a StructType with IsRefStruct == true
/// that does not carry a Standard.* namespace identity.
///
/// **Validates: Requirements 9.1**
/// </summary>
public sealed class ClrRefStructProjectionPropertyTests
{
    private readonly TypeProjector projector;
    private readonly IStdlibTypeProvider stdlib;

    /// <summary>
    /// All IsByRefLike CLR types from the InteropFixtures assembly that are NOT
    /// System.Span or System.ReadOnlySpan. These exercise the ProjectStruct path
    /// that sets IsRefStruct=true for arbitrary CLR ref structs.
    /// </summary>
    private static readonly Type[] RefStructFixtureTypes = BuildRefStructFixtureTypes();

    public ClrRefStructProjectionPropertyTests()
    {
        var intrinsicMap = new IntrinsicTypeMap();
        var filter = new VisibilityFilter();
        projector = new TypeProjector(intrinsicMap, filter);
        stdlib = FallbackStdlibTypeProvider.Instance;
    }

    private static Type[] BuildRefStructFixtureTypes()
    {
        // Collect IsByRefLike types from the InteropFixtures assembly
        var fixtureAssembly = typeof(InteropFixtures.RefStructType).Assembly;
        var fixtureRefStructs = fixtureAssembly.GetExportedTypes()
            .Where(IsNonSpanRefStruct)
            .ToArray();

        return fixtureRefStructs;
    }

    /// <summary>
    /// Returns true if a CLR type is IsByRefLike but NOT System.Span`1 or
    /// System.ReadOnlySpan`1 (those are diverted to SpanType projection).
    /// </summary>
    private static bool IsNonSpanRefStruct(Type t)
    {
        if(!t.IsByRefLike)
        {
            return false;
        }

        // Exclude System.Span<> and System.ReadOnlySpan<> (they project to SpanType)
        if(t.IsConstructedGenericType)
        {
            var genericDef = t.GetGenericTypeDefinition();
            if(genericDef == typeof(Span<>) || genericDef == typeof(ReadOnlySpan<>))
            {
                return false;
            }
        }

        // Also exclude the open generic definitions themselves
        if(t.IsGenericTypeDefinition)
        {
            var fullName = t.FullName;
            if(fullName == "System.Span`1" || fullName == "System.ReadOnlySpan`1")
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Generates a random index into the RefStructFixtureTypes array.
    /// </summary>
    private static Gen<Type> GenRefStructType()
    {
        return Gen.Choose(0, RefStructFixtureTypes.Length - 1)
            .Select(idx => RefStructFixtureTypes[idx]);
    }

    private static void RunProperty<T>(Gen<T> gen, Action<T> check, int maxTests = 100)
    {
        var samples = gen.Sample(maxTests).ToList();
        for(var idx = 0; idx < samples.Count; idx++)
        {
            try
            {
                check(samples[idx]);
            }
            catch(Exception ex)
            {
                throw new Exception(
                    $"Property failed on sample {idx}/{samples.Count}: {samples[idx]}\n" +
                    $"Inner: {ex.Message}", ex);
            }
        }
    }

    // ── Property 18: Arbitrary CLR ref structs keep projecting as ref structs ──
    //
    // For any IsByRefLike CLR type that is not System.Span`1 or
    // System.ReadOnlySpan`1, ProjectType yields a StructType with
    // IsRefStruct == true and no Standard.* identity.

    [Fact]
    public void P18_ArbitraryClrRefStructs_ProjectAsRefStructsWithNoStandardIdentity()
    {
        RunProperty(GenRefStructType(), clrType =>
        {
            var (projectedType, _) = projector.ProjectType(clrType, stdlib);

            // Must project to a StructType (not SpanType or any other IType)
            Assert.True(
                projectedType is StructType,
                $"Expected StructType for IsByRefLike type '{clrType.FullName}' " +
                $"but got {projectedType.GetType().Name} (DisplayName: {projectedType.DisplayName})");

            var structType = (StructType)projectedType;

            // IsRefStruct must be true
            Assert.True(
                structType.IsRefStruct,
                $"Expected IsRefStruct=true for IsByRefLike type '{clrType.FullName}' " +
                $"but got IsRefStruct=false");

            // Must NOT have a Standard.* namespace identity — these are arbitrary
            // CLR ref structs, not Mist standard library intrinsics.
            Assert.True(
                structType.ContainingNamespace != "Standard",
                $"Expected no Standard.* identity for '{clrType.FullName}' " +
                $"but got ContainingNamespace='Standard'");
        });
    }

    [Fact]
    public void P18_RefStructFixtureTypes_AreNonEmpty()
    {
        // Sanity check: ensure we have at least one non-Span ref struct fixture type
        Assert.True(
            RefStructFixtureTypes.Length > 0,
            "Expected at least one IsByRefLike fixture type (not Span/ReadOnlySpan) " +
            "in the InteropFixtures assembly.");
    }
}