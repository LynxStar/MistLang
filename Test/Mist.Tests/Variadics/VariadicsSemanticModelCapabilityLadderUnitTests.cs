// Feature: variadics, task 13.5: unit tests for the pack position-semantics capability ladder over
// the CLASSIC (legacy Mist.Semantics) pipeline.
// **Validates: Requirements 6.3, 6.4, 6.6, 6.7**
//
// The dedicated example-based unit suite for the capability ladder. It covers the four ladder
// transitions with explicit, enumerated cases (broader than the task 13.1 smoke facts and
// complementary to the task 13.3 property):
//   - bare-type rejection            -> MIST_VAR_0002 (6.3), across field / return / parameter
//                                       positions and both the `params` and `Type[]:` spellings
//   - unproven-index rejection       -> MIST_VAR_0003 (6.4), for several indices and both
//                                       unknown-length spellings
//   - out-of-bounds fixed-size index -> MIST_VAR_0004 (6.6), for several (N, index) pairs, with the
//                                       in-range companions accepted (the 6.5 boundary)
//   - guard-narrowing recovery       -> in-range indexing of the narrowed binding is permitted and
//                                       out-of-range is still bounds-checked (6.7)
//
// The bare-type / index rungs run during SemanticModelBuilder construction and are checked via the
// in-process BuildModel helper (no stdlib, no analyzers, no emit). Guard narrowing rides on the
// is-narrowing / array-bounds machinery in TypeCheckerAnalyzer and needs the Type metatype, so
// those cases compile through the full in-process pipeline. Either way there are ZERO external
// process spawns: emit is in-memory and no compiled DLL is ever run.

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// Example-based unit tests for the pack position-semantics capability ladder over the classic
/// (legacy <c>Mist.Semantics</c>) pipeline: bare-type rejection (<c>MIST_VAR_0002</c>),
/// unproven-index rejection (<c>MIST_VAR_0003</c>), out-of-bounds fixed-size indexing
/// (<c>MIST_VAR_0004</c>), and guard-narrowing recovery. <b>Validates: Requirements 6.3, 6.4, 6.6,
/// 6.7.</b>
/// </summary>
public sealed class VariadicsSemanticModelCapabilityLadderUnitTests
{
    private const string PackInTypePositionId = "MIST_VAR_0002";
    private const string UnprovenPackIndexId = "MIST_VAR_0003";
    private const string PackIndexOutOfBoundsId = "MIST_VAR_0004";
    private const string RefinementBoundsId = "MIST_REFINEMENT_0005";

    // ===== Requirement 6.3: a bare pack in type position -> MIST_VAR_0002 =====

    [Theory]
    [InlineData("params TArgs", "public TArgs Item;")]
    [InlineData("params TArgs", "public TArgs Get() { }")]
    [InlineData("params TArgs", "public void Use(TArgs single) { }")]
    [InlineData("Type[]:TArgs", "public TArgs Item;")]
    [InlineData("Type[]:TArgs", "public TArgs Get() { }")]
    public void BareTypePosition_EmitsMistVar0002(string packHeader, string member)
    {
        var diagnostics = BuildModel(
            "public class Bundle<" + packHeader + ">\n{\n    " + member + "\n}");

        // A pack is a value, not a type: using it bare in any type position is an error.
        Assert.Contains(
            diagnostics,
            d => (d.Id.Value == PackInTypePositionId) && (d.Severity == DiagnosticSeverity.Error));
    }

    // ===== Requirement 6.4: indexing an unknown-length pack -> MIST_VAR_0003 =====

