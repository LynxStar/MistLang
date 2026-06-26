// Feature: type-argument-inference, Property 4: Conflicting bindings produce diagnostic
// **Validates: Requirements 3.3, 4.2, 4.3**
//
// For any generic method where the same type parameter appears in multiple
// parameter positions, and for any set of arguments where two positions for
// that type parameter resolve to different concrete types, the inference engine
// SHALL report a conflict and the compiler SHALL emit exactly one diagnostic
// error at the call site.
//
// These tests run entirely in-process on the TypeArgumentInferenceEngine
// directly — no process spawning needed.

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

/// <summary>
/// Represents a scenario where the same type parameter appears in multiple
/// parameter positions but the corresponding arguments have different concrete types,
/// which should cause the inference engine to report a conflict.
/// </summary>
public sealed class ConflictingBindingScenario
{
    /// <summary>The method's type parameters.</summary>
    public IReadOnlyList<IType> TypeParameters { get; }

    /// <summary>The declared parameter types (containing repeated type parameters).</summary>
    public IReadOnlyList<IType> ParameterTypes { get; }

    /// <summary>The resolved argument types at the call site (with conflicting concrete types).</summary>
    public IReadOnlyList<IType> ArgumentTypes { get; }

    /// <summary>The name of the type parameter that should have a conflict.</summary>
    public string ConflictingTypeParameterName { get; }

    /// <summary>Human-readable description for test output.</summary>
    public string Description { get; }

