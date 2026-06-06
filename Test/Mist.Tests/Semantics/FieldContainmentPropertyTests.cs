// Feature: drop-semantics, Property 13: Field containment rule
//
// **Validates: Requirements 11.1, 11.2, 11.3, 11.4**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for the field-containment rule.
///
/// Feature: drop-semantics, Property 13: Field containment rule
/// **Validates: Requirements 11.1, 11.2, 11.3, 11.4**
///
/// For any non-owned container type that declares a field whose type is <c>affine</c> or
/// <c>linear</c>, the compiler SHALL report <c>MIST_DROP_FIELD_NOT_OWNED</c>. For any owned
/// container (affine/linear/owned), or for any field whose type is <c>relevant</c> or
/// unrestricted, the compiler SHALL NOT report that diagnostic.
///
/// The model is built fully in-process via a <see cref="CompilerDriver"/> that runs the
/// <see cref="DropAnalyzer"/> pipeline; diagnostics are inspected directly. Spawns 0
/// external processes.
///
/// Containers are declared as <c>class</c> (never inferred owned) plus an explicit
/// <c>owned</c> variant for the owned-but-unrestricted-discipline case, so struct
/// ownership inference cannot flip <c>IsOwnedType</c> and confound the property. Field
/// types are named helper classes with each discipline.
/// </summary>
public sealed class FieldContainmentPropertyTests : IDisposable
{
    private const string FieldNotOwnedId = "MIST_DROP_FIELD_NOT_OWNED";

    private readonly string _outputDir;

    public FieldContainmentPropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistFieldContainmentPBT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Compile helper (in-process, 0 external processes) ───────────────

