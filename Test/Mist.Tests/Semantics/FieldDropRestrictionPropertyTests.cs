// Feature: drop-semantics, Property 14: Field drop restriction
//
// **Validates: Requirements 12.1, 12.2, 12.3**

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
/// Property-based tests for the field-drop restriction rule.
///
/// Feature: drop-semantics, Property 14: Field drop restriction
/// **Validates: Requirements 12.1, 12.2, 12.3**
///
/// For any call to <c>Drop()</c> on a field of an affine or linear type, the compiler SHALL
/// report <c>MIST_DROP_FIELD_OUTSIDE_CONTAINER</c> unless the call is a direct
/// <c>this.Field.Drop()</c> inside the containing type's own <c>Drop()</c> method. Relevant
/// (and unrestricted) fields are exempt from this restriction.
///
/// Containers are always declared with an owned discipline (<c>owned</c>/<c>affine</c>/
/// <c>linear</c>) so the field-containment rule (<c>MIST_DROP_FIELD_NOT_OWNED</c>) never fires
/// and confounds the signal under test. Generated programs perform exactly one field
/// <c>Drop()</c> call with no later use of the dropped value, so use-after-drop/move tracking
/// stays silent and <c>MIST_DROP_FIELD_OUTSIDE_CONTAINER</c> is the only signal. Diagnostics
/// are filtered to that specific id.
///
/// The model is built fully in-process via a <see cref="CompilerDriver"/> that runs the
/// <see cref="DropAnalyzer"/> pipeline; diagnostics are inspected directly. Spawns 0 external
/// processes.
/// </summary>
public sealed class FieldDropRestrictionPropertyTests : IDisposable
{
    private const string FieldOutsideContainerId = "MIST_DROP_FIELD_OUTSIDE_CONTAINER";

    private readonly string _outputDir;

    public FieldDropRestrictionPropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistFieldDropRestrictionPBT_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>The site at which the generated field <c>Drop()</c> call appears.</summary>
    public enum DropSite
    {
        /// <summary><c>this.Field.Drop()</c> inside the container's own <c>Drop()</c> method.</summary>
        InsideContainerDrop,

        /// <summary><c>this.Field.Drop()</c> inside a different method of the container.</summary>
        InsideOtherMethod,

        /// <summary><c>other.Field.Drop()</c> on another object's field from outside.</summary>
        ExternalObject,
    }

    /// <summary>
    /// A generated container-discipline / field-discipline / drop-site combination together with
    /// whether the field-drop restriction requires a <c>MIST_DROP_FIELD_OUTSIDE_CONTAINER</c>
    /// diagnostic.
    /// </summary>
    public sealed record FieldDropScenario(
        string Source,
        string ContainerDiscipline,
        string FieldDiscipline,
        DropSite Site,
        bool ExpectError,
        string Label)
    {
        public override string ToString() => Label;
    }

#pragma warning restore CA1034

    // Owned container disciplines — every one yields an owned container so the field-containment
    // rule (MIST_DROP_FIELD_NOT_OWNED) never fires.
    private static readonly string[] ContainerDisciplines = ["owned", "affine", "linear"];

    // Field-type disciplines and whether each is an affine/linear (owned-droppable) field.
    //   affine/linear ⇒ restricted (error outside container's Drop()).
    //   relevant/unrestricted ⇒ exempt (never an error).
    private static readonly (string Keyword, string TypeName, bool IsOwnedField)[] FieldDisciplines =
    [
        ("", "PlainField", false),             // unrestricted field — exempt
        ("affine", "AffineField", true),       // affine field — restricted
        ("linear", "LinearField", true),       // linear field — restricted
        ("relevant", "RelevantField", false),  // relevant field — exempt
    ];

    private static Gen<FieldDropScenario> GenScenario()
    {
        return from container in Gen.Elements(ContainerDisciplines)
               from field in Gen.Elements(FieldDisciplines)
               from site in Gen.Elements(Enum.GetValues<DropSite>())
               select Build(container, field, site);
    }

