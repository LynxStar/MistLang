using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

/// <summary>
/// What the rule adds to <see cref="EntryPointSelection.Decide"/>, whose rows are in
/// <see cref="EntryPointSelectionTests"/>: which projects are judged, against whose manifest, and where
/// the finding lands. Over a hand-built model, because none of it depends on what a body binds to.
/// </summary>
public sealed class EntryPointAnalyzerTests
{
    private readonly HandBuiltModel hand = new();

    [Theory]
    [Trait("Tier", "2")]
    [InlineData(false)]
    [InlineData(true)]
    public void TheCompiledExecutableRequiresItsOwnEntryPointRegardlessOfAReferencedMain(bool referencedMain)
    {
        _ = hand.Model.AddSelfAssembly("Probe");
        if(referencedMain)
        {
            Main(hand.Model.AddReferenceAssembly("Library", "/library/mist.toml"), "Program");
        }

        var refusal = Assert.Single(hand.Analyze(new EntryPointAnalyzer()));

        Assert.Equal("MIST_MISSING_ENTRY_POINT", refusal.Number.Value);
        Assert.Equal(DiagnosticSeverity.Error, refusal.Severity);
        Assert.Equal(PipelineStage.Analysis, refusal.Stage);
        Assert.Equal("entry-point-resolution", refusal.AnalyzerName);
        Assert.Equal(new FileOnly("Probe"), refusal.Location);
        Assert.Equal(
            "Project 'Probe' produces an executable but declares no entry point. Add a 'static void Main()' method, or set output type to library.",
            refusal.Message);
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData(false)]
    [InlineData(true)]
    public void AReferencedExecutableWithoutAnEntryPointDoesNotProduceItsOwnArtifact(bool compiledLibrary)
    {
        var self = hand.Model.AddSelfAssembly("Probe", isLibrary: compiledLibrary);
        if(!compiledLibrary)
        {
            Main(self, "Program");
        }

        _ = hand.Model.AddReferenceAssembly("Library", "/library/mist.toml");

        Assert.Empty(hand.Analyze(new EntryPointAnalyzer()));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void LibrariesWithoutAnEntryPointAreAdmittedInEitherProjectRole()
    {
        _ = hand.Model.AddSelfAssembly("Probe", isLibrary: true);
        _ = hand.Model.AddReferenceAssembly("Library", "/library/mist.toml", isLibrary: true);

        Assert.Empty(hand.Analyze(new EntryPointAnalyzer()));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void AnIndependentAnalysisRefusalDoesNotHideTheMissingEntryPoint()
    {
        _ = hand.Model.AddSelfAssembly("Probe");
        var statement = new ReturnStatement(HandBuiltModel.Literal(hand.Type("String"), "wrong"), hand.Type("Int32"));
        var diagnostics = new DiagnosticBag();
        var assignability = new AssignabilityAnalyzer();
        assignability.Analyze(hand.Context(assignability, diagnostics, statement));
        var entryPoint = new EntryPointAnalyzer();

        entryPoint.Analyze(hand.Context(entryPoint, diagnostics));

        Assert.Equal(
            ["MIST_PA_NO_IMPLICIT_CONVERSION", "MIST_MISSING_ENTRY_POINT"],
            diagnostics.ToImmutableArray().Select(diagnostic => diagnostic.Number.Value));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ARefusalIsReportedAgainstTheCompiledProjectByName()
    {
        var self = hand.Model.AddSelfAssembly("Probe");
        TopLevelFile(self, "Program.mist");
        Main(self, "A");

        var refusal = Assert.Single(hand.Analyze(new EntryPointAnalyzer()));

        Assert.Equal("MIST_EP_0002", refusal.Number.Value);
        Assert.Equal(DiagnosticSeverity.Error, refusal.Severity);
        Assert.Equal("Probe", refusal.Location.Path);
        Assert.Equal(
            "Project has both top-level statements (in 'Program.mist') and a static void Main (in 'Probe.A'). "
            + "Add entry_point = \"top-level\" to mist.toml to resolve this conflict.",
            refusal.Message);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void TheManifestsSettingResolvesWhatWouldOtherwiseBeRefused()
    {
        var self = hand.Model.AddSelfAssembly("Probe", entryPointSetting: "top-level");
        TopLevelFile(self, "Program.mist");
        Main(self, "A");
        Main(self, "B");

        Assert.Empty(hand.Analyze(new EntryPointAnalyzer()));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void TheBlockNamespacesRecordedOnTheStatementsFileReachTheDecision()
    {
        var self = hand.Model.AddSelfAssembly("Probe", entryPointSetting: "top-level");
        TopLevelFile(self, "Program.mist", blockNamespaces: ["Foo", "Baz"]);
        Main(self, "A");

        var refusal = Assert.Single(hand.Analyze(new EntryPointAnalyzer()));

        Assert.Equal(("MIST_EP_0006", "Probe"), (refusal.Number.Value, refusal.Location.Path));
        Assert.StartsWith(
            "File 'Program.mist' has both top-level statements and a block namespace declaration: namespace Foo { ... }, namespace Baz { ... }. ",
            refusal.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void AReferencedProjectIsRefusedAtItsOwnManifest()
    {
        Main(hand.Model.AddSelfAssembly("Probe"), "Program");
        var library = hand.Model.AddReferenceAssembly("Lib", "/lib/mist.toml");
        Main(library, "A");
        Main(library, "B");

        var refusal = Assert.Single(hand.Analyze(new EntryPointAnalyzer()));

        Assert.Equal("MIST_EP_0004", refusal.Number.Value);
        Assert.Equal("/lib/mist.toml", refusal.Location.Path);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void AReferencedProjectIsJudgedByItsOwnSettingAndNotTheCompiledProjects()
    {
        Main(hand.Model.AddSelfAssembly("Probe", entryPointSetting: "Probe.Program"), "Program");
        var library = hand.Model.AddReferenceAssembly("Lib", "/lib/mist.toml", entryPointSetting: "Probe.B");
        Main(library, "A");
        Main(library, "B");

        Assert.Empty(hand.Analyze(new EntryPointAnalyzer()));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void AReferencedLibraryIsJudgedAsALibrary()
    {
        Main(hand.Model.AddSelfAssembly("Probe"), "Program");
        var library = hand.Model.AddReferenceAssembly("Lib", "/lib/mist.toml", isLibrary: true);
        TopLevelFile(library, "Script.mist");

        Assert.Equal("MIST_EP_0008", Assert.Single(hand.Analyze(new EntryPointAnalyzer())).Number.Value);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void TheStandardLibraryHasNoManifestAndIsNotJudged()
    {
        var standard = hand.Model.AddStandardAssembly();
        Main(standard, "A");
        Main(standard, "B");
        TopLevelFile(standard, "Script.mist");

        Assert.Empty(hand.Analyze(new EntryPointAnalyzer()));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void APassRestrictedToAClonedTypeDoesNotReportTheProjectAgain()
    {
        var self = hand.Model.AddSelfAssembly("Probe");
        Main(self, "A");
        Main(self, "B");
        var diagnostics = new DiagnosticBag();
        var analyzer = new EntryPointAnalyzer();

        analyzer.Analyze(new AnalysisContext(
            hand.Model,
            new AnalysisScope([], [], [], [], RestrictToScope: true),
            [],
            new Dictionary<object, SourceRoot>(),
            new Dictionary<object, AssignmentExpression>(),
            new HashSet<string>(),
            diagnostics,
            analyzer.Id,
            CloneConversionRefusals.None));

        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Fact]
    [Trait("Tier", "3")]
    public void TheRunnerJudgesEveryProjectOnceThroughThisRule()
    {
        Main(hand.Model.AddSelfAssembly("Probe"), "Program");
        var library = hand.Model.AddReferenceAssembly("Lib", "/lib/mist.toml");
        Main(library, "A");
        Main(library, "B");
        var diagnostics = new DiagnosticBag();

        ProgramAnalysisRunner.Run(hand.Model, diagnostics);

        var refusal = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal(("MIST_EP_0004", "/lib/mist.toml", "entry-point-resolution"), (refusal.Number.Value, refusal.Location.Path, refusal.AnalyzerName));
    }

    private static void TopLevelFile(MistAssembly assembly, string path, ImmutableArray<string> blockNamespaces = default)
    {
        assembly.CompilationUnits.Add(new CompilationUnit
        {
            SourcePath = path,
            HasTopLevelStatements = true,
            BlockNamespaces = blockNamespaces.IsDefault ? [] : blockNamespaces,
        });
    }

    private void Main(MistAssembly assembly, string className)
    {
        assembly.EntryPoints.Add(new Method("Main", hand.Type("Void"), [], new MemberModifiers { IsStatic = true })
        {
            DeclaringType = HandBuiltModel.Class(className),
        });
    }
}