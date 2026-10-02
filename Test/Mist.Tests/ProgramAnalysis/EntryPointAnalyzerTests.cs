using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
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

    [Fact]
    [Trait("Tier", "2")]
    public void ARefusalIsReportedAgainstTheCompiledProjectByName()
    {
        var self = hand.Model.AddSelfAssembly("Probe");
        TopLevelFile(self, "Program.mist");
        Main(self, "A");

        var refusal = Assert.Single(hand.Analyze(new EntryPointAnalyzer()));

        Assert.Equal("MIST_EP_0002", refusal.Id.Value);
        Assert.Equal(DiagnosticSeverity.Error, refusal.Severity);
        Assert.Equal("Probe", refusal.CompilationUnitPath);
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
    public void AReferencedProjectIsRefusedAtItsOwnManifest()
    {
        Main(hand.Model.AddSelfAssembly("Probe"), "Program");
        var library = hand.Model.AddReferenceAssembly("Lib", "/lib/mist.toml");
        Main(library, "A");
        Main(library, "B");

        var refusal = Assert.Single(hand.Analyze(new EntryPointAnalyzer()));

        Assert.Equal("MIST_EP_0004", refusal.Id.Value);
        Assert.Equal("/lib/mist.toml", refusal.CompilationUnitPath);
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

        Assert.Equal("MIST_EP_0008", Assert.Single(hand.Analyze(new EntryPointAnalyzer())).Id.Value);
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
        Assert.Equal(("MIST_EP_0004", "/lib/mist.toml", "entry-point-resolution"), (refusal.Id.Value, refusal.CompilationUnitPath, refusal.AnalyzerName));
    }

    private static void TopLevelFile(MistAssembly assembly, string path)
    {
        assembly.CompilationUnits.Add(new CompilationUnit { SourcePath = path, HasTopLevelStatements = true });
    }

    private void Main(MistAssembly assembly, string className)
    {
        assembly.EntryPoints.Add(new Method("Main", hand.Type("Void"), [], new MemberModifiers { IsStatic = true })
        {
            DeclaringType = HandBuiltModel.Class(className),
        });
    }
}