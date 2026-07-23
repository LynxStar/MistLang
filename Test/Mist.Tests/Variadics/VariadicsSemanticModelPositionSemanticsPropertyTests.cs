// Feature: variadics, task 13.3: property test for position-semantics soundness over the
// CLASSIC (legacy Mist.Semantics) pipeline.
//
// Property 10: Position-semantics soundness.
// **Validates: Requirements 6.1, 6.3, 6.4, 6.5, 6.6, 6.7**
//
// For any pack identifier:
//   - expression position (TArgs.Length)          -> the comptime Type[] value, no diagnostic (6.1)
//   - bare type position (public TArgs Item;)      -> MIST_VAR_0002                            (6.3)
//   - indexing an unknown-length Type[] pack       -> MIST_VAR_0003                            (6.4)
//   - indexing a fixed-size Type[N] pack in range  -> accepted; out of range -> MIST_VAR_0004  (6.5, 6.6)
//   - guard-narrowing (TArgs is Type[N]) restores in-range indexing inside the branch          (6.7)
//
// This generalizes the task 13.1 smoke facts (VariadicsSemanticModelCapabilityLadderSmokeTests)
// and the task 13.5 example suite into one property over generated pack names, fixed arities, and
// indices. The semantic-model-observable rungs (6.1, 6.3, 6.4, 6.5, 6.6) run during
// SemanticModelBuilder construction and are checked via the in-process BuildModel helper (no
// stdlib, no analyzers, no emit). The guard-narrowing rung (6.7) rides on the is-narrowing /
// array-bounds machinery in TypeCheckerAnalyzer and needs the Type metatype, so those scenarios
// compile through the full in-process pipeline (VariadicsCompilerFixture.Compile, in-memory emit).
// Either way there are ZERO external process spawns: nothing is written to an output directory and
// no compiled DLL is ever run.

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// A generated position-semantics scenario: a complete, parseable Mist source that places a pack
/// identifier in one position (expression, bare type, unknown-length index, fixed-size index, or a
/// guard-narrowed index), together with the fixed arity and index used so the property can compute
/// the expected outcome independently.
/// </summary>
public sealed record PositionSemanticsScenario(
    string Kind,
    string Source,
    int FixedArity,
    int Index,
    string Description)
{
    public override string ToString()
    {
        return Description;
    }
}

/// <summary>
/// Property test for <b>Property 10 (Position-semantics soundness)</b> over the classic (legacy
/// <c>Mist.Semantics</c>) pipeline. Generates a pack identifier in each designated position and
/// asserts the capability ladder: expression-position value, <c>MIST_VAR_0002</c> for a bare type
/// position, <c>MIST_VAR_0003</c> for an unknown-length index, a fixed-size <c>Type[N]</c> index
/// accepted iff in range else <c>MIST_VAR_0004</c>, and guard narrowing restoring in-range
/// indexing within the branch. <b>Validates: Requirements 6.1, 6.3, 6.4, 6.5, 6.6, 6.7.</b>
/// </summary>
public sealed class VariadicsSemanticModelPositionSemanticsPropertyTests
{
    private const string PackInTypePositionId = "MIST_VAR_0002";
    private const string UnprovenPackIndexId = "MIST_VAR_0003";
    private const string PackIndexOutOfBoundsId = "MIST_VAR_0004";
    private const string RefinementBoundsId = "MIST_REFINEMENT_0005";
    private const string UndefinedIdentifierId = "MIST_UNDEFINED_IDENTIFIER";

    private const string ExpressionKind = "expression";
    private const string BareTypeKind = "bare-type";
    private const string UnknownIndexKind = "unknown-index";
    private const string FixedIndexKind = "fixed-index";
    private const string GuardNarrowKind = "guard-narrow";

    private static readonly string[] PackNames = ["TArgs", "TMid", "TElems", "TValues", "TShapes"];

    // =====================================================================
    // Property 10: position semantics are sound across every position and every proven length.
    // **Validates: Requirements 6.1, 6.3, 6.4, 6.5, 6.6, 6.7**
    // =====================================================================

