// Feature: unknown-type (Phase B, Task 10.1 + 10.2)
// **Validates: Requirements 5.1, 5.2, 5.3, 7.5, 7.6**
//
// Unit tests and a property-based test for the TypeContainment helper class,
// which exposes:
//
//   * ContainsType(haystack, needle) — structural containment predicate.
//   * IsStorageBanned(type)          — ContainsType(type, UnknownType.Instance).
//
// The helpers live in Source/Mist/Semantics/Model/Types/TypeContainment.cs
// and run entirely on the in-memory IType model. No compilation, no process
// spawning (per the workspace test-process-spawning policy).

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Tests for <see cref="TypeContainment.IsStorageBanned"/> and
/// <see cref="TypeContainment.ContainsType"/>.
///
/// <para>
/// <strong>Property 3 (unknown-type feature):</strong>
/// <c>IsStorageBanned</c> is monotonic under <c>unknown</c> substitution. For
/// any <see cref="IType"/> <c>t</c> built from a pool that does not contain
/// <see cref="UnknownType"/>, <c>IsStorageBanned(t)</c> is <c>false</c>; if
/// <c>t</c> has at least one substitution point (an array element or a type
/// argument at any depth), replacing any such position with
/// <see cref="UnknownType.Instance"/> makes <c>IsStorageBanned</c> become
/// <c>true</c>.
/// <strong>Validates: Requirements 5.1, 7.5, 7.6</strong>
/// </para>
/// </summary>
public class TypeContainmentTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Shared fixture — stable ClassType/StructType singletons.
    //
    // The helpers under test use ReferenceEquals for non-type-parameter
    // types, so reusing the same ClassType/StructType singletons across
    // generated samples is what makes substitution-equality meaningful
    // in the property test below.
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly ClassType ListDef = BuildGenericClass("List", arity: 1);
    private static readonly ClassType MapDef = BuildGenericClass("Map", arity: 2);
    private static readonly StructType OptionalDef = BuildGenericStruct("Optional", arity: 1);

    private static readonly ClassType SampleClass =
        new("SampleClass", new Scope(parent: null), containingNamespace: "TestPool");

    private static readonly StructType SampleStruct =
        new("SampleStruct", new Scope(parent: null), containingNamespace: "TestPool");

    private static ClassType BuildGenericClass(string name, int arity)
    {
        var c = new ClassType(name, new Scope(parent: null), containingNamespace: "TestPool");
        var tps = new TypeParameterType[arity];
        for (var i = 0; i < arity; i++)
            tps[i] = new TypeParameterType($"T{i}", i);
        c.TypeParameters = tps;
        return c;
    }

    private static StructType BuildGenericStruct(string name, int arity)
    {
        var s = new StructType(name, new Scope(parent: null), containingNamespace: "TestPool");
        var tps = new TypeParameterType[arity];
        for (var i = 0; i < arity; i++)
            tps[i] = new TypeParameterType($"T{i}", i);
        s.TypeParameters = tps;
        return s;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Targeted unit tests — pin down each case the design calls out.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Int_IsNotStorageBanned()
    {
        Assert.False(TypeContainment.IsStorageBanned(LegacyPrimitiveTypes.Int));
    }

    [Fact]
    public void Unknown_IsStorageBanned()
    {
        Assert.True(TypeContainment.IsStorageBanned(UnknownType.Instance));
    }

    [Fact]
    public void Array_Of_Int_IsNotStorageBanned()
    {
        Assert.False(TypeContainment.IsStorageBanned(new ArrayType(LegacyPrimitiveTypes.Int)));
    }

    [Fact]
    public void Array_Of_Unknown_IsStorageBanned()
    {
        Assert.True(TypeContainment.IsStorageBanned(new ArrayType(UnknownType.Instance)));
    }

    [Fact]
    public void List_Of_Int_IsNotStorageBanned()
    {
        var listOfInt = new ConstructedType(ListDef, new IType[] { LegacyPrimitiveTypes.Int });
        Assert.False(TypeContainment.IsStorageBanned(listOfInt));
    }

    [Fact]
    public void List_Of_Unknown_IsStorageBanned()
    {
        var listOfUnknown = new ConstructedType(ListDef, new IType[] { UnknownType.Instance });
        Assert.True(TypeContainment.IsStorageBanned(listOfUnknown));
    }

    [Fact]
    public void Map_Of_String_Unknown_IsStorageBanned()
    {
        var mapOfStringUnknown = new ConstructedType(
            MapDef, new IType[] { LegacyPrimitiveTypes.String, UnknownType.Instance });
        Assert.True(TypeContainment.IsStorageBanned(mapOfStringUnknown));
    }

    [Fact]
    public void Map_Of_Unknown_Int_IsStorageBanned()
    {
        var mapOfUnknownInt = new ConstructedType(
            MapDef, new IType[] { UnknownType.Instance, LegacyPrimitiveTypes.Int });
        Assert.True(TypeContainment.IsStorageBanned(mapOfUnknownInt));
    }

    [Fact]
    public void List_Of_Optional_Of_Unknown_IsStorageBanned_RecursiveCascade()
    {
        // List<Optional<unknown>> — the spec's Req 7.6 recursive-cascade case.
        var optionalOfUnknown = new ConstructedType(
            OptionalDef, new IType[] { UnknownType.Instance });
        var listOfOptionalOfUnknown = new ConstructedType(
            ListDef, new IType[] { optionalOfUnknown });
        Assert.True(TypeContainment.IsStorageBanned(listOfOptionalOfUnknown));
    }

    // ─── ContainsType — direct coverage of the general helper ─────────────

    [Fact]
    public void ContainsType_Finds_Needle_At_Root()
    {
        Assert.True(TypeContainment.ContainsType(
            LegacyPrimitiveTypes.Int, LegacyPrimitiveTypes.Int));
    }

    [Fact]
    public void ContainsType_Does_Not_Match_Unrelated_Singleton()
    {
        Assert.False(TypeContainment.ContainsType(
            LegacyPrimitiveTypes.Int, LegacyPrimitiveTypes.Bool));
    }

    [Fact]
    public void ContainsType_Matches_TypeParameter_By_Name_And_Ordinal()
    {
        // TypeParameterType equality in the helper is (Name, Ordinal),
        // mirroring the Type Checker's TypesEqual rule. A fresh TPT
        // instance with the same Name and Ordinal must match.
        var needle = new TypeParameterType("T", 0);
        var haystackTp = new TypeParameterType("T", 0);
        var wrapped = new ConstructedType(ListDef, new IType[] { haystackTp });
        Assert.True(TypeContainment.ContainsType(wrapped, needle));
    }

    [Fact]
    public void ContainsType_Rejects_TypeParameter_With_Different_Ordinal()
    {
        var needle = new TypeParameterType("T", 0);
        var haystackTp = new TypeParameterType("T", 1);  // same name, different ordinal
        var wrapped = new ConstructedType(ListDef, new IType[] { haystackTp });
        Assert.False(TypeContainment.ContainsType(wrapped, needle));
    }

    [Fact]
    public void ContainsType_Descends_Into_Array_Element()
    {
        var needle = new TypeParameterType("T", 0);
        var arrayOfT = new ArrayType(new TypeParameterType("T", 0));
        Assert.True(TypeContainment.ContainsType(arrayOfT, needle));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3 — IsStorageBanned is monotonic under unknown substitution.
    //
    // The pool of "clean" types is built exclusively from non-unknown
    // components. Property 3 has two conjuncts:
    //
    //   (a) For every generated type t, IsStorageBanned(t) is false.
    //   (b) For every generated type t that has at least one substitution
    //       point (array element or type argument at any depth), the type
    //       obtained by replacing any one such point with UnknownType.Instance
    //       satisfies IsStorageBanned == true.
    //
    // **Validates: Requirements 5.1, 7.5, 7.6**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// One step in a path into an IType tree. A path of steps navigates from
    /// a root IType to a specific sub-type that can be replaced with
    /// UnknownType.Instance to produce a substituted type.
    /// </summary>
#pragma warning disable CA1034 // Public-through-record nested types are standard FsCheck fixture shape in this test suite.
    public enum StepKind { ArrayElement, TypeArgument }

    public readonly record struct PathStep(StepKind Kind, int TypeArgumentIndex);
#pragma warning restore CA1034

    /// <summary>
    /// A candidate for the property test: a clean IType <c>t</c> (contains no
    /// <see cref="UnknownType"/>) together with the full list of substitution
    /// paths inside it. The test picks one path per sample and asserts the
    /// substituted type is storage-banned.
    /// </summary>
#pragma warning disable CA1034 // Nested type is a test fixture DTO, consistent with other PBTs in this repo.
    public sealed record StorageBanScenario(
        IType CleanType,
        ImmutableArray<ImmutableArray<PathStep>> SubstitutionPaths)
    {
        public override string ToString() =>
            $"{CleanType.DisplayName} (paths={SubstitutionPaths.Length})";
    }
#pragma warning restore CA1034

    // Pool of clean "leaf" types — no unknown, no type parameter, flat.
    private static readonly IType[] CleanLeafTypes =
    {
        LegacyPrimitiveTypes.Int,
        LegacyPrimitiveTypes.Bool,
        LegacyPrimitiveTypes.String,
        LegacyPrimitiveTypes.Float,
        SampleClass,
        SampleStruct,
    };

    /// <summary>
    /// Generates a clean (unknown-free) IType tree up to the supplied depth,
    /// drawn from the constructed/array/leaf alternatives.
    /// </summary>
    private static Gen<IType> GenCleanType(int depth)
    {
        if (depth <= 0)
            return Gen.Elements(CleanLeafTypes);

        // Branching: leaf, array-of-cleaner, List<cleaner>, Map<cleaner, cleaner>,
        // Optional<cleaner>. Each recursive case reduces depth by 1 to bound the
        // tree size.
        var leaf = Gen.Elements(CleanLeafTypes);
        var array = GenCleanType(depth - 1).Select(e => (IType)new ArrayType(e));
        var list = GenCleanType(depth - 1)
            .Select(t => (IType)new ConstructedType(ListDef, new[] { t }));
        var map = from k in GenCleanType(depth - 1)
                  from v in GenCleanType(depth - 1)
                  select (IType)new ConstructedType(MapDef, new[] { k, v });
        var opt = GenCleanType(depth - 1)
            .Select(t => (IType)new ConstructedType(OptionalDef, new[] { t }));

        return Gen.OneOf(leaf, array, list, map, opt);
    }

    /// <summary>
    /// Collects every substitution path inside <paramref name="t"/>: a path
    /// is the sequence of steps from the root to a node that will be replaced
    /// by <see cref="UnknownType.Instance"/>. Leaves of the walk (primitives,
    /// classes, structs) contribute nothing; arrays contribute a path ending
    /// at ArrayElement plus the deeper paths through their element; generic
    /// constructions contribute a path ending at each type-argument index
    /// plus the deeper paths through each argument.
    /// </summary>
    private static IEnumerable<ImmutableArray<PathStep>> EnumerateSubstitutionPaths(IType t)
    {
        switch (t)
        {
            case ArrayType array:
                // Direct substitution at the element level.
                yield return ImmutableArray.Create(new PathStep(StepKind.ArrayElement, -1));
                // Deeper substitutions inside the element.
                foreach (var sub in EnumerateSubstitutionPaths(array.ElementType))
                    yield return ImmutableArray.Create(new PathStep(StepKind.ArrayElement, -1))
                        .AddRange(sub);
                break;

            case ConstructedType constructed:
                for (var i = 0; i < constructed.TypeArguments.Count; i++)
                {
                    // Direct substitution at position i.
                    yield return ImmutableArray.Create(new PathStep(StepKind.TypeArgument, i));
                    // Deeper substitutions inside the i-th argument.
                    foreach (var sub in EnumerateSubstitutionPaths(constructed.TypeArguments[i]))
                        yield return ImmutableArray.Create(new PathStep(StepKind.TypeArgument, i))
                            .AddRange(sub);
                }
                break;

            default:
                // Leaf — no substitution points.
                yield break;
        }
    }

    /// <summary>
    /// Rewrites <paramref name="t"/> so that the sub-tree reached by
    /// <paramref name="path"/> is replaced with
    /// <see cref="UnknownType.Instance"/>. Every intermediate
    /// <see cref="ArrayType"/> / <see cref="ConstructedType"/> is
    /// rebuilt so the returned type is a new tree whose only change
    /// is at the target position.
    /// </summary>
    private static IType SubstituteAtPath(IType t, ReadOnlySpan<PathStep> path)
    {
        if (path.Length == 0)
            return UnknownType.Instance;

        var head = path[0];
        var tail = path.Slice(1);

        switch (t)
        {
            case ArrayType array when head.Kind == StepKind.ArrayElement:
                return new ArrayType(SubstituteAtPath(array.ElementType, tail));

            case ConstructedType constructed when head.Kind == StepKind.TypeArgument:
                var newArgs = new IType[constructed.TypeArguments.Count];
                for (var i = 0; i < newArgs.Length; i++)
                {
                    newArgs[i] = i == head.TypeArgumentIndex
                        ? SubstituteAtPath(constructed.TypeArguments[i], tail)
                        : constructed.TypeArguments[i];
                }
                return new ConstructedType(constructed.GenericDefinition, newArgs);

            default:
                throw new InvalidOperationException(
                    $"Path step {head.Kind} is incompatible with type {t.GetType().Name}.");
        }
    }

    /// <summary>
    /// FsCheck arbitrary that draws a clean type up to depth 3 and precomputes
    /// its substitution paths for the property test.
    /// </summary>
    public static Arbitrary<StorageBanScenario> ArbitraryStorageBanScenario()
    {
        var gen = from t in GenCleanType(depth: 3)
                  let paths = EnumerateSubstitutionPaths(t)
                      .Select(p => p.ToImmutableArray())
                      .ToImmutableArray()
                  select new StorageBanScenario(t, paths);
        return gen.ToArbitrary();
    }

    /// <summary>
    /// Property 3: IsStorageBanned is monotonic under <c>unknown</c>
    /// substitution.
    ///
    /// <para>
    /// Two conjuncts are checked for every generated sample:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// The clean type is not storage-banned (it contains no
    /// <see cref="UnknownType"/>).
    /// </description></item>
    /// <item><description>
    /// For <em>every</em> substitution path, the rewritten type — with
    /// <see cref="UnknownType.Instance"/> planted at that path — is
    /// storage-banned. This is the strongest form of the property: the
    /// rule must fire no matter which position the <c>unknown</c> lands
    /// at, including nested-depth positions (Req 7.6).
    /// </description></item>
    /// </list>
    ///
    /// <para>
    /// <strong>Validates: Requirements 5.1, 7.5, 7.6</strong>
    /// </para>
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TypeContainmentTests) }, MaxTest = 100)]
    public Property IsStorageBanned_IsMonotonic_Under_Unknown_Substitution(StorageBanScenario scenario)
    {
        // Conjunct (a): the generated type is clean.
        var cleanIsNotBanned = !TypeContainment.IsStorageBanned(scenario.CleanType);
        if (!cleanIsNotBanned)
        {
            return false.ToProperty()
                .Label($"Expected IsStorageBanned(clean) == false, was true. Clean type: {scenario.CleanType.DisplayName}");
        }

        // Conjunct (b): every substitution path yields a storage-banned type.
        // If the scenario has no substitution paths (leaf case), (a) alone
        // is the full property — skip (b) by returning true for the empty
        // quantifier.
        foreach (var path in scenario.SubstitutionPaths)
        {
            var substituted = SubstituteAtPath(scenario.CleanType, path.AsSpan());
            if (!TypeContainment.IsStorageBanned(substituted))
            {
                return false.ToProperty()
                    .Label(
                        $"Expected IsStorageBanned(substituted) == true for path of length {path.Length}. " +
                        $"Clean: {scenario.CleanType.DisplayName}, Substituted: {substituted.DisplayName}");
            }
        }

        return true.ToProperty();
    }
}
