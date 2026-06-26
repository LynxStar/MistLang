// Feature: type-argument-inference, Property 5: Uninferrable type parameters produce diagnostic
// **Validates: Requirements 1.4, 4.1, 4.3**
//
// For any generic method where at least one type parameter does not appear in
// any parameter position (e.g., only in the return type), and the call omits
// explicit type arguments, the inference engine SHALL report failure and the
// compiler SHALL emit exactly one diagnostic error naming the uninferrable
// type parameter(s).
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
/// Represents a scenario where at least one type parameter does not appear in
/// any parameter position, which should cause the inference engine to report
/// those type parameters as uninferrable.
/// </summary>
public sealed class UninferrableTypeParameterScenario
{
    /// <summary>The method's type parameters.</summary>
    public IReadOnlyList<IType> TypeParameters { get; }

    /// <summary>The declared parameter types (missing at least one type parameter).</summary>
    public IReadOnlyList<IType> ParameterTypes { get; }

    /// <summary>The resolved argument types at the call site.</summary>
    public IReadOnlyList<IType> ArgumentTypes { get; }

    /// <summary>The names of type parameters that should be reported as uninferrable.</summary>
    public IReadOnlyList<string> ExpectedUninferrableNames { get; }

    /// <summary>Human-readable description for test output.</summary>
    public string Description { get; }

