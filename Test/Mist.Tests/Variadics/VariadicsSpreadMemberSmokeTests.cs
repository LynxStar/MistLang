// Feature: variadics - Smoke check for spread-generated members (Task 5.4)
// Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7, 10.8
//
// A minimal in-process smoke test confirming that a member-declaring spread of a
// type-parameter pack is expanded into a spread-generated member family:
//   * `public TArgs... Item;` surfaces an aggregate field `Item` of the value-pack tuple
//     type (TArgs...) and records a SpreadMember family with the declaration's
//     accessibility and Field kind (10.1);
//   * `public TArgs... Slot { get; }` does the same as a Property family (10.1);
//   * two spreads of the SAME pack (`Key...`, `Value...`) both reference that one pack, so
//     their element projections correlate at the same N (10.8);
//   * the aggregate type carries the pack as its single symbolic element, so the N
//     projections are materialized only when the pack binds -- empty/length-1 packs fall
//     out of that deferral (10.6, 10.7);
//   * the stem namespace is reserved: a regular member named `Item1` collides with the
//     stem `Item` (10.2/10.3 -> MIST_VAR_0007), and two stems that prefix one another
//     (`Item`, `Item1`) collide.
//
// The full generated-member-count property test is Task 5.8 and the edge-case unit tests
// (including MIST_VAR_0008) are Task 5.9. This drives ProgramModelBuilder.Build directly:
// there are ZERO external process spawns.

using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// Task 5.4 smoke tests for spread-generated members. Each test builds the program model
/// in-process and inspects the aggregate member plus the recorded
/// <see cref="SpreadMember"/> family, or the <c>MIST_VAR_0007</c> collision diagnostic.
/// </summary>
public sealed class VariadicsSpreadMemberSmokeTests : IDisposable
{
    private const string SpreadStemCollisionId = "MIST_VAR_0007";

    private readonly string outputDir;

    public VariadicsSpreadMemberSmokeTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistVariadicsSpreadMember_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try
            {
                Directory.Delete(outputDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup of the temporary build directory.
            }
        }

