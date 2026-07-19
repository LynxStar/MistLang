// Feature: variadics, task 13.1: pack position semantics and the capability ladder over the
// CLASSIC (legacy Mist.Semantics) pipeline. Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6.
//
// These smoke tests drive the classic SemanticModelBuilder entirely in-process (parse -> build
// semantic model -> inspect the emitted diagnostics / resolved types). The position semantics
// live in SemanticModelBuilder.ResolveSingleTypeRef (the pack-aware type-position path) and
// ResolvePrimary (the expression-position path), mirroring the frozen ProgramModel resolver
// (task 8.1) over the legacy model:
//   - a bare pack in type position                    -> MIST_VAR_0002 (6.3)
//   - indexing an unknown-length pack `TArgs[i]`       -> MIST_VAR_0003 (6.4)
//   - indexing a fixed-size `Type[N]` pack in range    -> accepted     (6.5)
//   - indexing a fixed-size `Type[N]` pack out of range -> MIST_VAR_0004 (6.6)
//   - a pack in expression position (`TArgs.Length`)   -> the comptime Type[] value (6.1, 6.2)
//   - the value-pack form `params TArgs args`          -> NOT flagged (names the pack)
//
// There are ZERO external process spawns: the builder runs directly and no backend, analyzer,
// or compiled program is ever invoked. This is the classic-track counterpart of the
// ProgramModel-track VariadicsCapabilityLadderSmokeTests (task 8.1). The dedicated
// capability-ladder unit suite (task 13.5) and the property test (task 13.3) exercise these
// rules more broadly; this file is the minimal in-process verification for 13.1.

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// Smoke tests for pack position semantics and the capability ladder over the classic
/// (legacy <c>Mist.Semantics</c>) pipeline (Requirements 6.1-6.6). Each builds the semantic
/// model in-process and asserts on the emitted diagnostics.
/// </summary>
public sealed class VariadicsSemanticModelCapabilityLadderSmokeTests
{
    private const string PackInTypePositionId = "MIST_VAR_0002";
    private const string UnprovenPackIndexId = "MIST_VAR_0003";
    private const string PackIndexOutOfBoundsId = "MIST_VAR_0004";

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 6.3: a bare pack in type position -> MIST_VAR_0002
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void BarePackInFieldType_EmitsMistVar0002()
    {
        var diagnostics = BuildModel(
            "public class Bundle<params TArgs>\n"
            + "{\n"
            + "    public TArgs Item;\n"
            + "}");

        // `public TArgs Item;` uses the pack directly as a type (not a spread, not an index):
        // a pack is a value, not a type.
        var diagnostic = Assert.Single(diagnostics, d => d.Id.Value == PackInTypePositionId);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("TArgs", diagnostic.Message);
    }

    [Fact]
    public void BarePackInReturnType_EmitsMistVar0002()
    {
        var diagnostics = BuildModel(
            "public class Bundle<params TArgs>\n"
            + "{\n"
            + "    public TArgs Get() { }\n"
            + "}");

        // A method whose declared return type is the bare pack is the same bare-type error.
        Assert.Contains(diagnostics, d => d.Id.Value == PackInTypePositionId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 6.4: indexing an unknown-length pack -> MIST_VAR_0003
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void IndexingUnknownLengthPack_EmitsMistVar0003()
    {
        var diagnostics = BuildModel(
            "public class Bundle<params TArgs>\n"
            + "{\n"
            + "    public TArgs[0] First;\n"
            + "}");

        // The unknown-length `params TArgs` pack cannot prove its length is at least 1, so
        // `TArgs[0]` is rejected.
        var diagnostic = Assert.Single(diagnostics, d => d.Id.Value == UnprovenPackIndexId);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);

        // ...and it is NOT misreported as a bare-type-position error.
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == PackInTypePositionId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 6.6: fixed-size Type[N] pack indexed out of range -> MIST_VAR_0004
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void IndexingFixedSizePackOutOfRange_EmitsMistVar0004()
    {
        var diagnostics = BuildModel(
            "public class Pair<Type[2]:TArgs>\n"
            + "{\n"
            + "    public TArgs[2] Bad;\n"
            + "}");

        // A fixed-size `Type[2]` pack has valid indices 0..1; index 2 is out of bounds.
        var diagnostic = Assert.Single(diagnostics, d => d.Id.Value == PackIndexOutOfBoundsId);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);

        // A fixed-size pack proves a length, so it is NOT the unproven-index case.
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == UnprovenPackIndexId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 6.5: fixed-size Type[N] pack indexed in range is accepted
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void IndexingFixedSizePackInRange_EmitsNoPackPositionDiagnostic()
    {
        var diagnostics = BuildModel(
            "public class Pair<Type[2]:TArgs>\n"
            + "{\n"
            + "    public TArgs[0] First;\n"
            + "    public TArgs[1] Second;\n"
            + "}");

        // Indices 0 and 1 are in range for a fixed-size `Type[2]` pack, so none of the pack
        // position/index diagnostics fire.
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == PackInTypePositionId);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == UnprovenPackIndexId);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == PackIndexOutOfBoundsId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // The value-pack form `params TArgs args` names the pack and must NOT be flagged
    // as a bare-type-position error (it becomes the tuple (TArgs...)).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValuePackParameter_IsNotFlaggedAsBareTypePosition()
    {
        var diagnostics = BuildModel(
            "public class Bundle<params TArgs>\n"
            + "{\n"
            + "    public Bundle(params TArgs values) { }\n"
            + "}");

        // `params TArgs values` legitimately names the pack to form its value tuple; the
        // bare-type-position error must not fire here.
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == PackInTypePositionId);
    }