    private ImmutableArray<Diagnostic> Compile(string source)
    {
        var filePath = Path.Combine(_outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var driver = new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            new Mist.Semantics.Model.SemanticModelBuilder(),
            ImmutableArray.Create<IAnalyzer>(
                new TypeCheckerAnalyzer(),
                new ReadonlyAnalyzer(),
                new OwnershipAnalyzer(),
                new DropAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

        var options = new CompilerOptions(
            ProjectFolder: _outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: _outputDir,
            BinDirectory: Path.Combine(_outputDir, "bin"),
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return driver.Compile(options).Diagnostics;
    }

    // ─── Scenario ─────────────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// A generated container/field discipline combination together with whether the
    /// field-containment rule requires a <c>MIST_DROP_FIELD_NOT_OWNED</c> diagnostic.
    /// </summary>
    public sealed record FieldContainmentScenario(
        string Source,
        string ContainerDiscipline,
        string FieldDiscipline,
        bool ExpectError,
        string Label)
    {
        public override string ToString() => Label;
    }

#pragma warning restore CA1034

    // Container disciplines and whether each yields an OWNED container.
    //   affine/linear ⇒ owned (write-through), owned ⇒ owned, none/relevant ⇒ not owned.
    // Declared with `class` only (never struct) so ownership inference never flips the flag.
    private static readonly (string Keyword, bool IsOwned)[] ContainerDisciplines =
    [
        ("", false),          // unrestricted class — not owned
        ("owned", true),      // explicit owned class — owned
        ("affine", true),     // affine ⇒ owned
        ("linear", true),     // linear ⇒ owned
        ("relevant", false),  // relevant — not owned
    ];

    // Field-type disciplines and whether each is an affine/linear (owned) field.
    private static readonly (string Keyword, string TypeName, bool IsOwnedField)[] FieldDisciplines =
    [
        ("", "PlainField", false),         // unrestricted field — always permitted
        ("affine", "AffineField", true),   // affine field — requires owned container
        ("linear", "LinearField", true),   // linear field — requires owned container
        ("relevant", "RelevantField", false), // relevant field — always permitted
    ];

    private static Gen<FieldContainmentScenario> GenScenario()
    {
        return from container in Gen.Elements(ContainerDisciplines)
               from field in Gen.Elements(FieldDisciplines)
               select Build(container, field);
    }

    private static FieldContainmentScenario Build(
        (string Keyword, bool IsOwned) container,
        (string Keyword, string TypeName, bool IsOwnedField) field)
    {
        // Error is required iff a non-owned container declares an affine/linear field.
        var expectError = !container.IsOwned && field.IsOwnedField;

        var containerKeyword = container.Keyword.Length == 0
            ? "class"
            : $"{container.Keyword} class";

        var source = $$"""
            class PlainField
            {
            }
            affine class AffineField
            {
            }
            linear class LinearField
            {
            }
            relevant class RelevantField
            {
            }
            {{containerKeyword}} Container
            {
                {{field.TypeName}} member;
            }
            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var containerLabel = container.Keyword.Length == 0 ? "(none)" : container.Keyword;
        var fieldLabel = field.Keyword.Length == 0 ? "(none)" : field.Keyword;
        var label = $"container={containerLabel} (owned={container.IsOwned}), "
            + $"field={fieldLabel} => expectError={expectError}";

        return new FieldContainmentScenario(
            source, containerLabel, fieldLabel, expectError, label);
    }

    public static Arbitrary<FieldContainmentScenario> ArbitraryFieldContainmentScenario() =>
        GenScenario().ToArbitrary();

    private static int CountFieldNotOwned(ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics.Count(d => d.Id.Value == FieldNotOwnedId);

    // ═══════════════════════════════════════════════════════════════════════
    // Property 13: Field containment rule
    //
    // A MIST_DROP_FIELD_NOT_OWNED diagnostic is present IFF the container is
    // non-owned AND the field type is affine or linear; otherwise it is absent.
    //
    // **Validates: Requirements 11.1, 11.2, 11.3, 11.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(FieldContainmentPropertyTests) })]
    public Property FieldContainment_ErrorIffNonOwnedContainerWithOwnedField(
        FieldContainmentScenario scenario)
    {
        var diagnostics = Compile(scenario.Source);
        var count = CountFieldNotOwned(diagnostics);

        if (scenario.ExpectError)
        {
            return (count == 1).ToProperty()
                .Label($"Expected exactly one {FieldNotOwnedId} for {scenario.Label}, got {count}.\n"
                    + $"Source:\n{scenario.Source}");
        }

        return (count == 0).ToProperty()
            .Label($"Expected no {FieldNotOwnedId} for {scenario.Label}, got {count}.\n"
                + $"Source:\n{scenario.Source}");
    }

    // ─── Example-based sanity checks ──────────────────────────────────────

    [Theory]
    // Non-owned container + affine/linear field => error.
    [InlineData("class", "affine", "AffineField", true)]
    [InlineData("class", "linear", "LinearField", true)]
    [InlineData("relevant class", "affine", "AffineField", true)]
    [InlineData("relevant class", "linear", "LinearField", true)]
    // Owned container + affine/linear field => no error.
    [InlineData("owned class", "affine", "AffineField", false)]
    [InlineData("affine class", "linear", "LinearField", false)]
    [InlineData("linear class", "affine", "AffineField", false)]
    // Relevant / unrestricted field => always permitted.
    [InlineData("class", "relevant", "RelevantField", false)]
    [InlineData("class", "", "PlainField", false)]
    [InlineData("relevant class", "relevant", "RelevantField", false)]
    public void FieldContainment_ReportsExpectedDiagnostic(
        string containerKeyword, string fieldKeyword, string fieldTypeName, bool expectError)
    {
        var fieldDecl = fieldKeyword.Length == 0 ? "class" : $"{fieldKeyword} class";

        var source = $$"""
            class PlainField
            {
            }
            affine class AffineField
            {
            }
            linear class LinearField
            {
            }
            relevant class RelevantField
            {
            }
            {{containerKeyword}} Container
            {
                {{fieldTypeName}} member;
            }
            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var count = CountFieldNotOwned(Compile(source));

        if (expectError)
            Assert.Equal(1, count);
        else
            Assert.Equal(0, count);
    }
}