        GC.SuppressFinalize(this);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 10.1 - field spread: aggregate `Item : (TArgs...)` + family.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FieldSpread_ProducesAggregateFieldAndRecordsFamily()
    {
        var (model, _) = Build(
            "public class Bundle<params TArgs>\n"
            + "{\n"
            + "    public TArgs... Item;\n"
            + "}");
        var bundle = FindConcreteType(model, "Bundle");
        var pack = Assert.Single(bundle.TypeParametersList);

        // The aggregate is a real field named by the stem, of the value-pack tuple type.
        var aggregate = Assert.Single(bundle.FieldsList);
        Assert.Equal("Item", aggregate.Name);
        var tuple = Assert.IsAssignableFrom<ITupleType>(aggregate.Type);

        // The aggregate carries the pack as its single symbolic element (TArgs...); the N
        // element projections follow when the pack binds (10.6/10.7 handled by deferral).
        var element = Assert.Single(tuple.ElementTypes);
        Assert.Same(pack, element);

        // The spread-generated member family is recorded: stem, pack, aggregate type, kind.
        var family = Assert.Single(bundle.SpreadMembersList);
        Assert.Equal("Item", family.Stem);
        Assert.Same(pack, family.Pack);
        Assert.Same(aggregate.Type, family.AggregateType);
        Assert.Equal(SpreadMemberKind.Field, family.Kind);

        // Aggregate and family take the declaration's accessibility (public).
        Assert.Equal(Accessibility.Public, aggregate.Modifiers.Accessibility);
        Assert.Equal(Accessibility.Public, family.Modifiers.Accessibility);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 10.1 - property spread: aggregate `Slot : (TArgs...)` + family.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void PropertySpread_ProducesAggregatePropertyAndRecordsFamily()
    {
        var (model, _) = Build(
            "public class Row<params TArgs>\n"
            + "{\n"
            + "    public TArgs... Slot { get; }\n"
            + "}");
        var row = FindConcreteType(model, "Row");
        var pack = Assert.Single(row.TypeParametersList);

        // The aggregate is a real property named by the stem, of the value-pack tuple type.
        var aggregate = Assert.Single(row.PropertiesList);
        Assert.Equal("Slot", aggregate.Name);
        _ = Assert.IsAssignableFrom<ITupleType>(aggregate.Type);
        Assert.NotNull(aggregate.Getter);

        // No field aggregate is produced for a property spread.
        Assert.Empty(row.FieldsList);

        var family = Assert.Single(row.SpreadMembersList);
        Assert.Equal("Slot", family.Stem);
        Assert.Same(pack, family.Pack);
        Assert.Equal(SpreadMemberKind.Property, family.Kind);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 10.8 - two spreads of the same pack correlate at the same N.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CorrelatedSpreads_ShareTheSamePack()
    {
        var (model, _) = Build(
            "public class Pair<params TArgs>\n"
            + "{\n"
            + "    public TArgs... Key;\n"
            + "    public TArgs... Value;\n"
            + "}");
        var pair = FindConcreteType(model, "Pair");
        var pack = Assert.Single(pair.TypeParametersList);

        // Two distinct families, one per spread, both over the same pack instance -- so
        // their projections Key1..KeyN and Value1..ValueN correlate at the same N.
        Assert.Equal(2, pair.SpreadMembersList.Count);
        var key = pair.SpreadMembersList.Single(s => s.Stem == "Key");
        var value = pair.SpreadMembersList.Single(s => s.Stem == "Value");
        Assert.Same(pack, key.Pack);
        Assert.Same(pack, value.Pack);

        // Both aggregate fields are present and named by their stems.
        Assert.Equal(2, pair.FieldsList.Count);
        Assert.Contains(pair.FieldsList, f => f.Name == "Key");
        Assert.Contains(pair.FieldsList, f => f.Name == "Value");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 10.2 / 10.3 - a member outside the family using an `S{i}` name
    // collides with the stem `S` (MIST_VAR_0007).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void RegularMemberInStemNamespace_EmitsMistVar0007()
    {
        var (_, diagnostics) = Build(
            "public class Bad<params TArgs>\n"
            + "{\n"
            + "    public TArgs... Item;\n"
            + "    public int Item1;\n"
            + "}");

        Assert.Equal(1, CountDiagnostics(diagnostics, SpreadStemCollisionId));
        var diagnostic = diagnostics.Single(d => d.Id.Value == SpreadStemCollisionId);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Item", diagnostic.Message);
    }

    [Fact]
    public void NonCollidingMembers_DoNotEmitMistVar0007()
    {
        var (_, diagnostics) = Build(
            "public class Fine<params TArgs>\n"
            + "{\n"
            + "    public TArgs... Item;\n"
            + "    public int Count;\n"
            + "}");

        Assert.Equal(0, CountDiagnostics(diagnostics, SpreadStemCollisionId));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 10.2 / 10.3 - two spread stems that prefix one another collide.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TwoStemsPrefixingOneAnother_EmitMistVar0007()
    {
        var (_, diagnostics) = Build(
            "public class Bad<params TArgs>\n"
            + "{\n"
            + "    public TArgs... Item;\n"
            + "    public TArgs... Item1;\n"
            + "}");

        // Reported once, on the later stem, since `Item` prefixes `Item1`.
        Assert.Equal(1, CountDiagnostics(diagnostics, SpreadStemCollisionId));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private (IProgramModel Model, ImmutableArray<Diagnostic> Diagnostics) Build(string source)
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
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create("test.mist"),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
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

    private static ConcreteType FindConcreteType(IProgramModel model, string simpleName)
    {
        var matches = model.TypesByFQN.Values
            .OfType<ConcreteType>()
            .Where(t => t.Origin == TypeOrigin.Source && SimpleName(t.Name) == simpleName)
            .ToList();

        Assert.True(
            matches.Count == 1,
            $"Expected exactly one source type named '{simpleName}', found {matches.Count}.");
        return matches[0];
    }

    private static string SimpleName(string typeName)
    {
        var angle = typeName.IndexOf('<', StringComparison.Ordinal);
        return angle < 0 ? typeName : typeName[..angle];
    }

    private static int CountDiagnostics(ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Count(d => d.Id.Value == id);
    }
}