    public ConflictingBindingScenario(
        IReadOnlyList<IType> typeParameters,
        IReadOnlyList<IType> parameterTypes,
        IReadOnlyList<IType> argumentTypes,
        string conflictingTypeParameterName,
        string description)
    {
        TypeParameters = typeParameters;
        ParameterTypes = parameterTypes;
        ArgumentTypes = argumentTypes;
        ConflictingTypeParameterName = conflictingTypeParameterName;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// Property-based tests verifying that conflicting bindings produce inference failure.
///
/// Property 4: Conflicting bindings produce diagnostic
///
/// For any generic method where the same type parameter appears in multiple parameter
/// positions, and for any set of arguments where two positions for that type parameter
/// resolve to different concrete types, the inference engine SHALL report a conflict.
///
/// **Validates: Requirements 3.3, 4.2, 4.3**
/// </summary>
public class TypeArgumentInferenceConflictPropertyTests
{
    // ─── Concrete type pool ──────────────────────────────────────────────
    // Using distinct types with unique DisplayNames so conflicts are unambiguous.

    private static readonly IType[] ConcreteTypes =
    [
        LegacyPrimitiveTypes.Int,
        LegacyPrimitiveTypes.Bool,
        LegacyPrimitiveTypes.Double,
        LegacyPrimitiveTypes.Float,
        LegacyPrimitiveTypes.Char,
        LegacyPrimitiveTypes.Long,
        LegacyPrimitiveTypes.String,
    ];

    // ─── Generic definitions for ConstructedType scenarios ───────────────

    private static readonly ClassType ListDef = BuildGenericClass("List", arity: 1);

    private static ClassType BuildGenericClass(string name, int arity)
    {
        var c = new ClassType(name, new Scope(parent: null), containingNamespace: "TestPool");
        var tps = new TypeParameterType[arity];
        for(var i = 0; i < arity; i++)
            tps[i] = new TypeParameterType($"T{i}", i);
        c.TypeParameters = tps;
        return c;
    }

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a pair of concrete types that are guaranteed to be different
    /// (different DisplayName).
    /// </summary>
    private static Gen<(IType First, IType Second)> GenDistinctTypePair()
    {
        return from firstIdx in Gen.Choose(0, ConcreteTypes.Length - 1)
               from secondIdx in Gen.Choose(0, ConcreteTypes.Length - 2)
               let adjustedSecond = secondIdx >= firstIdx ? secondIdx + 1 : secondIdx
               select (ConcreteTypes[firstIdx], ConcreteTypes[adjustedSecond]);
    }

    /// <summary>
    /// Generates a scenario with a single type parameter T appearing in exactly
    /// two parameter positions, with different concrete argument types.
    /// Example: Method&lt;T&gt;(T a, T b) called with (int, string).
    /// </summary>
    private static Gen<ConflictingBindingScenario> GenSimpleTwoPositionConflict()
    {
        return from pair in GenDistinctTypePair()
               from tpName in Gen.Elements("T", "U", "V", "X")
               select new ConflictingBindingScenario(
                   typeParameters: new IType[] { new TypeParameterType(tpName, 0) },
                   parameterTypes: new IType[]
                   {
                       new TypeParameterType(tpName, 0),
                       new TypeParameterType(tpName, 0),
                   },
                   argumentTypes: new IType[] { pair.First, pair.Second },
                   conflictingTypeParameterName: tpName,
                   description: $"Method<{tpName}>({tpName} a, {tpName} b) called with ({pair.First.DisplayName}, {pair.Second.DisplayName})");
    }

    /// <summary>
    /// Generates a scenario with a single type parameter appearing in three or more
    /// positions, where at least two have different concrete types.
    /// Example: Method&lt;T&gt;(T a, T b, T c) called with (int, int, string).
    /// </summary>
    private static Gen<ConflictingBindingScenario> GenMultiPositionConflict()
    {
        return from pair in GenDistinctTypePair()
               from extraCount in Gen.Choose(1, 3)
               from tpName in Gen.Elements("T", "U", "V")
               let totalParams = 2 + extraCount
               let tp = new TypeParameterType(tpName, 0)
               let paramTypes = Enumerable.Repeat((IType)new TypeParameterType(tpName, 0), totalParams).ToArray()
               let argTypes = new[] { pair.First }
                   .Concat(Enumerable.Repeat(pair.Second, totalParams - 1))
                   .ToArray()
               select new ConflictingBindingScenario(
                   typeParameters: new IType[] { tp },
                   parameterTypes: paramTypes,
                   argumentTypes: argTypes,
                   conflictingTypeParameterName: tpName,
                   description: $"Method<{tpName}>({totalParams} params of {tpName}) called with ({pair.First.DisplayName} then {totalParams - 1}x {pair.Second.DisplayName})");
    }

    /// <summary>
    /// Generates a scenario with multiple type parameters where one has a conflict
    /// and others are fine.
    /// Example: Method&lt;T, U&gt;(T a, U b, T c) called with (int, string, bool)
    /// — T conflicts (int vs bool), U is fine (string).
    /// </summary>
    private static Gen<ConflictingBindingScenario> GenMultiTypeParamWithOneConflict()
    {
        return from conflictPair in GenDistinctTypePair()
               from okType in Gen.Elements(ConcreteTypes)
               from conflictName in Gen.Elements("T", "A", "X")
               from okName in Gen.Elements("U", "B", "Y")
               where conflictName != okName
               select new ConflictingBindingScenario(
                   typeParameters: new IType[]
                   {
                       new TypeParameterType(conflictName, 0),
                       new TypeParameterType(okName, 1),
                   },
                   parameterTypes: new IType[]
                   {
                       new TypeParameterType(conflictName, 0),
                       new TypeParameterType(okName, 1),
                       new TypeParameterType(conflictName, 0),
                   },
                   argumentTypes: new IType[] { conflictPair.First, okType, conflictPair.Second },
                   conflictingTypeParameterName: conflictName,
                   description: $"Method<{conflictName}, {okName}>({conflictName}, {okName}, {conflictName}) called with ({conflictPair.First.DisplayName}, {okType.DisplayName}, {conflictPair.Second.DisplayName})");
    }

    /// <summary>
    /// Generates a scenario where the conflict arises from a type parameter inside
    /// a ConstructedType. Example: Method&lt;T&gt;(List&lt;T&gt; a, T b) called with
    /// (List&lt;int&gt;, string) — T inferred as int from first arg, string from second.
    /// </summary>
    private static Gen<ConflictingBindingScenario> GenConstructedTypeConflict()
    {
        return from pair in GenDistinctTypePair()
               from tpName in Gen.Elements("T", "E", "V")
               let tp = new TypeParameterType(tpName, 0)
               let listOfTp = new ConstructedType(ListDef, new IType[] { new TypeParameterType(tpName, 0) })
               let listOfFirst = new ConstructedType(ListDef, new IType[] { pair.First })
               select new ConflictingBindingScenario(
                   typeParameters: new IType[] { tp },
                   parameterTypes: new IType[] { listOfTp, new TypeParameterType(tpName, 0) },
                   argumentTypes: new IType[] { listOfFirst, pair.Second },
                   conflictingTypeParameterName: tpName,
                   description: $"Method<{tpName}>(List<{tpName}>, {tpName}) called with (List<{pair.First.DisplayName}>, {pair.Second.DisplayName})");
    }

    /// <summary>
    /// Generates a scenario where the conflict arises from two ConstructedType positions.
    /// Example: Method&lt;T&gt;(List&lt;T&gt; a, List&lt;T&gt; b) called with
    /// (List&lt;int&gt;, List&lt;string&gt;).
    /// </summary>
    private static Gen<ConflictingBindingScenario> GenDualConstructedTypeConflict()
    {
        return from pair in GenDistinctTypePair()
               from tpName in Gen.Elements("T", "E", "V")
               let tp = new TypeParameterType(tpName, 0)
               let listOfTpA = new ConstructedType(ListDef, new IType[] { new TypeParameterType(tpName, 0) })
               let listOfTpB = new ConstructedType(ListDef, new IType[] { new TypeParameterType(tpName, 0) })
               let listOfFirst = new ConstructedType(ListDef, new IType[] { pair.First })
               let listOfSecond = new ConstructedType(ListDef, new IType[] { pair.Second })
               select new ConflictingBindingScenario(
                   typeParameters: new IType[] { tp },
                   parameterTypes: new IType[] { listOfTpA, listOfTpB },
                   argumentTypes: new IType[] { listOfFirst, listOfSecond },
                   conflictingTypeParameterName: tpName,
                   description: $"Method<{tpName}>(List<{tpName}>, List<{tpName}>) called with (List<{pair.First.DisplayName}>, List<{pair.Second.DisplayName}>)");
    }

    // ─── Combined generator ──────────────────────────────────────────────

    private static Gen<ConflictingBindingScenario> GenScenario()
    {
        return Gen.OneOf(
            GenSimpleTwoPositionConflict(),
            GenMultiPositionConflict(),
            GenMultiTypeParamWithOneConflict(),
            GenConstructedTypeConflict(),
            GenDualConstructedTypeConflict());
    }

    public static Arbitrary<ConflictingBindingScenario> ArbitraryConflictingBindingScenario() =>
        GenScenario().ToArbitrary();

    // ─── Property test ───────────────────────────────────────────────────

    /// <summary>
    /// Property 4: Conflicting bindings produce diagnostic
    ///
    /// For any generic method where the same type parameter appears in multiple
    /// parameter positions, and for any set of arguments where two positions for
    /// that type parameter resolve to different concrete types, the inference engine
    /// SHALL report a conflict (Success = false, Conflicts is non-empty).
    ///
    /// **Validates: Requirements 3.3, 4.2, 4.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TypeArgumentInferenceConflictPropertyTests) })]
    public Property ConflictingBindings_ProduceInferenceFailure(ConflictingBindingScenario scenario)
    {
        var result = TypeArgumentInferenceEngine.TryInfer(
            scenario.TypeParameters,
            scenario.ParameterTypes,
            scenario.ArgumentTypes);

        var successIsFalse = !result.Success;
        var hasConflicts = result.Conflicts is not null && result.Conflicts.Count > 0;
        var conflictNamesExpectedParam = result.Conflicts is not null
            && result.Conflicts.Any(c => c.TypeParameter == scenario.ConflictingTypeParameterName);

        return (successIsFalse && hasConflicts && conflictNamesExpectedParam).ToProperty()
            .Label($"Expected inference failure with conflict for '{scenario.ConflictingTypeParameterName}'.\n"
                 + $"  Success={result.Success}\n"
                 + $"  Conflicts={result.Conflicts?.Count ?? 0}\n"
                 + $"  ConflictNames=[{string.Join(", ", result.Conflicts?.Select(c => c.TypeParameter) ?? [])}]\n"
                 + $"  Scenario: {scenario.Description}");
    }
}