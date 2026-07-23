// Feature: variadics, task 17.5: property test for inference agreement (classic pipeline).
//
// Property 13: Inferred pack matches argument types.
// **Validates: Requirements 14.1, 14.2, 14.4**
//
// Asserts:
//   - The inferred pack element types match the static types of the value arguments in
//     left-to-right source order (Req 14.1).
//   - An empty argument list for a sole-pack method infers the empty pack (Req 14.4).
//   - Mixed fixed+pack inference uses the binding rule: fixed params bind positionally
//     inward from both ends; the pack absorbs the contiguous middle (Req 14.2).
//
// Drives TypeArgumentInferenceEngine.TryInfer directly with generated type parameters and
// argument types. All tests run entirely in-process with ZERO external process spawns.

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Variadics;

/// <summary>
/// A generated inference scenario: a method signature with zero or more leading fixed type
/// params, one pack, zero or more trailing fixed type params, and a corresponding argument
/// list. The oracle computes the expected inference result purely from the generated layout.
/// </summary>
public sealed record InferenceAgreementScenario(
    int LeadingFixed,
    int TrailingFixed,
    IReadOnlyList<IType> ArgumentTypes)
{
    /// <summary>Total fixed parameters (leading + trailing).</summary>
    public int TotalFixed => LeadingFixed + TrailingFixed;

    /// <summary>
    /// Whether inference should succeed: we need at least enough arguments for the
    /// fixed params (pack can absorb zero or more of the middle).
    /// </summary>
    public bool ShouldSucceed => ArgumentTypes.Count >= TotalFixed;

    /// <summary>
    /// The expected pack element types (the middle slice after fixed ends are removed).
    /// </summary>
    public IReadOnlyList<IType> ExpectedPackElements
    {
        get
        {
            if(!ShouldSucceed)
            {
                return Array.Empty<IType>();
            }

            var start = LeadingFixed;
            var end = ArgumentTypes.Count - TrailingFixed;
            if(end <= start)
            {
                return Array.Empty<IType>();
            }

            return ArgumentTypes.Skip(start).Take(end - start).ToArray();
        }
    }

    public override string ToString()
    {
        return $"leading={LeadingFixed} trailing={TrailingFixed} args={ArgumentTypes.Count}";
    }
}

/// <summary>
/// Property test for <b>Property 13 (Inferred pack matches argument types)</b> over the
/// classic pipeline. Drives <see cref="TypeArgumentInferenceEngine.TryInfer"/> directly with
/// generated type parameters and argument types. MaxTest = 150.
/// <b>Validates: Requirements 14.1, 14.2, 14.4.</b>
/// </summary>
public sealed class VariadicsInferenceAgreementPropertyTests
{
    // ── Concrete type pool for generating argument types ──────────────────
    private static readonly IType[] TypePool =
    [
        LegacyPrimitiveTypes.Int,
        LegacyPrimitiveTypes.String,
        LegacyPrimitiveTypes.Bool,
        LegacyPrimitiveTypes.Float,
        LegacyPrimitiveTypes.Double,
        LegacyPrimitiveTypes.Long,
        LegacyPrimitiveTypes.Char,
    ];

