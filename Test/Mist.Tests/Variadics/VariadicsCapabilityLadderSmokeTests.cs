// Feature: variadics - Smoke tests for pack position semantics and the capability ladder (Task 8.1)
// Requirements: 6.3, 6.4, 6.5, 6.6
//
// These smoke tests drive pack position semantics end-to-end in-process via
// ProgramModelBuilder.Build: source declaring variadic families uses a pack identifier in a
// type position, the program model is built, and the emitted diagnostics are inspected. The
// position semantics live in TypeReferenceResolver (the pack-aware ProgramModel resolution
// path), where a named reference that resolves to a type-parameter pack is either bare-type
// (MIST_VAR_0002), pack indexing under the capability ladder (MIST_VAR_0003 for an
// unknown-length pack, MIST_VAR_0004 for a fixed-size pack indexed out of range, and an
// accepted in-range fixed-size index), or the value-pack form `params TArgs args` (which
// names the pack and must NOT be flagged). There are ZERO external process spawns: the
// builder runs directly and emit is never invoked.
//
// The dedicated property test (Task 8.3) and the full capability-ladder unit tests (Task 8.5)
// exercise these rules more broadly; this file is the minimal in-process verification for 8.1.
// The expression-position value semantics (6.1, 6.2) and guard narrowing (6.7) require a
// ProgramModel body/expression checker that does not exist yet and are intentionally not
// covered here.

using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// Smoke tests for pack position semantics and the capability ladder (Requirement 6.3-6.6).
/// Each builds the program model in-process and asserts on the emitted diagnostics:
/// <list type="bullet">
///   <item>a bare pack in type position raises <c>MIST_VAR_0002</c> (6.3);</item>
///   <item>indexing an unknown-length pack raises <c>MIST_VAR_0003</c> (6.4);</item>
///   <item>indexing a fixed-size <c>Type[N]</c> pack out of range raises <c>MIST_VAR_0004</c> (6.6);</item>
///   <item>an in-range fixed-size index is accepted (no pack-position diagnostic) (6.5);</item>
///   <item>the value-pack form <c>params TArgs args</c> names the pack and is NOT flagged.</item>
/// </list>
/// </summary>
public sealed class VariadicsCapabilityLadderSmokeTests
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
        var (_, diagnostics) = BuildModel(
            """
            namespace TestProject;

            public class Bundle<params TArgs>
            {
                public TArgs Item;
            }
            """);

        // `public TArgs Item;` uses the pack directly as a type (not a spread, not an index):
        // a pack is a value, not a type.
        var diagnostic = Assert.Single(diagnostics, d => d.Id.Value == PackInTypePositionId);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("TArgs", diagnostic.Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 6.4: indexing an unknown-length pack -> MIST_VAR_0003
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void IndexingUnknownLengthPack_EmitsMistVar0003()
    {
        var (_, diagnostics) = BuildModel(
            """
            namespace TestProject;

            public class Bundle<params TArgs>
            {
                public TArgs[0] First;
            }
            """);

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
        var (_, diagnostics) = BuildModel(
            """
            namespace TestProject;

            public class Pair<Type[2]:TArgs>
            {
                public TArgs[2] Bad;
            }
            """);

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
        var (_, diagnostics) = BuildModel(
            """
            namespace TestProject;

            public class Pair<Type[2]:TArgs>
            {
                public TArgs[0] First;
                public TArgs[1] Second;
            }
            """);

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
        var (_, diagnostics) = BuildModel(
            """
            namespace TestProject;

            public class Bundle<params TArgs>
            {
                public Bundle(params TArgs values) { }
            }
            """);

        // `params TArgs values` legitimately names the pack to form its value tuple; the
        // bare-type-position error must not fire here.
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == PackInTypePositionId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Parses <paramref name="source"/> and builds the program model entirely in-process,
    /// returning the model together with every diagnostic collected during construction.
    /// Asserts the source parses without a lex/parse error.
    /// </summary>
    private static (IProgramModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModel(string source)
    {
        var lexer = new MistLexer();
        var parser = new MistParser();
        var parseDiagnostics = new DiagnosticBag();

        var sourceText = new SourceText("test.mist", source);
        var tokens = lexer.Lex(sourceText, parseDiagnostics);
        var parseResult = parser.Parse(tokens, parseDiagnostics);

        Assert.NotNull(parseResult.Root);

        var parseErrors = parseDiagnostics.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(
            parseErrors.Count == 0,
            $"Unexpected parse errors: {string.Join("; ", parseErrors.Select(d => $"{d.Id.Value} {d.Message}"))}");

        var unit = new CompilationUnit(sourceText);
        var compilation = new MistCompilation(ImmutableArray.Create(unit));

        var options = new CompilerOptions(
            ProjectFolder: ".",
            SourcePaths: ImmutableArray.Create("test.mist"),
            Backend: new BackendId("csharp"),
            OutputDirectory: ".",
            BinDirectory: "bin",
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        var buildDiagnostics = new DiagnosticBag();
        var model = ProgramModelBuilder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            options,
            buildDiagnostics,
            NullAmbientTypeProvider.Instance);

        Assert.NotNull(model);
        return (model!, buildDiagnostics.ToImmutableArray());
    }
}