// Feature: variadics, task 19.4: property test for unbounded front end and backend ceiling
// (classic pipeline).
//
// Property 14: Unbounded front end, backend ceiling.
// **Validates: Requirements 16.1, 16.2, 16.3, 16.4, 16.5**
//
// Asserts:
//   - The grammar, semantic model, and type checker never reject on pack length (Req 16.1).
//   - Only the backend emits MIST_VAR_0010 when a Func/Action construction exceeds 16
//     input parameter types (Req 16.2, 16.4).
//   - ValueTuple over 7 elements is represented by nesting, NOT rejected (Req 16.3).
//   - Constructions within the CLR capacity emit cleanly without MIST_VAR_0010 (Req 16.5).
//
// The test uses specific boundary arities for Func (0, 1, 5, 7, 8, 15, 16, 17, 20) and
// verifies the front end is unbounded while the backend enforces the ceiling. For ValueTuple
// nesting, BuildValueTupleTypeName is exercised directly at various arities to confirm
// nesting rather than rejection.
//
// All tests run entirely in-process with ZERO external process spawns.

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// A generated arity ceiling scenario specifying the number of input parameters for a
/// <c>Func</c> construction. The oracle knows whether <c>MIST_VAR_0010</c> should fire.
/// </summary>
public sealed record ArityCeilingScenario(int InputCount)
{
    /// <summary>Func/Action ceiling is 16 inputs; over 16 emits MIST_VAR_0010.</summary>
    public bool ExpectsBackendDiagnostic => InputCount > 16;

    public override string ToString()
    {
        return $"Func inputs={InputCount}, expectMIST_VAR_0010={ExpectsBackendDiagnostic}";
    }
}

/// <summary>
/// Property test for <b>Property 14 (Unbounded front end, backend ceiling)</b> over the
/// classic pipeline. Generates <c>Func</c> constructions at various arities and asserts:
/// <list type="bullet">
///   <item>No front-end (grammar/semantic-model/type-checker) diagnostic rejects on length.</item>
///   <item><c>MIST_VAR_0010</c> is emitted only by the backend, only for arities over 16.</item>
///   <item>ValueTuple nesting at arity 8+ does NOT emit <c>MIST_VAR_0010</c>.</item>
/// </list>
/// <b>Validates: Requirements 16.1, 16.2, 16.3, 16.4, 16.5.</b>
/// </summary>
public sealed class VariadicsArityCeilingPropertyTests
{
    private const string ArityCeilingDiagnosticId = "MIST_VAR_0010";

    // ═══════════════════════════════════════════════════════════════════════
    // Property 14: Func/Action arity ceiling -- MIST_VAR_0010 only for >16 inputs.
    // **Validates: Requirements 16.1, 16.2, 16.4, 16.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(Arbitrary = new[] { typeof(VariadicsArityCeilingPropertyTests) }, MaxTest = 100)]
    public bool FuncArityCeiling_EmittedOnlyWhenOver16Inputs(ArityCeilingScenario scenario)
    {
        var source = BuildFuncSource(scenario.InputCount);
        var result = VariadicsCompilerFixture.Compile(source, globalUsings: "Standard");
        var diagnostics = result.Diagnostics;

        // Front-end stages (parsing, semantic model, analysis) must NOT reject on length.
        var frontEndErrors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => d.Stage is PipelineStage.Lexing or PipelineStage.Parsing
                or PipelineStage.SemanticModelConstruction or PipelineStage.Analysis)
            .Where(d => d.Id.Value == ArityCeilingDiagnosticId)
            .ToList();

        Assert.Empty(frontEndErrors);

        // The arity ceiling diagnostic should come only from the backend.
        var backendCeilingDiags = diagnostics
            .Where(d => d.Id.Value == ArityCeilingDiagnosticId)
            .ToList();

