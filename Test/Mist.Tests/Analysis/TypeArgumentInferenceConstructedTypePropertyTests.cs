// Feature: type-argument-inference, Property 2: Constructed type recursive unification
// **Validates: Requirements 2.1, 2.3, 2.4, 2.5**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying constructed type recursive unification.
///
/// Property 2: For any generic method whose parameter type is a ConstructedType
/// containing type parameters at any nesting depth, and for any argument whose type
/// is a ConstructedType with the same GenericDefinition at each level, the inference
/// engine SHALL recursively descend and bind each type parameter to the concrete type
/// at its corresponding position in the argument's type arguments.
///
/// **Validates: Requirements 2.1, 2.3, 2.4, 2.5**
///
/// No processes are spawned — all verification is in-process on the
/// <see cref="TypeArgumentInferenceEngine"/> directly.
/// </summary>
public class TypeArgumentInferenceConstructedTypePropertyTests
{
    // ─── Shared type infrastructure ──────────────────────────────────────

    private static readonly IScope EmptyScope = new Scope(null);

    // Concrete types used as inferred results
    private static readonly IType[] ConcreteTypes =
    [
        new StructType("Int32", EmptyScope, null),
        new StructType("Boolean", EmptyScope, null),
        new ClassType("String", EmptyScope, null),
        new StructType("Double", EmptyScope, null),
        new StructType("Single", EmptyScope, null),
        new ClassType("Object", EmptyScope, null),
        new StructType("Char", EmptyScope, null),
        new StructType("Int64", EmptyScope, null),
    ];

    // Generic definitions used to build ConstructedTypes
    private static readonly ClassType ListDef = new("List", EmptyScope, null)
    {
        TypeParameters = [new TypeParameterType("T", 0)],
    };

    private static readonly ClassType MapDef = new("Map", EmptyScope, null)
    {
        TypeParameters = [new TypeParameterType("K", 0), new TypeParameterType("V", 1)],
    };

    private static readonly ClassType BoxDef = new("Box", EmptyScope, null)
    {
        TypeParameters = [new TypeParameterType("T", 0)],
    };

    private static readonly ClassType PairDef = new("Pair", EmptyScope, null)
    {
        TypeParameters = [new TypeParameterType("A", 0), new TypeParameterType("B", 1)],
    };

    private static readonly ClassType TripleDef = new("Triple", EmptyScope, null)
    {
        TypeParameters =
        [
            new TypeParameterType("X", 0),
            new TypeParameterType("Y", 1),
            new TypeParameterType("Z", 2),
        ],
    };

    // ─── Test data models ────────────────────────────────────────────────

#pragma warning disable CA1034 // Test scenario type must be public for FsCheck Arbitrary discovery
    /// <summary>
    /// Represents a scenario where a method parameter is a ConstructedType containing
    /// type parameters at some nesting depth, and the argument is a matching
    /// ConstructedType with concrete types at those positions.
    /// </summary>
    public sealed class ConstructedTypeScenario
    {
        /// <summary>The type parameters declared on the method.</summary>
        public IReadOnlyList<IType> TypeParameters { get; }

        /// <summary>The declared parameter type (ConstructedType with TypeParameterTypes).</summary>
        public IType DeclaredParamType { get; }

        /// <summary>The argument type (ConstructedType with concrete types).</summary>
        public IType ArgumentType { get; }

        /// <summary>Expected bindings: type parameter name → concrete type.</summary>
        public IReadOnlyDictionary<string, IType> ExpectedBindings { get; }

        /// <summary>Human-readable description for test output.</summary>
        public string Description { get; }

        public ConstructedTypeScenario(
            IReadOnlyList<IType> typeParameters,
            IType declaredParamType,
            IType argumentType,
            IReadOnlyDictionary<string, IType> expectedBindings,
            string description)
        {
            TypeParameters = typeParameters;
            DeclaredParamType = declaredParamType;
            ArgumentType = argumentType;
            ExpectedBindings = expectedBindings;
            Description = description;
        }