    [Fact]
    public void BarePackParameterWithoutParams_EmitsMistVar0002()
    {
        var diagnostics = BuildModel(
            "public class Bundle<params TArgs>\n"
            + "{\n"
            + "    public void Use(TArgs single) { }\n"
            + "}");

        // Without the `params` modifier a bare pack parameter is an ordinary type position, so
        // the exemption does NOT apply: a pack is a value, not a type.
        Assert.Contains(diagnostics, d => d.Id.Value == PackInTypePositionId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 6.1, 6.2: a pack in expression position is the comptime Type[] value, so
    // `.Length` is available regardless of whether the pack's length is proven.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void PackLengthInExpressionPosition_ResolvesWithoutError()
    {
        var diagnostics = BuildModel(
            "public class Tup<params TArgs>\n"
            + "{\n"
            + "    public int Count()\n"
            + "    {\n"
            + "        return TArgs.Length;\n"
            + "    }\n"
            + "}");

        // `TArgs.Length` reads the arity in expression position: the pack surfaces as its
        // comptime Type[] value, so `.Length` resolves and none of the pack position/index
        // diagnostics fire, nor does an undefined-identifier error.
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == PackInTypePositionId);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == UnprovenPackIndexId);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_UNDEFINED_IDENTIFIER");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // A pack SPREAD member `public TArgs... Item;` is a spread-generated member (task 10.4),
    // not a bare type position, so the capability ladder must not flag it.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void PackSpreadMember_IsNotFlaggedAsBareTypePosition()
    {
        var diagnostics = BuildModel(
            "public class Bundle<params TArgs>\n"
            + "{\n"
            + "    public TArgs... Item;\n"
            + "}");

        // The spread `TArgs...` declares a spread-generated member family, distinct from the
        // bare-type `TArgs`; the capability ladder must not misfire on it.
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == PackInTypePositionId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 6.7: guard narrowing an unknown-length pack to a fixed size Type[N] restores
    // in-range indexing of the narrowed binding within that branch. This rides on the
    // expression-position pack value (the pack surfaces as a Type[] value that `is Type[N]` can
    // narrow) plus the existing is-narrowing and array-bounds discipline; it needs the standard
    // library (for the `Type` metatype), so it compiles through the full in-process pipeline.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GuardNarrowingToFixedSize_RestoresInRangeIndexing()
    {
        var diagnostics = CompileDiagnostics(
            "using Standard;\n"
            + "namespace TestProject;\n"
            + "public class Bundle<params TArgs>\n"
            + "{\n"
            + "    public int Probe()\n"
            + "    {\n"
            + "        if (TArgs is Type[3] narrowed)\n"
            + "        {\n"
            + "            var first = narrowed[0];\n"
            + "            return 0;\n"
            + "        }\n"
            + "        return 1;\n"
            + "    }\n"
            + "}");

        // Within the narrowed branch the pack is proven to be Type[3], so `narrowed[0]` (in range)
        // is permitted: no bounds/refinement violation and no pack position diagnostics.
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_REFINEMENT_0005");
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == PackInTypePositionId);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == UnprovenPackIndexId);
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == PackIndexOutOfBoundsId);
    }

    [Fact]
    public void GuardNarrowingToFixedSize_StillBoundsChecksOutOfRange()
    {
        var diagnostics = CompileDiagnostics(
            "using Standard;\n"
            + "namespace TestProject;\n"
            + "public class Bundle<params TArgs>\n"
            + "{\n"
            + "    public int Probe()\n"
            + "    {\n"
            + "        if (TArgs is Type[3] narrowed)\n"
            + "        {\n"
            + "            var bad = narrowed[5];\n"
            + "            return 0;\n"
            + "        }\n"
            + "        return 1;\n"
            + "    }\n"
            + "}");

        // Narrowing produces a genuine fixed-size Type[3] binding, so indexing it out of range is
        // rejected by the ordinary array-bounds discipline (Requirement 6.5/6.7 boundary).
        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_REFINEMENT_0005");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers (ZERO external process spawns).
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Parses <paramref name="source"/> and builds the legacy semantic model entirely
    /// in-process (no stdlib, no analyzers, no emit), returning every diagnostic collected
    /// during construction. The pack position semantics run during model construction, so
    /// inspecting the builder's diagnostics is sufficient for these smoke checks.
    /// </summary>
    private static ImmutableArray<Diagnostic> BuildModel(string source)
    {
        var parse = VariadicsCompilerFixture.Parse(source);
        Assert.NotNull(parse.Root);
        Assert.True(
            VariadicsCompilerFixture.ParsedCleanly(parse),
            $"Smoke-test source is expected to parse without lex/parse errors: <{source}>");

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

    /// <summary>
    /// Compiles <paramref name="source"/> through the full classic pipeline in-process via the
    /// shared <see cref="VariadicsCompilerFixture"/> (which loads the standard library, needed for
    /// the <c>Type</c> metatype used by guard narrowing) and returns every emitted diagnostic.
    /// Emit is in-memory, so there are ZERO external process spawns.
    /// </summary>
    private static ImmutableArray<Diagnostic> CompileDiagnostics(string source)
    {
        var result = VariadicsCompilerFixture.Compile(source, globalUsings: "Standard");
        return result.Diagnostics;
    }
}