// Feature: type-argument-inference, Property 3: Consistent multi-position bindings
// **Validates: Requirements 3.2, 3.4**

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
/// parameter positions and all corresponding arguments resolve to the same concrete type.
/// </summary>
public sealed class ConsistentBindingScenario
{
    /// <summary>The method's type parameters.</summary>
    public IReadOnlyList<IType> TypeParameters { get; }

    /// <summary>The declared parameter types (containing repeated type parameters).</summary>
    public IReadOnlyList<IType> ParameterTypes { get; }

    /// <summary>The argument types (all positions for a given type parameter use the same concrete type).</summary>
    public IReadOnlyList<IType> ArgumentTypes { get; }

    /// <summary>Expected bindings: type parameter name → concrete type.</summary>
    public IReadOnlyDictionary<string, IType> ExpectedBindings { get; }

    public string Description { get; }

    public ConsistentBindingScenario(
        IReadOnlyList<IType> typeParameters,
        IReadOnlyList<IType> parameterTypes,
        IReadOnlyList<IType> argumentTypes,
        IReadOnlyDictionary<string, IType> expectedBindings,
        string description)
    {
        TypeParameters = typeParameters;
        ParameterTypes = parameterTypes;
        ArgumentTypes = argumentTypes;
        ExpectedBindings = expectedBindings;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// Property-based tests verifying consistent multi-position bindings.
///
/// Property 3: For any generic method where the same type parameter appears in multiple
/// parameter positions, and for any set of arguments where all positions for that type
/// parameter resolve to the same concrete type, the inference engine SHALL succeed and
/// record that single concrete type.
///
/// **Validates: Requirements 3.2, 3.4**
/// </summary>
public class TypeArgumentInferenceConsistentBindingPropertyTests
{
    // ─── Concrete type pool ──────────────────────────────────────────────

    private static readonly IType[] ConcreteTypes =
    [
        new StructType("int", new Scope(null), containingNamespace: null),
        new StructType("bool", new Scope(null), containingNamespace: null),
        new StructType("float", new Scope(null), containingNamespace: null),
        new ClassType("string", new Scope(null), containingNamespace: null),
        new ClassType("Widget", new Scope(null), containingNamespace: "App"),
        new StructType("Point", new Scope(null), containingNamespace: "Geometry"),
        new ClassType("Service", new Scope(null), containingNamespace: "DI"),
    ];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario with a single type parameter appearing in 2–5 positions,
    /// all bound to the same concrete type.
    /// </summary>
    private static Gen<ConsistentBindingScenario> GenSingleTypeParamMultiplePositions()
    {
        return from concreteTypeIndex in Gen.Choose(0, ConcreteTypes.Length - 1)
               from positionCount in Gen.Choose(2, 5)
               let concreteType = ConcreteTypes[concreteTypeIndex]
               let tp = new TypeParameterType("T", 0)
               let typeParameters = new IType[] { tp }
               let parameterTypes = Enumerable.Repeat((IType)tp, positionCount).ToArray()
               let argumentTypes = Enumerable.Repeat(concreteType, positionCount).ToArray()
               let expectedBindings = new Dictionary<string, IType> { ["T"] = concreteType }
               select new ConsistentBindingScenario(
                   typeParameters,
                   parameterTypes,
                   argumentTypes,
                   expectedBindings,
                   $"T appears {positionCount}x, all bound to {concreteType.DisplayName}");
    }

    /// <summary>
    /// Generates a scenario with multiple type parameters, each appearing in multiple positions,
    /// all consistently bound.
    /// </summary>
    private static Gen<ConsistentBindingScenario> GenMultipleTypeParamsMultiplePositions()
    {
        return Gen.Choose(2, 3).SelectMany(typeParamCount =>
            Gen.ArrayOf(Gen.Choose(0, ConcreteTypes.Length - 1), typeParamCount).SelectMany(concreteIndices =>
                Gen.ArrayOf(Gen.Choose(2, 3), typeParamCount).Select(repetitions =>
                    BuildMultiParamScenario(typeParamCount, concreteIndices, repetitions))));
    }

    private static ConsistentBindingScenario BuildMultiParamScenario(
        int typeParamCount, int[] concreteIndices, int[] repetitions)
    {
        var typeParamNames = new[] { "T", "U", "V" };
        var typeParameters = new IType[typeParamCount];
        var expectedBindings = new Dictionary<string, IType>();

        for(var i = 0; i < typeParamCount; i++)
        {
            typeParameters[i] = new TypeParameterType(typeParamNames[i], i);
            // Ensure distinct concrete types for different type parameters
            // by cycling through the pool with an offset
            var concreteIndex = (concreteIndices[i] + i) % ConcreteTypes.Length;
            expectedBindings[typeParamNames[i]] = ConcreteTypes[concreteIndex];
        }

        // Build parameter types: interleave repetitions of each type parameter
        var parameterTypes = new List<IType>();
        var argumentTypes = new List<IType>();

        for(var i = 0; i < typeParamCount; i++)
        {
            var tp = typeParameters[i];
            var concreteType = expectedBindings[typeParamNames[i]];
            for(var r = 0; r < repetitions[i]; r++)
            {
                parameterTypes.Add(tp);
                argumentTypes.Add(concreteType);
            }
        }

        var desc = string.Join(", ", Enumerable.Range(0, typeParamCount)
            .Select(i => $"{typeParamNames[i]}×{repetitions[i]}→{expectedBindings[typeParamNames[i]].DisplayName}"));

        return new ConsistentBindingScenario(
            typeParameters,
            parameterTypes.ToArray(),
            argumentTypes.ToArray(),
            expectedBindings,
            $"Multi-param consistent: {desc}");
    }

    /// <summary>
    /// Generates a scenario where a type parameter appears both directly and inside
    /// a ConstructedType, with consistent bindings from both positions.
    /// </summary>
    private static Gen<ConsistentBindingScenario> GenMixedDirectAndConstructedPositions()
    {
        return from concreteTypeIndex in Gen.Choose(0, ConcreteTypes.Length - 1)
               let concreteType = ConcreteTypes[concreteTypeIndex]
               let tp = new TypeParameterType("T", 0)
               let typeParameters = new IType[] { tp }
               let listDef = MakeGenericDefinition("List", 1)
               // Parameter types: T, List<T>
               let parameterTypes = new IType[]
               {
                   tp,
                   new ConstructedType(listDef, new IType[] { tp })
               }
               // Argument types: concreteType, List<concreteType>
               let argumentTypes = new IType[]
               {
                   concreteType,
                   new ConstructedType(listDef, new IType[] { concreteType })
               }
               let expectedBindings = new Dictionary<string, IType> { ["T"] = concreteType }
               select new ConsistentBindingScenario(
                   typeParameters,
                   parameterTypes,
                   argumentTypes,
                   expectedBindings,
                   $"T direct + List<T>, both bound to {concreteType.DisplayName}");
    }

    /// <summary>
    /// Generates a scenario where a type parameter appears in multiple constructed type
    /// positions (e.g., List&lt;T&gt; and Map&lt;T, int&gt;), all consistently bound.
    /// </summary>
    private static Gen<ConsistentBindingScenario> GenMultipleConstructedPositions()
    {
        return from concreteTypeIndex in Gen.Choose(0, ConcreteTypes.Length - 1)
               let concreteType = ConcreteTypes[concreteTypeIndex]
               let tp = new TypeParameterType("T", 0)
               let typeParameters = new IType[] { tp }
               let listDef = MakeGenericDefinition("List", 1)
               let pairDef = MakeGenericDefinition("Pair", 2)
               let fillerType = ConcreteTypes[(concreteTypeIndex + 1) % ConcreteTypes.Length]
               // Parameter types: List<T>, Pair<T, Filler>
               let parameterTypes = new IType[]
               {
                   new ConstructedType(listDef, new IType[] { tp }),
                   new ConstructedType(pairDef, new IType[] { tp, fillerType })
               }
               // Argument types: List<concreteType>, Pair<concreteType, Filler>
               let argumentTypes = new IType[]
               {
                   new ConstructedType(listDef, new IType[] { concreteType }),
                   new ConstructedType(pairDef, new IType[] { concreteType, fillerType })
               }
               let expectedBindings = new Dictionary<string, IType> { ["T"] = concreteType }
               select new ConsistentBindingScenario(
                   typeParameters,
                   parameterTypes,
                   argumentTypes,
                   expectedBindings,
                   $"List<T> + Pair<T, _>, both bound to {concreteType.DisplayName}");
    }

    /// <summary>
    /// Generates a scenario where some positions provide no binding (e.g., argument is
    /// a non-matching type) but other positions consistently bind the type parameter.
    /// This validates Requirement 3.4: inference uses the successfully inferred type
    /// from other positions without reporting a conflict.
    /// </summary>
    private static Gen<ConsistentBindingScenario> GenPartialBindingWithConsistentPositions()
    {
        return from concreteTypeIndex in Gen.Choose(0, ConcreteTypes.Length - 1)
               let concreteType = ConcreteTypes[concreteTypeIndex]
               let tp = new TypeParameterType("T", 0)
               let typeParameters = new IType[] { tp }
               let listDef = MakeGenericDefinition("List", 1)
               let unrelatedDef = MakeGenericDefinition("Unrelated", 1)
               // Parameter types: T, T, List<T>
               // Argument types: concreteType, concreteType, List<concreteType>
               // The third position also provides T via constructed type unification
               let parameterTypes = new IType[] { tp, tp, new ConstructedType(listDef, new IType[] { tp }) }
               let argumentTypes = new IType[]
               {
                   concreteType,
                   concreteType,
                   new ConstructedType(listDef, new IType[] { concreteType })
               }
               let expectedBindings = new Dictionary<string, IType> { ["T"] = concreteType }
               select new ConsistentBindingScenario(
                   typeParameters,
                   parameterTypes,
                   argumentTypes,
                   expectedBindings,
                   $"T×2 + List<T>, all consistently bound to {concreteType.DisplayName}");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static ClassType MakeGenericDefinition(string name, int arity)
    {
        var scope = new Scope(null);
        var def = new ClassType(name, scope, containingNamespace: "TestPool");
        var tps = new TypeParameterType[arity];
        for(var i = 0; i < arity; i++)
            tps[i] = new TypeParameterType($"T{i}", i);
        def.TypeParameters = tps;
        return def;
    }

    // ─── Combined generator ──────────────────────────────────────────────

    private static Gen<ConsistentBindingScenario> GenScenario()
    {
        return Gen.OneOf(
            GenSingleTypeParamMultiplePositions(),
            GenMultipleTypeParamsMultiplePositions(),
            GenMixedDirectAndConstructedPositions(),
            GenMultipleConstructedPositions(),
            GenPartialBindingWithConsistentPositions());
    }

    public static Arbitrary<ConsistentBindingScenario> ArbitraryConsistentBindingScenario() =>
        GenScenario().ToArbitrary();

    // ─── Property test ───────────────────────────────────────────────────

    /// <summary>
    /// Property 3: Consistent multi-position bindings
    ///
    /// For any generic method where the same type parameter appears in multiple parameter
    /// positions, and for any set of arguments where all positions for that type parameter
    /// resolve to the same concrete type, the inference engine SHALL succeed and record
    /// that single concrete type.
    ///
    /// **Validates: Requirements 3.2, 3.4**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TypeArgumentInferenceConsistentBindingPropertyTests) })]
    public Property ConsistentMultiPositionBindings_InferenceSucceedsWithSingleConcreteType(
        ConsistentBindingScenario scenario)
    {
        var result = TypeArgumentInferenceEngine.TryInfer(
            scenario.TypeParameters,
            scenario.ParameterTypes,
            scenario.ArgumentTypes);

        // Inference must succeed
        if(!result.Success)
        {
            return false.ToProperty()
                .Label($"Inference failed but should have succeeded.\nScenario: {scenario.Description}\n" +
                       $"Uninferrable: {FormatList(result.UninferrableTypeParameters)}\n" +
                       $"Conflicts: {FormatConflicts(result.Conflicts)}");
        }

        // Each expected binding must match
        var inferredArgs = result.InferredTypeArguments!;
        for(var i = 0; i < scenario.TypeParameters.Count; i++)
        {
            if(scenario.TypeParameters[i] is TypeParameterType tpt)
            {
                var expected = scenario.ExpectedBindings[tpt.Name];
                var actual = inferredArgs[i];

                if(expected.DisplayName != actual.DisplayName)
                {
                    return false.ToProperty()
                        .Label($"Type parameter '{tpt.Name}' inferred as '{actual.DisplayName}' " +
                               $"but expected '{expected.DisplayName}'.\nScenario: {scenario.Description}");
                }
            }
        }

        return true.ToProperty()
            .Label($"OK: {scenario.Description}");
    }

    private static string FormatList(IReadOnlyList<string>? items) =>
        items is null ? "(null)" : $"[{string.Join(", ", items)}]";

    private static string FormatConflicts(
        IReadOnlyList<(string TypeParameter, IType First, IType Second)>? conflicts) =>
        conflicts is null
            ? "(null)"
            : $"[{string.Join(", ", conflicts.Select(c => $"{c.TypeParameter}: {c.First.DisplayName} vs {c.Second.DisplayName}"))}]";
}