        public override string ToString()
        {
            return Description;
        }
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario with a single-level ConstructedType (depth 1).
    /// e.g., List&lt;T&gt; unified with List&lt;int&gt; → T = int
    /// Validates Requirement 2.1: pairwise matching of type argument positions.
    /// </summary>
    private static Gen<ConstructedTypeScenario> GenSingleLevelSingleParam()
    {
        return from concreteIdx in Gen.Choose(0, ConcreteTypes.Length - 1)
               let concrete = ConcreteTypes[concreteIdx]
               let tp = new TypeParameterType("T", 0)
               let declaredParam = new ConstructedType(ListDef, [tp])
               let argumentType = new ConstructedType(ListDef, [concrete])
               let bindings = new Dictionary<string, IType> { ["T"] = concrete }
               select new ConstructedTypeScenario(
                   [tp],
                   declaredParam,
                   argumentType,
                   bindings,
                   $"List<T> ~ List<{concrete.DisplayName}> → T={concrete.DisplayName}");
    }

    /// <summary>
    /// Generates a scenario with a single-level ConstructedType with multiple type params.
    /// e.g., Map&lt;K, V&gt; unified with Map&lt;string, int&gt; → K = string, V = int
    /// Validates Requirement 2.3: multiple type parameters in a constructed type.
    /// </summary>
    private static Gen<ConstructedTypeScenario> GenSingleLevelMultiParam()
    {
        return from idx1 in Gen.Choose(0, ConcreteTypes.Length - 1)
               from idx2 in Gen.Choose(0, ConcreteTypes.Length - 1)
               let concrete1 = ConcreteTypes[idx1]
               let concrete2 = ConcreteTypes[idx2]
               let tpK = new TypeParameterType("K", 0)
               let tpV = new TypeParameterType("V", 1)
               let declaredParam = new ConstructedType(MapDef, [tpK, tpV])
               let argumentType = new ConstructedType(MapDef, [concrete1, concrete2])
               let bindings = new Dictionary<string, IType> { ["K"] = concrete1, ["V"] = concrete2 }
               select new ConstructedTypeScenario(
                   [tpK, tpV],
                   declaredParam,
                   argumentType,
                   bindings,
                   $"Map<K,V> ~ Map<{concrete1.DisplayName},{concrete2.DisplayName}> → K={concrete1.DisplayName}, V={concrete2.DisplayName}");
    }

    /// <summary>
    /// Generates a scenario with a triple-arity ConstructedType.
    /// e.g., Triple&lt;X, Y, Z&gt; unified with Triple&lt;int, string, bool&gt;
    /// Validates Requirement 2.3: multiple type parameters in a constructed type.
    /// </summary>
    private static Gen<ConstructedTypeScenario> GenTripleParam()
    {
        return from idx1 in Gen.Choose(0, ConcreteTypes.Length - 1)
               from idx2 in Gen.Choose(0, ConcreteTypes.Length - 1)
               from idx3 in Gen.Choose(0, ConcreteTypes.Length - 1)
               let c1 = ConcreteTypes[idx1]
               let c2 = ConcreteTypes[idx2]
               let c3 = ConcreteTypes[idx3]
               let tpX = new TypeParameterType("X", 0)
               let tpY = new TypeParameterType("Y", 1)
               let tpZ = new TypeParameterType("Z", 2)
               let declaredParam = new ConstructedType(TripleDef, [tpX, tpY, tpZ])
               let argumentType = new ConstructedType(TripleDef, [c1, c2, c3])
               let bindings = new Dictionary<string, IType>
               { ["X"] = c1, ["Y"] = c2, ["Z"] = c3 }
               select new ConstructedTypeScenario(
                   [tpX, tpY, tpZ],
                   declaredParam,
                   argumentType,
                   bindings,
                   $"Triple<X,Y,Z> ~ Triple<{c1.DisplayName},{c2.DisplayName},{c3.DisplayName}>");
    }

    /// <summary>
    /// Generates a scenario with nested ConstructedTypes (depth 2).
    /// e.g., List&lt;List&lt;T&gt;&gt; unified with List&lt;List&lt;int&gt;&gt; → T = int
    /// Validates Requirement 2.4: recursive descent into nested ConstructedTypes.
    /// </summary>
    private static Gen<ConstructedTypeScenario> GenNestedDepth2()
    {
        return from concreteIdx in Gen.Choose(0, ConcreteTypes.Length - 1)
               let concrete = ConcreteTypes[concreteIdx]
               let tp = new TypeParameterType("T", 0)
               // Declared: List<List<T>>
               let innerDeclared = new ConstructedType(ListDef, [tp])
               let outerDeclared = new ConstructedType(ListDef, [innerDeclared])
               // Argument: List<List<int>>
               let innerArg = new ConstructedType(ListDef, [concrete])
               let outerArg = new ConstructedType(ListDef, [innerArg])
               let bindings = new Dictionary<string, IType> { ["T"] = concrete }
               select new ConstructedTypeScenario(
                   [tp],
                   outerDeclared,
                   outerArg,
                   bindings,
                   $"List<List<T>> ~ List<List<{concrete.DisplayName}>> → T={concrete.DisplayName}");
    }

