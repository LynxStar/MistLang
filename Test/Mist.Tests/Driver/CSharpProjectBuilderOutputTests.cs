using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

// What the builder reads back after a successful build: the TargetPath MSBuild reports, the managed
// files beside it and the runtimeconfig. The output lives in one private temp directory per test;
// dotnet itself is the run seam, so no process starts.
[Trait("Tier", "2")]
public sealed class CSharpProjectBuilderOutputTests : IDisposable
{
    private const string Manifest = "/work/App/mist.toml";
    private const string Chain = "/work/Outer/mist.toml";

    private readonly string root = Path.Combine(Path.GetTempPath(), $"mist_csproj_output_{Guid.NewGuid():N}");
    private readonly List<DotnetInvocation> invocations = [];

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch(IOException)
        {
        }
    }

    [Fact]
    public void TheOutput_IsTheLastLineMSBuildReports_WithEveryOtherDllBesideIt_InOrdinalOrder()
    {
        var project = Path.Combine(root, "Migrations", "Migrations.csproj");
        var output = Path.Combine(root, "bin", "App.Migrations.dll");
        Touch(output, "Zeta.dll", "alpha.dll", "Beta.dll", "App.Migrations.pdb", "App.Migrations.runtimeconfig.json", "notes.txt");
        Touch(Path.Combine(root, "bin", "runtimes", "Native.dll"));
        var diagnostics = new DiagnosticBag();

        var assembly = CSharpProjectBuilder.Build(
            Reference("App.Migrations", project), "net10.0", Chain, diagnostics, Run(_ => $"Preamble\r\n  {output}  \r\n\r\n  \n"));

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(2, invocations.Count);
        var query = invocations[1];
        Assert.Equal($"msbuild \"{project}\" -getProperty:TargetPath -p:Configuration=Debug -p:TargetFramework=net10.0", query.Arguments);
        Assert.Equal(Path.GetDirectoryName(project), query.WorkingDirectory);
        Assert.Equal(Chain, query.Environment[BuildChain.VariableName]);

        Assert.NotNull(assembly);
        Assert.Equal(("App.Migrations", output, ReferenceKind.CSharpProject), (assembly.Name, assembly.Path, assembly.Kind));
        var bin = Path.Combine(root, "bin");
        Assert.Equal<string>([Path.Combine(bin, "Beta.dll"), Path.Combine(bin, "Zeta.dll"), Path.Combine(bin, "alpha.dll")], assembly.Closure);
        Assert.Equal(Path.Combine(bin, "App.Migrations.runtimeconfig.json"), assembly.RuntimeConfigPath);
    }

    [Fact]
    public void AnOutputAlone_HasNoClosureAndNoRuntimeConfig()
    {
        var output = Path.Combine(root, "bin", "App.Migrations.dll");
        Touch(output);

        var assembly = CSharpProjectBuilder.Build(
            Reference("App.Migrations", Path.Combine(root, "App.Migrations.csproj")), "net10.0", Chain, new DiagnosticBag(), Run(_ => output));

        Assert.NotNull(assembly);
        Assert.Empty(assembly.Closure);
        Assert.Null(assembly.RuntimeConfigPath);
    }

    // {target} is a built output that exists; {missing} names one that does not.
    [Theory]
    [InlineData(1, false, "{target}", "")]
    [InlineData(0, true, "{target}", "")]
    [InlineData(0, false, "\r\n  \r\n", "")]
    [InlineData(0, false, "{missing}\n", "{missing}")]
    public void AQueryThatYieldsNoBuiltOutput_IsAFailedBuild(int exitCode, bool timedOut, string stdout, string reported)
    {
        var project = Path.Combine(root, "App.Migrations", "App.Migrations.csproj");
        var target = Path.Combine(root, "bin", "App.Migrations.dll");
        var missing = Path.Combine(root, "bin", "Missing.dll");
        Touch(target);
        string fill(string text)
        {
            return text.Replace("{target}", target).Replace("{missing}", missing);
        }

        var diagnostics = new DiagnosticBag();

        var assembly = CSharpProjectBuilder.Build(
            Reference("App.Migrations", project), "net10.0", Chain, diagnostics,
            invocation => Record(invocation, invocation.Arguments.StartsWith("build ", StringComparison.Ordinal)
                ? new DotnetOutcome(0, "", "", TimedOut: false)
                : new DotnetOutcome(exitCode, fill(stdout), "", timedOut)));

        Assert.Null(assembly);
        Assert.Equal(2, invocations.Count);
        var diagnostic = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal(DriverDiagnostics.CSharpProjectBuildFailed.Number, diagnostic.Number);
        Assert.Equal(Manifest, diagnostic.Location.Path);
        Assert.Equal(
            $"Building C# project '{project}' for reference 'App.Migrations' failed: MSBuild reported no built output (TargetPath '{fill(reported)}').",
            diagnostic.Message);
    }

    [Fact]
    public void AProjectNamedTwice_IsBuiltOnce_AndIsOneAssembly_KeyedByItsOutputNotItsProjectFile()
    {
        var project = Path.Combine(root, "Migrations", "Migrations.csproj");
        var output = Path.Combine(root, "bin", "App.Migrations.dll");
        Touch(output);
        var reference = Reference("App.Migrations", project);
        var diagnostics = new DiagnosticBag();

        var (assemblies, builtCount) = CSharpProjectBuilder.BuildAll(
            [reference, reference], "net10.0", Chain, diagnostics, Run(_ => output));

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(1, builtCount);
        Assert.Equal(2, invocations.Count);
        var assembly = Assert.Single(assemblies);
        Assert.Equal(("App.Migrations", output), (assembly.Name, assembly.Path));
    }

    [Fact]
    public void AKeyThatIsNotTheOutputName_IsAMismatch_AndTheReferencesAfterItStillBuild()
    {
        var migrations = Path.Combine(root, "Migrations", "Migrations.csproj");
        var other = Path.Combine(root, "Other", "Other.csproj");
        var migrationsOutput = Path.Combine(root, "bin", "App.Migrations.dll");
        var otherOutput = Path.Combine(root, "other-bin", "Other.dll");
        Touch(migrationsOutput);
        Touch(otherOutput);
        var diagnostics = new DiagnosticBag();

        var (assemblies, builtCount) = CSharpProjectBuilder.BuildAll(
            [Reference("Migrations", migrations), Reference("Other", other)], "net10.0", Chain, diagnostics,
            Run(query => query.Arguments.Contains(migrations, StringComparison.Ordinal) ? migrationsOutput : otherOutput));

        Assert.Equal(2, builtCount);
        var assembly = Assert.Single(assemblies);
        Assert.Equal(("Other", otherOutput), (assembly.Name, assembly.Path));
        var diagnostic = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal(DriverDiagnostics.ProjectReferenceNameMismatch.Number, diagnostic.Number);
        Assert.Equal(migrations, diagnostic.Location.Path);
        Assert.Equal(
            $"Reference key 'Migrations' does not match 'App.Migrations', the name the C# project '{migrations}' produces.", diagnostic.Message);
    }

    private static ExternalReference Reference(string key, string project)
    {
        return new ExternalReference(key, project, ReferenceKind.CSharpProject, Manifest);
    }

    private static void Touch(string path, params string[] siblings)
    {
        var directory = Path.GetDirectoryName(path)!;
        _ = Directory.CreateDirectory(directory);
        File.WriteAllText(path, "");
        foreach(var sibling in siblings)
        {
            File.WriteAllText(Path.Combine(directory, sibling), "");
        }
    }

    // Every build succeeds; every TargetPath query succeeds and prints what the callback returns.
    private Func<DotnetInvocation, DotnetOutcome> Run(Func<DotnetInvocation, string> targetPath)
    {
        return invocation => Record(invocation, invocation.Arguments.StartsWith("build ", StringComparison.Ordinal)
            ? new DotnetOutcome(0, "", "", TimedOut: false)
            : new DotnetOutcome(0, targetPath(invocation), "", TimedOut: false));
    }

    private DotnetOutcome Record(DotnetInvocation invocation, DotnetOutcome outcome)
    {
        invocations.Add(invocation);
        return outcome;
    }
}