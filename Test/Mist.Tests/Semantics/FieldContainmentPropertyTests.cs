// Feature: substructural-field-discipline, Property 1: Discipline-Compatibility Matrix
//
// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.7**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for the field-containment rule using the discipline-compatibility matrix.
///
/// Feature: substructural-field-discipline, Property 1: Discipline-Compatibility Matrix
/// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.7**
///
/// The discipline-compatibility matrix determines which container/field discipline pairs are valid:
/// - Linear container → holds anything (linear, affine, relevant, unrestricted).
/// - Affine container → holds only affine + unrestricted.
/// - Relevant container → holds only relevant + unrestricted.
/// - Unrestricted/Owned container → holds only unrestricted.
///
/// The model is built fully in-process via a <see cref="CompilerDriver"/> that runs the
/// <see cref="DropAnalyzer"/> pipeline; diagnostics are inspected directly. Spawns 0
/// external processes.
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
        if(Directory.Exists(_outputDir))
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

    /// <summary>
    /// Container disciplines with their enum-equivalent for matrix lookup.
    /// </summary>
    private enum ContainerDisciplineKind { None, Owned, Affine, Linear, Relevant }

    /// <summary>
    /// Field disciplines with their enum-equivalent for matrix lookup.
    /// </summary>
    private enum FieldDisciplineKind { None, Affine, Linear, Relevant }

    private static readonly (string Keyword, ContainerDisciplineKind Kind)[] ContainerDisciplines =
    [
        ("", ContainerDisciplineKind.None),           // unrestricted class
        ("owned", ContainerDisciplineKind.Owned),     // explicit owned class (unrestricted rules)
        ("affine", ContainerDisciplineKind.Affine),   // affine class
        ("linear", ContainerDisciplineKind.Linear),   // linear class
        ("relevant", ContainerDisciplineKind.Relevant), // relevant class
    ];

    private static readonly (string Keyword, string TypeName, FieldDisciplineKind Kind)[] FieldDisciplines =
    [
        ("", "PlainField", FieldDisciplineKind.None),           // unrestricted field
        ("affine", "AffineField", FieldDisciplineKind.Affine),  // affine field
        ("linear", "LinearField", FieldDisciplineKind.Linear),  // linear field
        ("relevant", "RelevantField", FieldDisciplineKind.Relevant), // relevant field
    ];

    /// <summary>
    /// Implements the discipline-compatibility matrix to determine expected error state.
    /// </summary>
    private static bool MatrixExpectsError(ContainerDisciplineKind container, FieldDisciplineKind field)
    {
        // Unrestricted fields are always permitted.
        if(field == FieldDisciplineKind.None)
        {
            return false;
        }

        return container switch
        {
            ContainerDisciplineKind.Linear => false, // Linear holds anything.
            ContainerDisciplineKind.Affine => field != FieldDisciplineKind.Affine,
            ContainerDisciplineKind.Relevant => field != FieldDisciplineKind.Relevant,
            _ => true, // Unrestricted/Owned holds only unrestricted.
        };
    }

    private static Gen<FieldContainmentScenario> GenScenario()
    {
        return from container in Gen.Elements(ContainerDisciplines)
               from field in Gen.Elements(FieldDisciplines)
               select Build(container, field);
    }

    private static FieldContainmentScenario Build(
        (string Keyword, ContainerDisciplineKind Kind) container,
        (string Keyword, string TypeName, FieldDisciplineKind Kind) field)
    {
        var expectError = MatrixExpectsError(container.Kind, field.Kind);

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
        var label = $"container={containerLabel}, "
            + $"field={fieldLabel} => expectError={expectError}";

        return new FieldContainmentScenario(
            source, containerLabel, fieldLabel, expectError, label);
    }

    public static Arbitrary<FieldContainmentScenario> ArbitraryFieldContainmentScenario() =>
        GenScenario().ToArbitrary();

    private static int CountFieldNotOwned(ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics.Count(d => d.Id.Value == FieldNotOwnedId);

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Discipline-Compatibility Matrix
    //
    // A MIST_DROP_FIELD_NOT_OWNED diagnostic is present IFF the discipline-
    // compatibility matrix rejects the (container, field) pair; otherwise absent.
    //
    // **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.7**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(FieldContainmentPropertyTests) })]
    public Property FieldContainment_ErrorIffNonOwnedContainerWithOwnedField(
        FieldContainmentScenario scenario)
    {
        var diagnostics = Compile(scenario.Source);
        var count = CountFieldNotOwned(diagnostics);

        if(scenario.ExpectError)
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
    // Unrestricted container + substructural field => error (unrestricted holds only unrestricted).
    [InlineData("class", "AffineField", true)]
    [InlineData("class", "LinearField", true)]
    [InlineData("class", "RelevantField", true)]
    // Owned container + substructural field => error (owned follows unrestricted rules).
    [InlineData("owned class", "AffineField", true)]
    [InlineData("owned class", "LinearField", true)]
    [InlineData("owned class", "RelevantField", true)]
    // Affine container: holds affine + unrestricted only.
    [InlineData("affine class", "AffineField", false)]
    [InlineData("affine class", "LinearField", true)]
    [InlineData("affine class", "RelevantField", true)]
    [InlineData("affine class", "PlainField", false)]
    // Linear container: holds anything.
    [InlineData("linear class", "AffineField", false)]
    [InlineData("linear class", "LinearField", false)]
    [InlineData("linear class", "RelevantField", false)]
    [InlineData("linear class", "PlainField", false)]
    // Relevant container: holds relevant + unrestricted only.
    [InlineData("relevant class", "AffineField", true)]
    [InlineData("relevant class", "LinearField", true)]
    [InlineData("relevant class", "RelevantField", false)]
    [InlineData("relevant class", "PlainField", false)]
    // Unrestricted field => always permitted.
    [InlineData("class", "PlainField", false)]
    public void FieldContainment_ReportsExpectedDiagnostic(
        string containerKeyword, string fieldTypeName, bool expectError)
    {
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

        if(expectError)
        {
            Assert.Equal(1, count);
        }
        else
        {
            Assert.Equal(0, count);
        }
    }
}