    // ═══════════════════════════════════════════════════════════════════════
    // Property 13: inferred pack elements match argument types in source order.
    // **Validates: Requirements 14.1, 14.2, 14.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(Arbitrary = new[] { typeof(VariadicsInferenceAgreementPropertyTests) }, MaxTest = 150)]
    public bool InferredPackMatchesArgumentTypes(InferenceAgreementScenario scenario)
    {
        // Build the type-parameter list: [T0, ..., T_{leading-1}, params TArgs, T_{leading}, ...]
        var typeParameters = new List<IType>();
        for(var i = 0; i < scenario.LeadingFixed; i++)
        {
            typeParameters.Add(new TypeParameterType($"TFixed{i}", i));
        }

        var packOrdinal = scenario.LeadingFixed;
        var packTP = new TypeParameterType("TArgs", packOrdinal) { IsPack = true, IsUnbracketedPack = true };
        typeParameters.Add(packTP);

        for(var i = 0; i < scenario.TrailingFixed; i++)
        {
            typeParameters.Add(new TypeParameterType($"TTrailing{i}", packOrdinal + 1 + i));
        }

        // Build parameter types corresponding to the type parameters:
        // Each fixed type param gets its own positional param; the pack gets a TupleType wrapper.
        var parameterTypes = new List<IType>();
        for(var i = 0; i < scenario.LeadingFixed; i++)
        {
            parameterTypes.Add(new TypeParameterType($"TFixed{i}", i));
        }

        parameterTypes.Add(new TupleType(new IType[] { packTP }));

        for(var i = 0; i < scenario.TrailingFixed; i++)
        {
            parameterTypes.Add(new TypeParameterType($"TTrailing{i}", packOrdinal + 1 + i));
        }

        // Invoke inference.
        var result = TypeArgumentInferenceEngine.TryInfer(
            typeParameters, parameterTypes, scenario.ArgumentTypes);

        if(!scenario.ShouldSucceed)
        {
            // Too few arguments for the fixed params -> inference should fail.
            Assert.False(result.Success, $"Expected failure for scenario: {scenario}");
            return true;
        }

        // Inference should succeed.
        Assert.True(result.Success, $"Expected success for scenario: {scenario}");
        Assert.NotNull(result.InferredTypeArguments);
        Assert.Equal(typeParameters.Count, result.InferredTypeArguments!.Count);

        // Verify leading fixed type params were inferred correctly.
        for(var i = 0; i < scenario.LeadingFixed; i++)
        {
            Assert.Equal(
                scenario.ArgumentTypes[i].DisplayName,
                result.InferredTypeArguments[i].DisplayName);
        }

        // Verify the pack inference: element types match the middle slice in source order.
        var inferredPackArg = result.InferredTypeArguments[packOrdinal];
        var inferredPack = Assert.IsType<TupleType>(inferredPackArg);
        var expectedElements = scenario.ExpectedPackElements;

        Assert.Equal(expectedElements.Count, inferredPack.ElementTypes.Count);
        for(var i = 0; i < expectedElements.Count; i++)
        {
            Assert.Equal(
                expectedElements[i].DisplayName,
                inferredPack.ElementTypes[i].DisplayName);
        }

        // Verify trailing fixed type params were inferred correctly.
        for(var i = 0; i < scenario.TrailingFixed; i++)
        {
            var trailingArgIndex = scenario.ArgumentTypes.Count - scenario.TrailingFixed + i;
            Assert.Equal(
                scenario.ArgumentTypes[trailingArgIndex].DisplayName,
                result.InferredTypeArguments[packOrdinal + 1 + i].DisplayName);
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generator
    // ═══════════════════════════════════════════════════════════════════════

    public static Arbitrary<InferenceAgreementScenario> InferenceAgreementScenarios()
    {
        return GenScenario().ToArbitrary();
    }

    private static Gen<InferenceAgreementScenario> GenScenario()
    {
        // Mix of scenarios: sole pack (0 fixed), leading+trailing fixed, and
        // scenarios with too few args (failure case).
        return Gen.OneOf(
            GenSolePackScenario(),
            GenMixedFixedPackScenario(),
            GenTooFewArgsScenario());
    }

    /// <summary>Sole pack: zero fixed params, 0-5 arguments.</summary>
    private static Gen<InferenceAgreementScenario> GenSolePackScenario()
    {
        return
            from argCount in Gen.Choose(0, 5)
            from args in Gen.ArrayOf(GenType(), argCount)
            select new InferenceAgreementScenario(0, 0, args);
    }

    /// <summary>Mixed fixed + pack: 0-2 leading, 0-2 trailing, enough args to succeed.</summary>
    private static Gen<InferenceAgreementScenario> GenMixedFixedPackScenario()
    {
        return
            from leading in Gen.Choose(0, 2)
            from trailing in Gen.Choose(0, 2)
            from packSize in Gen.Choose(0, 4)
            let totalArgs = leading + packSize + trailing
            from args in Gen.ArrayOf(GenType(), totalArgs)
            select new InferenceAgreementScenario(leading, trailing, args);
    }

    /// <summary>Too few args: at least 1 fixed param, fewer args than fixed total.</summary>
    private static Gen<InferenceAgreementScenario> GenTooFewArgsScenario()
    {
        return
            from leading in Gen.Choose(1, 2)
            from trailing in Gen.Choose(0, 2)
            let totalFixed = leading + trailing
            from argCount in Gen.Choose(0, totalFixed - 1)
            from args in Gen.ArrayOf(GenType(), argCount)
            select new InferenceAgreementScenario(leading, trailing, args);
    }

    private static Gen<IType> GenType()
    {
        return Gen.Elements(TypePool);
    }
}