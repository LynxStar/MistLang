// Feature: type-argument-inference, Property 1: Direct type parameter inference
// **Validates: Requirements 1.2, 1.6, 3.1**
//
// For any generic method whose parameters include TypeParameterType positions,
// and for any set of concrete argument types at those positions, the inference
// engine SHALL map each type parameter to the concrete type of the argument at
// its corresponding position.
//
// These tests run entirely in-process on the TypeArgumentInferenceEngine
// directly — no process spawning needed.

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

/// <summary>
/// Represents a scenario where each type parameter appears as a direct
/// TypeParameterType in the parameter list, with a random concrete argument
/// type at its corresponding position.
/// </summary>
public sealed class DirectInferenceScenario
{
    /// <summary>The method's type parameters.</summary>
    public IReadOnlyList<IType> TypeParameters { get; }

    /// <summary>The declared parameter types (each a TypeParameterType).</summary>
    public IReadOnlyList<IType> ParameterTypes { get; }

    /// <summary>The resolved argument types at the call site (concrete types).</summary>
    public IReadOnlyList<IType> ArgumentTypes { get; }

    /// <summary>
    /// Expected mapping: index i → the concrete type that type parameter i should be inferred to.
    /// </summary>
    public IReadOnlyList<IType> ExpectedInferredTypes { get; }

    /// <summary>Human-readable description for test output.</summary>
    public string Description { get; }

    public DirectInferenceScenario(
        IReadOnlyList<IType> typeParameters,
        IReadOnlyList<IType> parameterTypes,
        IReadOnlyList<IType> argumentTypes,
        IReadOnlyList<IType> expectedInferredTypes,
        string description)
    {
        TypeParameters = typeParameters;
        ParameterTypes = parameterTypes;
        ArgumentTypes = argumentTypes;
        ExpectedInferredTypes = expectedInferredTypes;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// Property-based tests verifying direct type parameter inference.
///
/// Property 1: Direct type parameter inference
///
/// For any generic method whose parameters include TypeParameterType positions,
/// and for any set of concrete argument types at those positions, the inference
/// engine SHALL map each type parameter to the concrete type of the argument at
/// its corresponding position.
///
/// **Validates: Requirements 1.2, 1.6, 3.1**
/// </summary>
public class TypeArgumentInferenceDirectPropertyTests
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

    // ─── Type parameter name pool ────────────────────────────────────────

    private static readonly string[] TypeParamNames = ["T", "U", "V", "W"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a concrete type from the pool.
    /// </summary>
    private static Gen<IType> GenConcreteType()
    {
        return Gen.Elements(ConcreteTypes);
    }

    /// <summary>
    /// Generates a scenario with 1-4 type parameters, each appearing as a direct
    /// TypeParameterType in the parameter list, with a random concrete argument type
    /// at each position.
    /// </summary>
    private static Gen<DirectInferenceScenario> GenScenario()
    {
        return from typeParamCount in Gen.Choose(1, 4)
               from concreteTypes in GenConcreteType().ArrayOf(typeParamCount)
               let typeParams = Enumerable.Range(0, typeParamCount)
                   .Select(i => (IType)new TypeParameterType(TypeParamNames[i], i))
                   .ToArray()
               let paramTypes = Enumerable.Range(0, typeParamCount)
                   .Select(i => (IType)new TypeParameterType(TypeParamNames[i], i))
                   .ToArray()
               let description = BuildDescription(typeParamCount, concreteTypes)
               select new DirectInferenceScenario(
                   typeParameters: typeParams,
                   parameterTypes: paramTypes,
                   argumentTypes: concreteTypes,
                   expectedInferredTypes: concreteTypes,
                   description: description);
    }

    private static string BuildDescription(int typeParamCount, IType[] concreteTypes)
    {
        var tpNames = string.Join(", ", Enumerable.Range(0, typeParamCount).Select(i => TypeParamNames[i]));
        var paramList = string.Join(", ", Enumerable.Range(0, typeParamCount).Select(i => $"{TypeParamNames[i]} p{i}"));
        var argList = string.Join(", ", concreteTypes.Select(t => t.DisplayName));
        return $"Method<{tpNames}>({paramList}) called with ({argList})";
    }

    public static Arbitrary<DirectInferenceScenario> ArbitraryDirectInferenceScenario() =>
        GenScenario().ToArbitrary();

    // ─── Property test ───────────────────────────────────────────────────

    /// <summary>
    /// Property 1: Direct type parameter inference
    ///
    /// For any generic method whose parameters include TypeParameterType positions,
    /// and for any set of concrete argument types at those positions, the inference
    /// engine SHALL map each type parameter to the concrete type of the argument at
    /// its corresponding position.
    ///
    /// **Validates: Requirements 1.2, 1.6, 3.1**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TypeArgumentInferenceDirectPropertyTests) })]
    public Property DirectTypeParameters_AreInferredFromCorrespondingArguments(DirectInferenceScenario scenario)
    {
        var result = TypeArgumentInferenceEngine.TryInfer(
            scenario.TypeParameters,
            scenario.ParameterTypes,
            scenario.ArgumentTypes);

        var inferenceSucceeded = result.Success;
        var hasInferredTypes = result.InferredTypeArguments is not null
            && result.InferredTypeArguments.Count == scenario.TypeParameters.Count;

        var allMappingsCorrect = hasInferredTypes
            && result.InferredTypeArguments!
                .Select((inferred, i) => inferred.DisplayName == scenario.ExpectedInferredTypes[i].DisplayName)
                .All(x => x);

        return (inferenceSucceeded && hasInferredTypes && allMappingsCorrect).ToProperty()
            .Label($"Expected inference success with correct mappings.\n"
                 + $"  Success={result.Success}\n"
                 + $"  InferredCount={result.InferredTypeArguments?.Count ?? 0}\n"
                 + $"  Expected=[{string.Join(", ", scenario.ExpectedInferredTypes.Select(t => t.DisplayName))}]\n"
                 + $"  Actual=[{string.Join(", ", result.InferredTypeArguments?.Select(t => t.DisplayName) ?? [])}]\n"
                 + $"  Scenario: {scenario.Description}");
    }
}