    [Theory]
    [InlineData("params TArgs", 0)]
    [InlineData("params TArgs", 1)]
    [InlineData("params TArgs", 2)]
    [InlineData("Type[]:TArgs", 0)]
    [InlineData("Type[]:TArgs", 3)]
    public void UnprovenIndexOnUnknownLengthPack_EmitsMistVar0003(string packHeader, int index)
    {
        var diagnostics = BuildModel(
            "public class Bundle<" + packHeader + ">\n{\n    public TArgs[" + index + "] Field;\n}");

        // An unknown-length pack cannot prove its length is at least index + 1.
        Assert.Contains(diagnostics, d => d.Id.Value == UnprovenPackIndexId);

        // It is the unproven-index case, NOT a bare-type or out-of-bounds error.
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == PackInTypePositionId);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == PackIndexOutOfBoundsId);
    }

    // ===== Requirement 6.5 (boundary): a fixed-size Type[N] pack indexed in range is accepted =====

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 0)]
    [InlineData(3, 2)]
    [InlineData(4, 3)]
    public void InRangeIndexOnFixedSizePack_IsAccepted(int arity, int index)
    {
        var diagnostics = BuildModel(
            "public struct Pair<Type[" + arity + "]:TArgs>\n{\n    public TArgs[" + index + "] Field;\n}");

        // Indices 0..N-1 are in range for a fixed-size Type[N] pack, so none of the pack
        // position/index diagnostics fire.
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == PackInTypePositionId);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == UnprovenPackIndexId);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == PackIndexOutOfBoundsId);
    }

    // ===== Requirement 6.6: a fixed-size Type[N] pack indexed out of range -> MIST_VAR_0004 =====

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 3)]
    [InlineData(3, 5)]
    [InlineData(4, 4)]
    public void OutOfRangeIndexOnFixedSizePack_EmitsMistVar0004(int arity, int index)
    {
        var diagnostics = BuildModel(
            "public struct Pair<Type[" + arity + "]:TArgs>\n{\n    public TArgs[" + index + "] Field;\n}");

        // Index >= N is a comptime out-of-bounds error on the proven fixed-size pack.
        Assert.Contains(diagnostics, d => d.Id.Value == PackIndexOutOfBoundsId);

        // Because the length IS proven, it is not the unproven-index case, nor a bare-type error.
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == UnprovenPackIndexId);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == PackInTypePositionId);
    }

    // ===== Requirement 6.7: guard narrowing restores in-range indexing within the branch =====

    [Theory]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    [InlineData(3, 2)]
    [InlineData(4, 1)]
    public void GuardNarrowingToFixedSize_RestoresInRangeIndexing(int arity, int index)
    {
        var ids = CompileWithStdlib(BuildGuardSource(arity, index));

        // Within the narrowed branch the pack is proven Type[N], so in-range indexing of the
        // narrowed binding is permitted: no bounds/refinement violation and no pack diagnostics.
        Assert.DoesNotContain(RefinementBoundsId, ids);
        Assert.DoesNotContain(PackInTypePositionId, ids);
        Assert.DoesNotContain(UnprovenPackIndexId, ids);
        Assert.DoesNotContain(PackIndexOutOfBoundsId, ids);
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(3, 5)]
    public void GuardNarrowingToFixedSize_StillBoundsChecksOutOfRange(int arity, int index)
    {
        var ids = CompileWithStdlib(BuildGuardSource(arity, index));

        // Narrowing produces a genuine fixed-size Type[N] binding, so indexing it out of range is
        // still rejected by the ordinary array-bounds discipline.
        Assert.Contains(RefinementBoundsId, ids);
    }

    // =====================================================================
    // Helpers (ZERO external process spawns).
    // =====================================================================

    private static string BuildGuardSource(int arity, int index)
    {
        return
            "using Standard;\n"
            + "namespace TestProject;\n"
            + "public class Bundle<params TArgs>\n"
            + "{\n"
            + "    public int Probe()\n"
            + "    {\n"
            + "        if (TArgs is Type[" + arity + "] narrowed)\n"
            + "        {\n"
            + "            var value = narrowed[" + index + "];\n"
            + "            return 0;\n"
            + "        }\n"
            + "\n"
            + "        return 1;\n"
            + "    }\n"
            + "}";
    }

    /// <summary>
    /// Compiles <paramref name="source"/> through the full classic pipeline in-process (loading the
    /// standard library, needed for the <c>Type</c> metatype used by guard narrowing) and returns
    /// the emitted diagnostic IDs. Emit is in-memory, so there are ZERO external process spawns.
    /// </summary>
    private static ImmutableArray<string> CompileWithStdlib(string source)
    {
        return VariadicsCompilerFixture.CompileDiagnosticIds(source, ProjectOutputType.Library, "Standard");
    }

    /// <summary>
    /// Parses <paramref name="source"/> and builds the legacy semantic model entirely in-process
    /// (no stdlib, no analyzers, no emit), returning every diagnostic collected during
    /// construction. The bare-type / index rungs run during model construction, so inspecting the
    /// builder's diagnostics is sufficient.
    /// </summary>
    private static ImmutableArray<Diagnostic> BuildModel(string source)
    {
        var parse = VariadicsCompilerFixture.Parse(source);
        Assert.NotNull(parse.Root);
        Assert.True(
            VariadicsCompilerFixture.ParsedCleanly(parse),
            $"Unit-test source is expected to parse without lex/parse errors: <{source}>");

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