    [Property(Arbitrary = new[] { typeof(VariadicsSemanticModelPositionSemanticsPropertyTests) }, MaxTest = 200)]
    public bool PositionSemantics_AreSound_OverLegacyModel(PositionSemanticsScenario scenario)
    {
        // The guard-narrowing rung needs the Type metatype, so it compiles through the full
        // in-process pipeline (still ZERO process spawns; emit is in-memory).
        if(scenario.Kind == GuardNarrowKind)
        {
            return CheckGuardNarrow(scenario);
        }

        var diagnostics = BuildModel(scenario.Source);

        if(scenario.Kind == ExpressionKind)
        {
            // Expression position: the pack surfaces as its comptime Type[] value, so `.Length`
            // resolves and none of the pack position/index diagnostics fire (6.1).
            Assert.DoesNotContain(diagnostics, d => d.Id.Value == PackInTypePositionId);
            Assert.DoesNotContain(diagnostics, d => d.Id.Value == UnprovenPackIndexId);
            Assert.DoesNotContain(diagnostics, d => d.Id.Value == PackIndexOutOfBoundsId);
            Assert.DoesNotContain(diagnostics, d => d.Id.Value == UndefinedIdentifierId);
            return true;
        }

        if(scenario.Kind == BareTypeKind)
        {
            // Bare type position: a pack is a value, not a type (6.3).
            Assert.Contains(diagnostics, d => d.Id.Value == PackInTypePositionId);
            return true;
        }

        if(scenario.Kind == UnknownIndexKind)
        {
            // Indexing an unknown-length pack cannot prove the length, so it is the unproven-index
            // case -- NOT a bare-type or out-of-bounds error (6.4).
            Assert.Contains(diagnostics, d => d.Id.Value == UnprovenPackIndexId);
            Assert.DoesNotContain(diagnostics, d => d.Id.Value == PackInTypePositionId);
            Assert.DoesNotContain(diagnostics, d => d.Id.Value == PackIndexOutOfBoundsId);
            return true;
        }

        if(scenario.Kind == FixedIndexKind)
        {
            var inRange = (scenario.Index >= 0) && (scenario.Index < scenario.FixedArity);
            if(inRange)
            {
                // A fixed-size Type[N] pack indexed in 0..N-1 is accepted (6.5).
                Assert.DoesNotContain(diagnostics, d => d.Id.Value == PackInTypePositionId);
                Assert.DoesNotContain(diagnostics, d => d.Id.Value == UnprovenPackIndexId);
                Assert.DoesNotContain(diagnostics, d => d.Id.Value == PackIndexOutOfBoundsId);
            }
            else
            {
                // Out of range on a fixed-size pack is a comptime out-of-bounds error -- and,
                // because the length IS proven, not the unproven-index case (6.6).
                Assert.Contains(diagnostics, d => d.Id.Value == PackIndexOutOfBoundsId);
                Assert.DoesNotContain(diagnostics, d => d.Id.Value == UnprovenPackIndexId);
                Assert.DoesNotContain(diagnostics, d => d.Id.Value == PackInTypePositionId);
            }

            return true;
        }

        throw new InvalidOperationException($"Unhandled scenario kind: {scenario.Kind}");
    }

    private static bool CheckGuardNarrow(PositionSemanticsScenario scenario)
    {
        var ids = VariadicsCompilerFixture.CompileDiagnosticIds(
            scenario.Source, ProjectOutputType.Library, "Standard");
        var inRange = (scenario.Index >= 0) && (scenario.Index < scenario.FixedArity);
        if(inRange)
        {
            // Inside the narrowed branch the pack is proven Type[N], so in-range indexing of the
            // narrowed binding is permitted (6.7): no bounds/refinement violation and no pack
            // position/index diagnostics.
            Assert.DoesNotContain(RefinementBoundsId, ids);
            Assert.DoesNotContain(PackInTypePositionId, ids);
            Assert.DoesNotContain(UnprovenPackIndexId, ids);
            Assert.DoesNotContain(PackIndexOutOfBoundsId, ids);
        }
        else
        {
            // Narrowing yields a genuine fixed-size Type[N] binding, so an out-of-range index is
            // still caught by the ordinary array-bounds discipline (the 6.7 boundary).
            Assert.Contains(RefinementBoundsId, ids);
        }

        return true;
    }

    // =====================================================================
    // Generator
    // =====================================================================

    /// <summary>Arbitrary registration for the property above.</summary>
    public static Arbitrary<PositionSemanticsScenario> Scenarios()
    {
        return GenScenario().ToArbitrary();
    }

