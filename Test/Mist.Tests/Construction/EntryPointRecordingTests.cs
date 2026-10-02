using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.Construction;

/// <summary>
/// What the binder hands the entry-point decision and deliberately does not decide: every method an
/// entry point can be, the manifest's answer for each project, and a bound body for every file of
/// top-level statements. Whether the combination is a valid program is the analysis phase's.
/// </summary>
[Trait("Tier", "3")]
public sealed class EntryPointRecordingTests : ProgramModelProbe
{
    public EntryPointRecordingTests()
        : base("MistEntryPointRecording_")
    {
    }

    protected override string Fixtures { get; } = "";

    protected override string Preamble { get; } = "";

    [Fact]
    public void EveryMethodAnEntryPointCanBeIsRecordedAndNoneIsRefused()
    {
        var model = Build(
            """
            namespace Probe;

            public class A
            {
                public static void Main() { }

                public static void Main(string[] args) { }
            }

            public class B
            {
                public static int Main() { return 0; }
            }

            public class NotOne
            {
                public void Main() { }

                public static void Main(int first, int second) { }

                public static string Main(string[] args) { return ""; }

                public static void Run() { }
            }
            """,
            out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Equal(
            ["Probe.A()", "Probe.A(args)", "Probe.B()"],
            model.SelfAssembly!.EntryPoints.Select(method =>
                $"{method.DeclaringType!.FullyQualifiedName}({string.Join(", ", method.ParametersList.Select(parameter => parameter.Name))})"));
    }

    [Fact]
    public void EachProjectsManifestReachesItsOwnAssembly()
    {
        var model = Build(
            "namespace Probe; public class A { }",
            "namespace Library; public class B { }",
            out _,
            entryPoint: "Probe.A",
            outputType: ProjectOutputType.Library,
            referencedEntryPoint: "top-level");

        var library = Assert.Single(model.Assemblies, assembly => assembly.Name == "Library");
        Assert.Equal(("Probe.A", true), (model.SelfAssembly!.EntryPointSetting, model.SelfAssembly.IsLibrary));
        Assert.Equal(("top-level", false), (library.EntryPointSetting, library.IsLibrary));
    }

    [Fact]
    public void TwoFilesOfTopLevelStatementsInOneNamespaceAreBothBound()
    {
        var model = Build(
            "namespace Shared; var first = 1;",
            "namespace Shared; var second = 2;",
            out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Equal(
            ["first", "second"],
            model.Assemblies
                .SelectMany(assembly => assembly.CompilationUnits)
                .Select(unit => Assert.IsType<LocalDeclarationStatement>(Assert.Single(unit.TopLevelMethod!.Body!.Statements)).Local.Name));
    }
}