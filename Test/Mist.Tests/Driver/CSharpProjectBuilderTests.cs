using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

[Trait("Tier", "1")]
public sealed class CSharpProjectBuilderTests
{
    private const string Manifest = "/work/App/mist.toml";
    private const string Chain = "/work/Outer/mist.toml";
    private static readonly string Project = Path.Combine("/work", "App.Migrations", "App.Migrations.csproj");

    private readonly List<DotnetInvocation> invocations = [];

    [Fact]
    public void TheBuild_RunsTheSpecifiedCommandBesideTheProject_WithTheChainInItsEnvironment()
    {
        _ = Build(new DotnetOutcome(1, "", "", TimedOut: false));

        var build = Assert.Single(invocations);
        Assert.Equal($"build \"{Project}\" -c Debug -f net10.0 --nologo -v:quiet", build.Arguments);
        Assert.Equal(Path.GetDirectoryName(Project), build.WorkingDirectory);
        Assert.Equal(Chain, build.Environment[BuildChain.VariableName]);
        Assert.Equal(CSharpProjectBuilder.BuildTimeout, build.Timeout);
    }

    [Fact]
    public void AFailedBuild_ReportsItsErrorLines_AndAsksMSBuildNothingMore()
    {
        const string error = "/work/App.Migrations/Program.cs(3,5): error CS1002: ; expected [/work/App.Migrations/App.Migrations.csproj]";
        var diagnostics = Build(new DotnetOutcome(1, $"  {error}\n\nBuild FAILED.\n  {error}\n", "", TimedOut: false));

        var diagnostic = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal(DriverDiagnostics.CSharpProjectBuildFailed.Id, diagnostic.Id);
        Assert.Equal(Manifest, diagnostic.CompilationUnitPath);
        Assert.Equal($"Building C# project '{Project}' for reference 'App.Migrations' failed: {error}", diagnostic.Message);
        _ = Assert.Single(invocations);
    }

    [Fact]
    public void AFailedBuildWithNoErrorLine_ReportsItsExitCode()
    {
        var diagnostics = Build(new DotnetOutcome(3, "Build FAILED.", "", TimedOut: false));

        Assert.EndsWith("failed: dotnet build exited with code 3.", Assert.Single(diagnostics.ToImmutableArray()).Message);
    }

    [Fact]
    public void ABuildThatTimesOut_IsAFailedBuild()
    {
        var diagnostics = Build(new DotnetOutcome(-1, "", "", TimedOut: true));

        var diagnostic = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal(DriverDiagnostics.CSharpProjectBuildFailed.Id, diagnostic.Id);
        Assert.EndsWith("failed: dotnet build timed out after 600 seconds.", diagnostic.Message);
    }

    [Fact]
    public void AnAssemblyReference_IsAnExternalAssemblyAsItIs_AndOnlyOnce()
    {
        var widgets = new ExternalReference("Widgets", "/work/libs/Widgets.dll", ReferenceKind.Assembly, Manifest);
        var diagnostics = new DiagnosticBag();

        var (assemblies, builtCount) = CSharpProjectBuilder.BuildAll([widgets, widgets], "net10.0", Chain, diagnostics, Run(default!));

        var assembly = Assert.Single(assemblies);
        Assert.Equal(("Widgets", "/work/libs/Widgets.dll", ReferenceKind.Assembly), (assembly.Name, assembly.Path, assembly.Kind));
        Assert.Empty(assembly.Closure);
        Assert.Null(assembly.RuntimeConfigPath);
        Assert.Equal(0, builtCount);
        Assert.Empty(invocations);
        Assert.False(diagnostics.HasErrors);
    }

    [Fact]
    public void AFailedBuild_StopsTheRest()
    {
        var first = new ExternalReference("App.Migrations", Project, ReferenceKind.CSharpProject, Manifest);
        var second = first with { Name = "Other", Path = "/work/Other/Other.csproj" };
        var diagnostics = new DiagnosticBag();

        var (assemblies, builtCount) = CSharpProjectBuilder.BuildAll(
            [first, second], "net10.0", Chain, diagnostics, Run(new DotnetOutcome(1, "error MSB1009: Project file does not exist.", "", TimedOut: false)));

        Assert.Empty(assemblies);
        Assert.Equal(0, builtCount);
        _ = Assert.Single(invocations);
        _ = Assert.Single(diagnostics.ToImmutableArray());
    }

    private DiagnosticBag Build(DotnetOutcome outcome)
    {
        var diagnostics = new DiagnosticBag();
        var reference = new ExternalReference("App.Migrations", Project, ReferenceKind.CSharpProject, Manifest);

        Assert.Null(CSharpProjectBuilder.Build(reference, "net10.0", Chain, diagnostics, Run(outcome)));
        return diagnostics;
    }

    private Func<DotnetInvocation, DotnetOutcome> Run(DotnetOutcome outcome)
    {
        return invocation =>
        {
            invocations.Add(invocation);
            return outcome;
        };
    }
}