    public UninferrableTypeParameterScenario(
        IReadOnlyList<IType> typeParameters,
        IReadOnlyList<IType> parameterTypes,
        IReadOnlyList<IType> argumentTypes,
        IReadOnlyList<string> expectedUninferrableNames,
        string description)
    {
        TypeParameters = typeParameters;
        ParameterTypes = parameterTypes;
        ArgumentTypes = argumentTypes;
        ExpectedUninferrableNames = expectedUninferrableNames;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// Property-based tests verifying that uninferrable type parameters produce inference failure.
///
/// Property 5: Uninferrable type parameters produce diagnostic
///
/// For any generic method where at least one type parameter does not appear in any
/// parameter position (e.g., only in the return type), and the call omits explicit
/// type arguments, the inference engine SHALL report failure and the compiler SHALL
/// emit exactly one diagnostic error naming the uninferrable type parameter(s).
///
/// **Validates: Requirements 1.4, 4.1, 4.3**
/// </summary>
public class TypeArgumentInferenceUninferrablePropertyTests
{
    // ─── Concrete type pool ──────────────────────────────────────────────

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
    /// Generates a scenario with a single type parameter that does not appear in
    /// any parameter position (method has no parameters at all).
    /// Example: Method&lt;T&gt;() — T is only in the return type.
    /// </summary>
    private static Gen<UninferrableTypeParameterScenario> GenNoParametersScenario()
    {
        return from tpName in Gen.Elements("T", "U", "V", "R")
               select new UninferrableTypeParameterScenario(
                   typeParameters: new IType[] { new TypeParameterType(tpName, 0) },
                   parameterTypes: Array.Empty<IType>(),
                   argumentTypes: Array.Empty<IType>(),
                   expectedUninferrableNames: new[] { tpName },
                   description: $"Method<{tpName}>() — no parameters, {tpName} uninferrable");
    }

    /// <summary>
    /// Generates a scenario with a single type parameter that does not appear in
    /// any parameter position, but the method has parameters of concrete types.
    /// Example: Method&lt;T&gt;(int x, bool y) — T doesn't appear in parameters.
    /// </summary>
    private static Gen<UninferrableTypeParameterScenario> GenConcreteOnlyParametersScenario()
    {
        return Gen.Choose(1, 4).SelectMany(paramCount =>
            Gen.Elements("T", "U", "V", "R").SelectMany(tpName =>
                Gen.ArrayOf(Gen.Choose(0, ConcreteTypes.Length - 1), paramCount)
                    .Select(concreteIndices =>
                    {
                        var paramTypes = concreteIndices.Select(i => ConcreteTypes[i]).ToArray();
                        var argTypes = concreteIndices.Select(i => ConcreteTypes[i]).ToArray();
                        return new UninferrableTypeParameterScenario(
                            typeParameters: new IType[] { new TypeParameterType(tpName, 0) },
                            parameterTypes: paramTypes,
                            argumentTypes: argTypes,
                            expectedUninferrableNames: new[] { tpName },
                            description: $"Method<{tpName}>({string.Join(", ", paramTypes.Select(t => t.DisplayName))}) — {tpName} not in params");
                    })));
    }

    /// <summary>
    /// Generates a scenario with multiple type parameters where some appear in
    /// parameters and some don't.
    /// Example: Method&lt;T, U&gt;(T x) — U doesn't appear in any parameter.
    /// </summary>
    private static Gen<UninferrableTypeParameterScenario> GenPartiallyInferrableScenario()
    {
        return from inferrableName in Gen.Elements("T", "A", "X")
               from uninferrableName in Gen.Elements("U", "B", "Y")
               where inferrableName != uninferrableName
               from argTypeIdx in Gen.Choose(0, ConcreteTypes.Length - 1)
               let argType = ConcreteTypes[argTypeIdx]
               select new UninferrableTypeParameterScenario(
                   typeParameters: new IType[]
                   {
                       new TypeParameterType(inferrableName, 0),
                       new TypeParameterType(uninferrableName, 1),
                   },
                   parameterTypes: new IType[]
                   {
                       new TypeParameterType(inferrableName, 0),
                   },
                   argumentTypes: new IType[] { argType },
                   expectedUninferrableNames: new[] { uninferrableName },
                   description: $"Method<{inferrableName}, {uninferrableName}>({inferrableName} x) called with ({argType.DisplayName}) — {uninferrableName} uninferrable");
    }

    /// <summary>
    /// Generates a scenario with multiple type parameters where multiple are uninferrable.
    /// Example: Method&lt;T, U, V&gt;(T x) — U and V don't appear in any parameter.
    /// </summary>
    private static Gen<UninferrableTypeParameterScenario> GenMultipleUninferrableScenario()
    {
        return from argTypeIdx in Gen.Choose(0, ConcreteTypes.Length - 1)
               let argType = ConcreteTypes[argTypeIdx]
               select new UninferrableTypeParameterScenario(
                   typeParameters: new IType[]
                   {
                       new TypeParameterType("T", 0),
                       new TypeParameterType("U", 1),
                       new TypeParameterType("V", 2),
                   },
                   parameterTypes: new IType[]
                   {
                       new TypeParameterType("T", 0),
                   },
                   argumentTypes: new IType[] { argType },
                   expectedUninferrableNames: new[] { "U", "V" },
                   description: $"Method<T, U, V>(T x) called with ({argType.DisplayName}) — U, V uninferrable");
    }

    /// <summary>
    /// Generates a scenario where a type parameter appears inside a ConstructedType
    /// in the parameter list but another type parameter does not appear anywhere.
    /// Example: Method&lt;T, U&gt;(List&lt;T&gt; items) — U doesn't appear.
    /// </summary>
    private static Gen<UninferrableTypeParameterScenario> GenConstructedTypeWithUninferrableScenario()
    {
        return from inferrableName in Gen.Elements("T", "A", "E")
               from uninferrableName in Gen.Elements("U", "B", "R")
               where inferrableName != uninferrableName
               from argTypeIdx in Gen.Choose(0, ConcreteTypes.Length - 1)
               let innerArgType = ConcreteTypes[argTypeIdx]
               let listOfTp = new ConstructedType(ListDef, new IType[] { new TypeParameterType(inferrableName, 0) })
               let listOfConcrete = new ConstructedType(ListDef, new IType[] { innerArgType })
               select new UninferrableTypeParameterScenario(
                   typeParameters: new IType[]
                   {
                       new TypeParameterType(inferrableName, 0),
                       new TypeParameterType(uninferrableName, 1),
                   },
                   parameterTypes: new IType[] { listOfTp },
                   argumentTypes: new IType[] { listOfConcrete },
                   expectedUninferrableNames: new[] { uninferrableName },
                   description: $"Method<{inferrableName}, {uninferrableName}>(List<{inferrableName}> items) called with (List<{innerArgType.DisplayName}>) — {uninferrableName} uninferrable");
    }

    /// <summary>
    /// Generates a scenario where all type parameters are uninferrable (none appear
    /// in any parameter position). The method has no parameters.
    /// Example: Method&lt;T, U&gt;() — both T and U are uninferrable.
    /// </summary>
    private static Gen<UninferrableTypeParameterScenario> GenAllUninferrableNoParamsScenario()
    {
        return from count in Gen.Choose(2, 4)
               let names = Enumerable.Range(0, count).Select(i => ((char)('T' + i)).ToString()).ToArray()
               let typeParams = names.Select((n, i) => (IType)new TypeParameterType(n, i)).ToArray()
               select new UninferrableTypeParameterScenario(
                   typeParameters: typeParams,
                   parameterTypes: Array.Empty<IType>(),
                   argumentTypes: Array.Empty<IType>(),
                   expectedUninferrableNames: names,
                   description: $"Method<{string.Join(", ", names)}>() — all uninferrable");
    }

    // ─── Combined generator ──────────────────────────────────────────────

    private static Gen<UninferrableTypeParameterScenario> GenScenario()
    {
        return Gen.OneOf(
            GenNoParametersScenario(),
            GenConcreteOnlyParametersScenario(),
            GenPartiallyInferrableScenario(),
            GenMultipleUninferrableScenario(),
            GenConstructedTypeWithUninferrableScenario(),
            GenAllUninferrableNoParamsScenario());
    }

    public static Arbitrary<UninferrableTypeParameterScenario> ArbitraryUninferrableTypeParameterScenario() =>
        GenScenario().ToArbitrary();

    // ─── Property test ───────────────────────────────────────────────────

    /// <summary>
    /// Property 5: Uninferrable type parameters produce diagnostic
    ///
    /// For any generic method where at least one type parameter does not appear in
    /// any parameter position, and the call omits explicit type arguments, the
    /// inference engine SHALL report failure (Success = false) and the uninferrable
    /// type parameter names SHALL be listed in UninferrableTypeParameters.
    ///
    /// **Validates: Requirements 1.4, 4.1, 4.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TypeArgumentInferenceUninferrablePropertyTests) })]
    public Property UninferrableTypeParameters_ProduceInferenceFailure(UninferrableTypeParameterScenario scenario)
    {
        var result = TypeArgumentInferenceEngine.TryInfer(
            scenario.TypeParameters,
            scenario.ParameterTypes,
            scenario.ArgumentTypes);

        var successIsFalse = !result.Success;
        var hasUninferrable = result.UninferrableTypeParameters is not null
            && result.UninferrableTypeParameters.Count > 0;
        var allExpectedNamesPresent = result.UninferrableTypeParameters is not null
            && scenario.ExpectedUninferrableNames.All(
                expected => result.UninferrableTypeParameters.Contains(expected));

        return (successIsFalse && hasUninferrable && allExpectedNamesPresent).ToProperty()
            .Label($"Expected inference failure with uninferrable params [{string.Join(", ", scenario.ExpectedUninferrableNames)}].\n"
                 + $"  Success={result.Success}\n"
                 + $"  UninferrableTypeParameters=[{string.Join(", ", result.UninferrableTypeParameters ?? [])}]\n"
                 + $"  Conflicts={result.Conflicts?.Count ?? 0}\n"
                 + $"  Scenario: {scenario.Description}");
    }
}