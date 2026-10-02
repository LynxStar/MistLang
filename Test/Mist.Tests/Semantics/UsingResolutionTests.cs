using Mist.Common.Diagnostics;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

/// <summary>
/// When the builder judges usings, a file's own and a project's implicit ones: after every unit and the
/// ambient projection have registered their namespaces, and before a name is resolved through one.
/// </summary>
[Trait("Tier", "3")]
public sealed class UsingResolutionTests : ProgramModelProbe
{
    private const string UndefinedGlobal = "MIST_GLOBAL_USING_UNDEFINED_NAMESPACE";

    private const string Library = """
        namespace Library.Extensions;

        extension(int self)
        {
            public int Sides()
            {
                return 4;
            }
        }
        """;

    public UsingResolutionTests()
        : base("MistUsing_")
    {
    }

    protected override string Fixtures { get; } = "";

    protected override string Preamble { get; } = "";

    private static string Program(string statement, string usings = "")
    {
        return $$"""
            {{usings}}
            namespace Probe;

            public class Outer
            {
                public class Inner
                {
                }
            }

            public class Probe
            {
                public static void Run()
                {
                    {{statement}}
                }
            }
            """;
    }

    [Fact]
    public void ANamespaceALaterUnitOrTheAmbientProjectionDeclaresIsImported()
    {
        _ = Build(
            Program("Int32 square = 0; Int32 count = square.Sides();"),
            Library,
            out var diagnostics,
            globalUsings: ["Standard", "Library.Extensions"]);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void AnUndefinedGlobalUsingWarnsAndTheModelIsBuiltWithoutIt()
    {
        var model = Build(
            Program("int count = 1;"),
            Library,
            out var diagnostics,
            globalUsings: ["No.Such.Namespace"],
            referencedGlobalUsings: ["Library.Nowhere"]);

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, d => Assert.Equal((UndefinedGlobal, DiagnosticSeverity.Warning), (d.Id.Value, d.Severity)));
        Assert.All(model.Assemblies, assembly => Assert.Empty(assembly.GlobalUsings));
    }

    [Fact]
    public void ATypeIsNotANamespaceSoItsNestedTypesAreNotImported()
    {
        _ = Build(
            Program("Inner inner = new Inner();"),
            referenced: null,
            out var diagnostics,
            globalUsings: ["Probe.Outer"]);

        _ = Assert.Single(diagnostics, d => d.Id.Value == UndefinedGlobal);
        Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void AFilesUsingOfANamespaceTheProgramHasIsAdmitted()
    {
        const string usings = """
            using Standard;
            using Standard;
            using Probe;
            using Library;
            using Library.Extensions;
            """;

        _ = Build(Program("Int32 square = 0; Int32 count = square.Sides();", usings), Library, out var diagnostics);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void AFilesUsingOfAnUndefinedNamespaceIsRefusedAtTheDirective()
    {
        const string usings = """
            using Standard;
            using No.Such.Namespace;
            """;

        _ = Build(Program("Int32 count = 1;", usings), Library, out var diagnostics);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(("MIST_UNDEFINED_NAMESPACE", DiagnosticSeverity.Error), (diagnostic.Id.Value, diagnostic.Severity));
        Assert.Equal("The namespace 'No.Such.Namespace' could not be found.", diagnostic.Message);
        Assert.Equal(2, diagnostic.Location.Start.Line);
    }
}