    /// <summary>
    /// Generates a scenario with nested ConstructedTypes (depth 3).
    /// e.g., Box&lt;List&lt;Box&lt;T&gt;&gt;&gt; unified with Box&lt;List&lt;Box&lt;string&gt;&gt;&gt;
    /// Validates Requirement 2.4: recursive descent at deeper nesting.
    /// </summary>
    private static Gen<ConstructedTypeScenario> GenNestedDepth3()
    {
        return from concreteIdx in Gen.Choose(0, ConcreteTypes.Length - 1)
               let concrete = ConcreteTypes[concreteIdx]
               let tp = new TypeParameterType("T", 0)
               // Declared: Box<List<Box<T>>>
               let level1Declared = new ConstructedType(BoxDef, [tp])
               let level2Declared = new ConstructedType(ListDef, [level1Declared])
               let level3Declared = new ConstructedType(BoxDef, [level2Declared])
               // Argument: Box<List<Box<concrete>>>
               let level1Arg = new ConstructedType(BoxDef, [concrete])
               let level2Arg = new ConstructedType(ListDef, [level1Arg])
               let level3Arg = new ConstructedType(BoxDef, [level2Arg])
               let bindings = new Dictionary<string, IType> { ["T"] = concrete }
               select new ConstructedTypeScenario(
                   [tp],
                   level3Declared,
                   level3Arg,
                   bindings,
                   $"Box<List<Box<T>>> ~ Box<List<Box<{concrete.DisplayName}>>> → T={concrete.DisplayName}");
    }

    /// <summary>
    /// Generates a scenario with multiple type parameters at different nesting depths.
    /// e.g., Map&lt;K, List&lt;V&gt;&gt; unified with Map&lt;int, List&lt;string&gt;&gt;
    /// Validates Requirements 2.3 + 2.4: multiple params with nesting.
    /// </summary>
    private static Gen<ConstructedTypeScenario> GenMixedDepthMultiParam()
    {
        return from idx1 in Gen.Choose(0, ConcreteTypes.Length - 1)
               from idx2 in Gen.Choose(0, ConcreteTypes.Length - 1)
               let c1 = ConcreteTypes[idx1]
               let c2 = ConcreteTypes[idx2]
               let tpK = new TypeParameterType("K", 0)
               let tpV = new TypeParameterType("V", 1)
               // Declared: Map<K, List<V>>
               let innerDeclared = new ConstructedType(ListDef, [tpV])
               let outerDeclared = new ConstructedType(MapDef, [tpK, innerDeclared])
               // Argument: Map<c1, List<c2>>
               let innerArg = new ConstructedType(ListDef, [c2])
               let outerArg = new ConstructedType(MapDef, [c1, innerArg])
               let bindings = new Dictionary<string, IType> { ["K"] = c1, ["V"] = c2 }
               select new ConstructedTypeScenario(
                   [tpK, tpV],
                   outerDeclared,
                   outerArg,
                   bindings,
                   $"Map<K,List<V>> ~ Map<{c1.DisplayName},List<{c2.DisplayName}>> → K={c1.DisplayName}, V={c2.DisplayName}");
    }

