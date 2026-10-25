// Tier 3 (Component): what the emitter does with an intrinsic the backend cannot supply, which is the
// standard-library spec's Req 5.9 and 5.10. Built over the real Standard sources with a metadata oracle
// that finds nothing, so a member the CLR does supply stands in for one it does not and the refusal is
// observed on its own. No Roslyn compile and no process spawning: the emitter stops before either.

using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "3")]
public sealed class MissingIntrinsicRefusalTests : IDisposable
{
    private const string Statement = "var magnitude = Math.Abs(-1);";

    private readonly string outputDir =
        Path.Combine(Path.GetTempPath(), "mist-missing-intrinsic-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public void AnUnsuppliedIntrinsicIsRefusedByDeclaringTypeAndMemberWithNoArtifact()
    {
        var model = StandardModelProbe.Build(Statement, out var built);
        Assert.Empty(built.Where(d => d.Severity == DiagnosticSeverity.Error));

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create("Probe.mist"),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "Probe",
            NamespaceRoot: "Probe",
            OutputType: ProjectOutputType.Library);

        var result = new CSharpEmitterV2(model, options, ImmutableArray<string>.Empty, new DiagnosticBag(), _ => null).Emit();

        Assert.False(result.Succeeded);
        Assert.Null(result.PrimaryOutputArtifactPath);
        Assert.False(Directory.Exists(options.BinDirectory));

        var refusal = Assert.Single(result.Diagnostics, d => d.Number == BackendDiagnostics.MissingIntrinsicEmissionTemplate.Number);
        Assert.Equal(DiagnosticSeverity.Error, refusal.Severity);
        Assert.Contains("Standard.Math.Abs(int)", refusal.Message);
        Assert.EndsWith("Probe.mist", refusal.CompilationUnitPath);

        var emitted = Directory.EnumerateFiles(outputDir, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText).ToList();
        Assert.NotEmpty(emitted);
        Assert.DoesNotContain(emitted, text => text.Contains("Math.Abs(-1)"));
    }

    [Fact]
    public void AMemberTheMetadataSuppliesIsEmittedAsWritten()
    {
        var model = StandardModelProbe.Build(Statement, out _);
        var run = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]).MethodsList.Single(m => m.Name == "Run");
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        using(IntrinsicSupply.Enter(model, Type.GetType))
        {
            BodyEmitter.EmitBlock(run.Body!, writer, diagnostics);
        }

        Assert.DoesNotContain(diagnostics.ToImmutableArray(), d => d.Number == BackendDiagnostics.MissingIntrinsicEmissionTemplate.Number);
        Assert.Contains("System.Math.Abs(-1)", writer.ToString());
    }
}