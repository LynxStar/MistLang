using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "3")]
public sealed class ForeignConformanceRefusalTests : IDisposable
{
    private readonly string outputDir = Path.Combine(Path.GetTempPath(), "mist-foreign-conformance-" + Guid.NewGuid().ToString("N"));
    private readonly ProgramModel.Model.ProgramModel model = new HandBuiltModel().Model;
    private readonly DiagnosticBag diagnostics = new();

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public void AClrTypeDeclaredToConform_IsRefusedAtItsDeclarationAndNothingIsLeftToCompile()
    {
        _ = model.AddSelfAssembly("Probe", isLibrary: true);
        var builder = new ClassType("StringBuilder", "System.Text", new Scope(null), null, null) { Assembly = MistAssembly.Ambient };
        var show = new AbstractType("IShow", "Probe", new Scope(null), null, null);
        Conform(builder, show, offset: 7);

        Assert.Null(Transpile());

        var refusal = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal(BackendDiagnostics.ForeignConformance.Number, refusal.Number);
        Assert.Equal(DiagnosticSeverity.Error, refusal.Severity);
        var at = Assert.IsType<SourceRange>(refusal.Location);
        Assert.Equal(("with.mist", 7), (at.Path, at.Offset));
        Assert.Collection(refusal.Arguments,
            type => Assert.Contains("StringBuilder", Assert.IsType<TypeValue>(type).Spelling),
            conformance => Assert.Contains("IShow", Assert.IsType<TypeValue>(conformance).Spelling));
    }

    [Fact]
    public void MistDeclaredConformers_AreAdmitted_AndAForeignOneAfterThemIsStillRefused()
    {
        var self = model.AddSelfAssembly("Probe", isLibrary: true);
        var library = model.AddReferenceAssembly("Library", "/library/mist.toml", isLibrary: true);
        var show = new AbstractType("IShow", "Probe", new Scope(null), null, null);
        Conform(new ClassType("Widget", "Probe", new Scope(null), null, null) { Assembly = self }, show, offset: 3);
        Conform(new ClassType("Gadget", "Library", new Scope(null), null, null) { Assembly = library }, show, offset: 5);
        Conform(new ClassType("Uri", "System", new Scope(null), null, null) { Assembly = MistAssembly.Ambient }, show, offset: 11);

        Assert.Null(Transpile());

        var refusal = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal(BackendDiagnostics.ForeignConformance.Number, refusal.Number);
        Assert.Equal(11, Assert.IsType<SourceRange>(refusal.Location).Offset);
    }

    private void Conform(ClassType conformer, AbstractType abstractType, int offset)
    {
        model.ConformancesList.Add(
            new ConformanceDeclaration(conformer, abstractType, "Probe", [], [], HandBuiltModel.Node(offset, "with.mist")));
    }

    private Startup? Transpile()
    {
        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create("with.mist"),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "Probe",
            NamespaceRoot: "Probe",
            OutputType: ProjectOutputType.Library);

        return new CSharpEmitterV2(model, options, [], diagnostics, _ => null).Transpile();
    }
}