    private static Gen<PositionSemanticsScenario> GenScenario()
    {
        return
            from pack in Gen.Elements(PackNames)
            from arity in Gen.Choose(1, 4)
            from index in Gen.Choose(0, arity + 2)
            from shape in Gen.Choose(0, 2)
            from bucket in Gen.Choose(0, 9)
            select BuildScenario(pack, arity, index, shape, bucket);
    }

    // Buckets map to categories so every rung is well populated while the guard-narrowing rung
    // (the only one that compiles through the full pipeline) stays a small fraction of the run.
    private static PositionSemanticsScenario BuildScenario(string pack, int arity, int index, int shape, int bucket)
    {
        if(bucket <= 1)
        {
            return new PositionSemanticsScenario(
                ExpressionKind, BuildExpressionSource(pack), -1, -1, "expression: " + pack + ".Length");
        }

        if(bucket <= 3)
        {
            return new PositionSemanticsScenario(
                BareTypeKind, BuildBareTypeSource(pack, shape), -1, -1, "bare-type[" + shape + "]: " + pack);
        }

        if(bucket <= 5)
        {
            return new PositionSemanticsScenario(
                UnknownIndexKind, BuildUnknownIndexSource(pack, index), -1, index,
                "unknown-index: " + pack + "[" + index + "]");
        }

        if(bucket <= 8)
        {
            return new PositionSemanticsScenario(
                FixedIndexKind, BuildFixedIndexSource(pack, arity, index), arity, index,
                "fixed-index: Type[" + arity + "]:" + pack + "[" + index + "]");
        }

        return new PositionSemanticsScenario(
            GuardNarrowKind, BuildGuardNarrowSource(pack, arity, index), arity, index,
            "guard-narrow: " + pack + " is Type[" + arity + "] then [" + index + "]");
    }

    private static string BuildExpressionSource(string pack)
    {
        return
            "public class Tup<params " + pack + ">\n"
            + "{\n"
            + "    public int Count()\n"
            + "    {\n"
            + "        return " + pack + ".Length;\n"
            + "    }\n"
            + "}";
    }

    private static string BuildBareTypeSource(string pack, int shape)
    {
        var member = shape switch
        {
            0 => "public " + pack + " Item;",
            1 => "public " + pack + " Get() { }",
            _ => "public void Use(" + pack + " single) { }",
        };

        return
            "public class Bundle<params " + pack + ">\n"
            + "{\n"
            + "    " + member + "\n"
            + "}";
    }

    private static string BuildUnknownIndexSource(string pack, int index)
    {
        return
            "public class Bundle<params " + pack + ">\n"
            + "{\n"
            + "    public " + pack + "[" + index + "] Field;\n"
            + "}";
    }

    private static string BuildFixedIndexSource(string pack, int arity, int index)
    {
        return
            "public struct Pair<Type[" + arity + "]:" + pack + ">\n"
            + "{\n"
            + "    public " + pack + "[" + index + "] Field;\n"
            + "}";
    }

    private static string BuildGuardNarrowSource(string pack, int arity, int index)
    {
        return
            "using Standard;\n"
            + "namespace TestProject;\n"
            + "public class Bundle<params " + pack + ">\n"
            + "{\n"
            + "    public int Probe()\n"
            + "    {\n"
            + "        if (" + pack + " is Type[" + arity + "] narrowed)\n"
            + "        {\n"
            + "            var value = narrowed[" + index + "];\n"
            + "            return 0;\n"
            + "        }\n"
            + "\n"
            + "        return 1;\n"
            + "    }\n"
            + "}";
    }

    // =====================================================================
    // In-process classic semantic-model construction (ZERO external process spawns).
    // =====================================================================

    private static ImmutableArray<Diagnostic> BuildModel(string source)
    {
        var parse = VariadicsCompilerFixture.Parse(source);
        Assert.NotNull(parse.Root);
        Assert.True(
            VariadicsCompilerFixture.ParsedCleanly(parse),
            $"Scenario source is expected to parse without lex/parse errors: <{source}>");

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(new SourceText("test.mist", source))));
        var diagnostics = new DiagnosticBag();
        var builder = new SemanticModelBuilder();

        _ = builder.Build(
            compilation,
            ImmutableArray.Create(parse.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);

        return diagnostics.ToImmutableArray();
    }
}