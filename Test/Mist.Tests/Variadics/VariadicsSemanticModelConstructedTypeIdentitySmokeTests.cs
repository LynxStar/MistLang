// Feature: variadics - Smoke test for classic (legacy Mist.Semantics) constructed
// variadic type identity (Task 9.3). Requirements: 5.1, 5.2, 5.3, 5.4.
//
// A constructed variadic type is identified by the pair (family, the comptime Type[]
// value bound to its pack slot). In the legacy model the pack's Type[] value rides on a
// DependentValueArgument (DependentValueArgument.ForPack / PackElementTypes) at the pack
// slot of a ConstructedType, and identity is computed STRUCTURALLY by the type checker's
// TypeCheckerAnalyzer.TypesEqual -> DependentArgumentsEqual relation (the legacy model does
// not intern constructed types). This mirrors the ProgramModel-track smoke test
// (ConstructedVariadicTypeIdentityTests, task 4.3), which drives the interner instead.
//
// These checks build ConstructedType instances directly and call TypesEqual in-process
// (ZERO external process spawns: nothing is parsed, built, emitted, or run). The full
// FsCheck property test over the classic pipeline is task 9.4.

using Mist.Analysis.Builtin;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Variadics;

/// <summary>
/// Verifies that a constructed variadic type — a family applied to a comptime <c>Type[]</c>
/// value in its pack slot — has identity keyed by (family, that <c>Type[]</c> value) under
/// the legacy <see cref="TypeCheckerAnalyzer.TypesEqual"/> relation, with elementwise,
/// order-sensitive, arity-sensitive equality (Requirements 5.1-5.4).
/// </summary>
public sealed class VariadicsSemanticModelConstructedTypeIdentitySmokeTests
{
    // Shared, reference-stable infrastructure. Element types are the legacy primitive
    // singletons, so "int == int" holds by reference across independently-built packs; the
    // pack-slot placeholder and pack domain are shared so identity is driven purely by the
    // pack's Type[] value.
    private static readonly IType Int = LegacyPrimitiveTypes.Int;
    private static readonly IType String = LegacyPrimitiveTypes.String;
    private static readonly IType Bool = LegacyPrimitiveTypes.Bool;

    // The pack's value domain: the Type[] metatype.
    private static readonly IType PackDomain = new ArrayType(LegacyPrimitiveTypes.Type);

    // The type-argument placeholder occupying the pack slot (the `TArgs` parameter). Shared so
    // the ordinary type-argument comparison is reference-equal and does not affect the result.
    private static readonly TypeParameterType PackSlot =
        new("TArgs", 0) { IsPack = true, IsUnbracketedPack = true };

    private static ClassType Family(string name)
    {
        return new ClassType(name, new Scope(null), containingNamespace: null);
    }

    /// <summary>Builds <c>family&lt;[elements...]&gt;</c>: a single pack slot bound to the given Type[].</summary>
    private static ConstructedType Variadic(ClassType family, params IType[] elements)
    {
        var pack = DependentValueArgument.ForPack(elements, PackDomain);
        return new ConstructedType(
            family,
            new IType[] { PackSlot },
            new DependentValueArgument?[] { pack });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // R5.1 / R5.2: same family + equal Type[] value  =>  same type
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SameFamily_EqualTypeArray_AreEqual()
    {
        var foo = Family("Foo");

        var first = Variadic(foo, Int, String);
        var second = Variadic(foo, Int, String);

        Assert.True(TypeCheckerAnalyzer.TypesEqual(first, second),
            "Foo<[int, string]> built twice from equal Type[] values should be the same type (R5.1, R5.2).");
        Assert.True(TypeCheckerAnalyzer.TypesEqual(second, first), "Type identity must be symmetric.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // R5.3: same family, same element set, different order  =>  distinct
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SameFamily_DifferentOrder_AreDistinct()
    {
        var foo = Family("Foo");

        var intThenString = Variadic(foo, Int, String);
        var stringThenInt = Variadic(foo, String, Int);

        Assert.False(TypeCheckerAnalyzer.TypesEqual(intThenString, stringThenInt),
            "Foo<[int, string]> and Foo<[string, int]> are distinct: order matters (R5.3).");
        Assert.False(TypeCheckerAnalyzer.TypesEqual(stringThenInt, intThenString), "Distinctness must be symmetric.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // R5.4: same family, different pack arity  =>  distinct (incl. empty pack)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SameFamily_DifferentArity_AreDistinct()
    {
        var foo = Family("Foo");

        var two = Variadic(foo, Int, String);
        var three = Variadic(foo, Int, String, Bool);
        var empty = Variadic(foo);

        Assert.False(TypeCheckerAnalyzer.TypesEqual(two, three),
            "Different pack arity (2 vs 3) must be distinct types (R5.4).");
        Assert.False(TypeCheckerAnalyzer.TypesEqual(two, empty),
            "A non-empty pack and the empty pack must be distinct types (R5.4).");
        Assert.False(TypeCheckerAnalyzer.TypesEqual(three, empty),
            "Different pack arity (3 vs 0) must be distinct types (R5.4).");
    }

    [Fact]
    public void EmptyPack_EqualAcrossConstructions_AreEqual()
    {
        var foo = Family("Foo");

        var first = Variadic(foo);
        var second = Variadic(foo);

        Assert.True(TypeCheckerAnalyzer.TypesEqual(first, second),
            "The empty-pack construction Foo<[]> must be a single type (R5.4).");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // R5.1 / R5.2: identity includes the family
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DifferentFamily_SameTypeArray_AreDistinct()
    {
        var foo = Family("Foo");
        var bar = Family("Bar");

        var fooConstruction = Variadic(foo, Int, String);
        var barConstruction = Variadic(bar, Int, String);

        Assert.False(TypeCheckerAnalyzer.TypesEqual(fooConstruction, barConstruction),
            "Foo<[int, string]> and Bar<[int, string]> are distinct: identity includes the family (R5.1, R5.2).");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // A pack Type[] value is never equal to a scalar dependent value at the same slot.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void PackValue_IsNeverEqualToScalarDependentValue()
    {
        var foo = Family("Foo");

        var packConstruction = Variadic(foo, Int);
        var scalarConstruction = new ConstructedType(
            foo,
            new IType[] { PackSlot },
            new DependentValueArgument?[] { new(5, Int, "5") });

        Assert.False(TypeCheckerAnalyzer.TypesEqual(packConstruction, scalarConstruction),
            "A pack Type[] value and a scalar dependent value must never be considered equal.");
        Assert.False(TypeCheckerAnalyzer.TypesEqual(scalarConstruction, packConstruction),
            "Pack-vs-scalar distinctness must be symmetric.");
    }
}