        if(scenario.ExpectsBackendDiagnostic)
        {
            // Over 16 inputs: MIST_VAR_0010 must be emitted from the backend.
            Assert.NotEmpty(backendCeilingDiags);
            Assert.All(backendCeilingDiags, d => Assert.Equal(PipelineStage.Backend, d.Stage));
        }
        else
        {
            // 16 or fewer inputs: no MIST_VAR_0010 at all.
            Assert.Empty(backendCeilingDiags);
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 14: ValueTuple nesting -- arity >7 does NOT emit MIST_VAR_0010.
    // **Validates: Requirements 16.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(Arbitrary = new[] { typeof(VariadicsArityCeilingPropertyTests) }, MaxTest = 100)]
    public bool ValueTupleNesting_NeverEmitsArityCeilingDiagnostic(ArityCeilingScenario scenario)
    {
        // ValueTuple packs (used as value packs) should nest rather than reject.
        var source = BuildValueTupleSource(scenario.InputCount);
        var result = VariadicsCompilerFixture.Compile(source, globalUsings: "Standard");
        var diagnostics = result.Diagnostics;

        // MIST_VAR_0010 should NEVER appear for ValueTuple constructions (they nest).
        var ceilingDiags = diagnostics
            .Where(d => d.Id.Value == ArityCeilingDiagnosticId)
            .ToList();

        Assert.Empty(ceilingDiags);

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 14: BuildValueTupleTypeName produces nesting at arity >7.
    // **Validates: Requirements 16.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(Arbitrary = new[] { typeof(VariadicsArityCeilingPropertyTests) }, MaxTest = 100)]
    public bool BuildValueTupleTypeName_NestsAtArity8Plus(ArityCeilingScenario scenario)
    {
        var arity = scenario.InputCount;
        var typeName = VariadicMonomorphizer.BuildValueTupleTypeName(arity);

        if(arity == 0)
        {
            // Arity 0: non-generic ValueTuple.
            Assert.Equal("System.ValueTuple", typeName);
        }
        else if(arity <= 7)
        {
            // Arity 1-7: flat ValueTuple (no nesting).
            Assert.DoesNotContain("System.ValueTuple<", typeName.Replace("System.ValueTuple<", "", StringComparison.Ordinal));
            Assert.StartsWith("System.ValueTuple<", typeName);
            // Count commas to verify arity.
            var commaCount = typeName.Count(c => c == ',');
            Assert.Equal(arity - 1, commaCount);
        }
        else
        {
            // Arity 8+: must contain nested ValueTuple (nesting indicator).
            Assert.Contains("System.ValueTuple<", typeName[(typeName.IndexOf('<') + 1)..]);
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Deterministic boundary tests (supplement to the property tests above)
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(5, false)]
    [InlineData(7, false)]
    [InlineData(8, false)]
    [InlineData(15, false)]
    [InlineData(16, false)]
    [InlineData(17, true)]
    [InlineData(20, true)]
    public void FuncArityCeiling_BoundaryValues(int inputCount, bool expectsDiagnostic)
    {
        var source = BuildFuncSource(inputCount);
        var result = VariadicsCompilerFixture.Compile(source, globalUsings: "Standard");
        var diagnostics = result.Diagnostics;

        var ceilingDiags = diagnostics
            .Where(d => d.Id.Value == ArityCeilingDiagnosticId)
            .ToList();

        if(expectsDiagnostic)
        {
            Assert.NotEmpty(ceilingDiags);
            Assert.All(ceilingDiags, d => Assert.Equal(PipelineStage.Backend, d.Stage));
        }
        else
        {
            Assert.Empty(ceilingDiags);
        }

        // In all cases, no front-end stage should emit MIST_VAR_0010.
        var frontEndCeilingDiags = diagnostics
            .Where(d => d.Id.Value == ArityCeilingDiagnosticId)
            .Where(d => d.Stage is not PipelineStage.Backend)
            .ToList();

        Assert.Empty(frontEndCeilingDiags);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(20)]
    public void ValueTupleArity_NeverEmitsCeilingDiagnostic(int elementCount)
    {
        var source = BuildValueTupleSource(elementCount);
        var result = VariadicsCompilerFixture.Compile(source, globalUsings: "Standard");
        var diagnostics = result.Diagnostics;

        var ceilingDiags = diagnostics
            .Where(d => d.Id.Value == ArityCeilingDiagnosticId)
            .ToList();

        Assert.Empty(ceilingDiags);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generator
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates arity ceiling scenarios with input counts spanning the interesting
    /// boundary values: 0, 1, 5, 7, 8, 15, 16, 17, 20.
    /// </summary>
    public static Arbitrary<ArityCeilingScenario> ArityCeilingScenarios()
    {
        return GenScenario().ToArbitrary();
    }

    private static Gen<ArityCeilingScenario> GenScenario()
    {
        // Mix boundary values (deterministic coverage) with random values (exploration).
        var boundaryValues = Gen.Elements(0, 1, 5, 7, 8, 15, 16, 17, 20);
        var randomValues = Gen.Choose(0, 20);

        return Gen.OneOf(boundaryValues, randomValues)
            .Select(n => new ArityCeilingScenario(n));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Source builders
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds a Mist source that declares a method accepting a
    /// <c>Func&lt;int, ..., int, int&gt;</c> parameter where the input count is
    /// <paramref name="inputCount"/>. The return type is always <c>int</c>,
    /// so the total type-argument count is <c>inputCount + 1</c>.
    /// Follows the same pattern as the passing unit tests in VariadicsArityCeilingUnitTests.
    /// </summary>
    private static string BuildFuncSource(int inputCount)
    {
        var typeArgs = new List<string>();
        for(var i = 0; i < inputCount; i++)
        {
            typeArgs.Add("int");
        }

        typeArgs.Add("int"); // TResult

        var typeArgStr = string.Join(", ", typeArgs);

        return $$"""
            using Standard;

            namespace CeilingTest;

            public class Consumer
            {
                public static int Use(Func<{{typeArgStr}}> f)
                {
                    return 0;
                }
            }
            """;
    }

    /// <summary>
    /// Builds a Mist source that declares a variadic class with a value pack of the given
    /// element count, exercising ValueTuple nesting at the backend. The pack is used in a
    /// construction site so the monomorphizer can see it.
    /// </summary>
    private static string BuildValueTupleSource(int elementCount)
    {
        var typeArgs = new List<string>();
        for(var i = 0; i < elementCount; i++)
        {
            typeArgs.Add("int");
        }

        var typeArgStr = typeArgs.Count > 0 ? string.Join(", ", typeArgs) : "";

        // Use a variadic class with a pack to exercise the ValueTuple representation.
        return $$"""
            using Standard;

            namespace CeilingTest;

            public class TupleHolder<params TArgs>
            {
                public TArgs... Item;
            }

            public class Consumer
            {
                public TupleHolder<{{typeArgStr}}> field;
            }
            """;
    }
}