    /// <summary>
    /// Generates a scenario with deeply nested multi-param types.
    /// e.g., Pair&lt;Box&lt;A&gt;, Box&lt;B&gt;&gt; unified with Pair&lt;Box&lt;int&gt;, Box&lt;string&gt;&gt;
    /// Validates Requirements 2.3 + 2.4: multiple params each nested.
    /// </summary>
    private static Gen<ConstructedTypeScenario> GenNestedMultiParamSymmetric()
    {
        return from idx1 in Gen.Choose(0, ConcreteTypes.Length - 1)
               from idx2 in Gen.Choose(0, ConcreteTypes.Length - 1)
               let c1 = ConcreteTypes[idx1]
               let c2 = ConcreteTypes[idx2]
               let tpA = new TypeParameterType("A", 0)
               let tpB = new TypeParameterType("B", 1)
               // Declared: Pair<Box<A>, Box<B>>
               let boxA = new ConstructedType(BoxDef, [tpA])
               let boxB = new ConstructedType(BoxDef, [tpB])
               let declaredParam = new ConstructedType(PairDef, [boxA, boxB])
               // Argument: Pair<Box<c1>, Box<c2>>
               let boxC1 = new ConstructedType(BoxDef, [c1])
               let boxC2 = new ConstructedType(BoxDef, [c2])
               let argumentType = new ConstructedType(PairDef, [boxC1, boxC2])
               let bindings = new Dictionary<string, IType> { ["A"] = c1, ["B"] = c2 }
               select new ConstructedTypeScenario(
                   [tpA, tpB],
                   declaredParam,
                   argumentType,
                   bindings,
                   $"Pair<Box<A>,Box<B>> ~ Pair<Box<{c1.DisplayName}>,Box<{c2.DisplayName}>> → A={c1.DisplayName}, B={c2.DisplayName}");
    }

    // ─── Combined generator ──────────────────────────────────────────────

    private static Gen<ConstructedTypeScenario> GenScenario()
    {
        return Gen.OneOf(
            GenSingleLevelSingleParam(),
            GenSingleLevelMultiParam(),
            GenTripleParam(),
            GenNestedDepth2(),
            GenNestedDepth3(),
            GenMixedDepthMultiParam(),
            GenNestedMultiParamSymmetric());
    }

    public static Arbitrary<ConstructedTypeScenario> ArbitraryConstructedTypeScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ─── Property test ───────────────────────────────────────────────────

    /// <summary>
    /// Property 2: Constructed type recursive unification
    ///
    /// For any generic method whose parameter type is a ConstructedType containing
    /// type parameters at any nesting depth, and for any argument whose type is a
    /// ConstructedType with the same GenericDefinition at each level, the inference
    /// engine SHALL recursively descend and bind each type parameter to the concrete
    /// type at its corresponding position in the argument's type arguments.
    ///
    /// **Validates: Requirements 2.1, 2.3, 2.4, 2.5**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(TypeArgumentInferenceConstructedTypePropertyTests) })]
    public Property ConstructedType_RecursiveUnification_BindsAllTypeParameters(
        ConstructedTypeScenario scenario)
    {
        // Invoke the inference engine with a single parameter-argument pair
        var result = TypeArgumentInferenceEngine.TryInfer(
            scenario.TypeParameters,
            [scenario.DeclaredParamType],
            [scenario.ArgumentType]);

        // Inference must succeed
        var succeeded = result.Success;

        // All expected bindings must be present and correct
        var bindingsCorrect = true;
        var mismatchDetails = "";

        if(result.Success && result.InferredTypeArguments is not null)
        {
            foreach(var (name, expectedType) in scenario.ExpectedBindings)
            {
                // Find the type parameter by name to get its ordinal
                var tp = scenario.TypeParameters
                    .OfType<TypeParameterType>()
                    .FirstOrDefault(t => t.Name == name);

                if(tp is null)
                {
                    bindingsCorrect = false;
                    mismatchDetails += $"Type parameter '{name}' not found in declaration. ";
                    continue;
                }

                var inferredType = result.InferredTypeArguments[tp.Ordinal];
                if(!ReferenceEquals(inferredType, expectedType))
                {
                    bindingsCorrect = false;
                    mismatchDetails +=
                        $"Expected {name}={expectedType.DisplayName}, got {inferredType.DisplayName}. ";
                }
            }
        }

        return (succeeded && bindingsCorrect).ToProperty()
            .Label(succeeded
                ? $"Bindings incorrect: {mismatchDetails}\nScenario: {scenario}"
                : $"Inference failed (Success=false).\nScenario: {scenario}\n"
                  + $"Uninferrable: {FormatList(result.UninferrableTypeParameters)}\n"
                  + $"Conflicts: {FormatConflicts(result.Conflicts)}");
    }

    // ─── Formatting helpers ──────────────────────────────────────────────

    private static string FormatList(IReadOnlyList<string>? items)
    {
        return items is null ? "none" : string.Join(", ", items);
    }

    private static string FormatConflicts(
        IReadOnlyList<(string TypeParameter, IType First, IType Second)>? conflicts)
    {
        return conflicts is null
            ? "none"
            : string.Join("; ", conflicts.Select(c =>
                $"{c.TypeParameter}: {c.First.DisplayName} vs {c.Second.DisplayName}"));
    }
}