    private static FieldDropScenario Build(
        string containerDiscipline,
        (string Keyword, string TypeName, bool IsOwnedField) field,
        DropSite site)
    {
        // Error is required iff the field is affine/linear AND the call is not the permitted
        // this.Field.Drop() inside the container's own Drop().
        var expectError = field.IsOwnedField && site != DropSite.InsideContainerDrop;

        var fieldTypeDecls = $$"""
            class PlainField
            {
                public void Drop()
                {
                }
            }
            affine class AffineField
            {
                public void Drop()
                {
                }
            }
            linear class LinearField
            {
                public void Drop()
                {
                }
            }
            relevant class RelevantField
            {
                public void Drop()
                {
                }
            }
            """;

        var containerBody = site switch
        {
            DropSite.InsideContainerDrop => $$"""
                {{containerDiscipline}} class Container
                {
                    {{field.TypeName}} member;
                    public void Drop()
                    {
                        this.member.Drop();
                    }
                }
                class Program
                {
                    public static void Main()
                    {
                    }
                }
                """,
            DropSite.InsideOtherMethod => $$"""
                {{containerDiscipline}} class Container
                {
                    {{field.TypeName}} member;
                    public void SomeMethod()
                    {
                        this.member.Drop();
                    }
                }
                class Program
                {
                    public static void Main()
                    {
                    }
                }
                """,
            DropSite.ExternalObject => $$"""
                {{containerDiscipline}} class Container
                {
                    {{field.TypeName}} member;
                }
                class Other
                {
                    public static void DoDrop(Container c)
                    {
                        c.member.Drop();
                    }
                }
                class Program
                {
                    public static void Main()
                    {
                    }
                }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(site)),
        };

        var source = fieldTypeDecls + "\n" + containerBody;

        var fieldLabel = field.Keyword.Length == 0 ? "(none)" : field.Keyword;
        var label = $"container={containerDiscipline}, field={fieldLabel}, site={site} "
            + $"=> expectError={expectError}";

        return new FieldDropScenario(
            source, containerDiscipline, fieldLabel, site, expectError, label);
    }

    public static Arbitrary<FieldDropScenario> ArbitraryFieldDropScenario() =>
        GenScenario().ToArbitrary();

    private static int CountFieldOutsideContainer(ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics.Count(d => d.Id.Value == FieldOutsideContainerId);

    // ═══════════════════════════════════════════════════════════════════════
    // Property 14: Field drop restriction
    //
    // A MIST_DROP_FIELD_OUTSIDE_CONTAINER diagnostic is present IFF the dropped
    // field is affine/linear AND the drop site is not a this.Field.Drop() inside
    // the container's own Drop() method. Relevant/unrestricted fields are exempt.
    //
    // **Validates: Requirements 12.1, 12.2, 12.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(FieldDropRestrictionPropertyTests) })]
    public Property FieldDrop_ErrorIffOwnedFieldDroppedOutsideContainerDrop(
        FieldDropScenario scenario)
    {
        var diagnostics = Compile(scenario.Source);
        var count = CountFieldOutsideContainer(diagnostics);

        if(scenario.ExpectError)
        {
            return (count == 1).ToProperty()
                .Label($"Expected exactly one {FieldOutsideContainerId} for {scenario.Label}, got {count}.\n"
                    + $"Source:\n{scenario.Source}");
        }

        return (count == 0).ToProperty()
            .Label($"Expected no {FieldOutsideContainerId} for {scenario.Label}, got {count}.\n"
                + $"Source:\n{scenario.Source}");
    }

    // ─── Example-based sanity checks ──────────────────────────────────────

    [Theory]
    // Affine/linear field dropped inside the container's own Drop() => permitted.
    [InlineData("owned", "affine", "AffineField", DropSite.InsideContainerDrop, false)]
    [InlineData("affine", "affine", "AffineField", DropSite.InsideContainerDrop, false)]
    [InlineData("linear", "linear", "LinearField", DropSite.InsideContainerDrop, false)]
    // Affine/linear field dropped in another method => error.
    [InlineData("owned", "affine", "AffineField", DropSite.InsideOtherMethod, true)]
    [InlineData("linear", "linear", "LinearField", DropSite.InsideOtherMethod, true)]
    // Affine/linear field dropped on another object => error.
    [InlineData("owned", "affine", "AffineField", DropSite.ExternalObject, true)]
    [InlineData("affine", "linear", "LinearField", DropSite.ExternalObject, true)]
    // Relevant / unrestricted field => always permitted, at any site.
    [InlineData("owned", "relevant", "RelevantField", DropSite.InsideOtherMethod, false)]
    [InlineData("owned", "relevant", "RelevantField", DropSite.ExternalObject, false)]
    [InlineData("owned", "", "PlainField", DropSite.ExternalObject, false)]
    public void FieldDrop_ReportsExpectedDiagnostic(
        string containerDiscipline, string fieldKeyword, string fieldTypeName, DropSite site, bool expectError)
    {
        var field = (fieldKeyword, fieldTypeName, IsOwnedField: fieldKeyword is "affine" or "linear");
        var scenario = Build(containerDiscipline, field, site);

        var count = CountFieldOutsideContainer(Compile(scenario.Source));

        if(expectError)
            Assert.Equal(1, count);
        else
            Assert.Equal(0, count);
    }
}