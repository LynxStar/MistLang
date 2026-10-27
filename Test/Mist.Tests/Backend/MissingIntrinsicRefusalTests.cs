using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

using CompilationUnit = Mist.ProgramModel.Model.CompilationUnit;

namespace Mist.Tests.Backend;

public sealed class MissingIntrinsicRefusalTests : IDisposable
{
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
    [Trait("Tier", "3")]
    public void AnUnsuppliedIntrinsicIsRefusedByDeclaringTypeAndMemberWithNoArtifact()
    {
        var (model, _) = Model();

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
        Assert.Contains("Standard.Math.Abs", Assert.IsType<SymbolValue>(Assert.Single(refusal.Arguments)).Identity);
        Assert.EndsWith("Probe.mist", refusal.Location.Path);

        var emitted = Directory.EnumerateFiles(outputDir, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText).ToList();
        Assert.NotEmpty(emitted);
        Assert.DoesNotContain(emitted, text => text.Contains("Math.Abs(-1)"));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void AMemberTheMetadataSuppliesIsEmittedAsWritten()
    {
        var (model, run) = Model();
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        using(IntrinsicSupply.Enter(model, Type.GetType))
        {
            BodyEmitter.EmitBlock(run.Body!, writer, diagnostics);
        }

        Assert.DoesNotContain(diagnostics.ToImmutableArray(), d => d.Number == BackendDiagnostics.MissingIntrinsicEmissionTemplate.Number);
        Assert.Contains("System.Math.Abs(-1)", writer.ToString());
    }

    [Fact]
    [Trait("Tier", "3")]
    public void AnInternalEmissionFallbackPreventsArtifactCreation()
    {
        var (model, run) = Model();
        run.Body!.Statements = [new LocalDeclarationStatement(new LocalVariable("invalid", ErrorType.Instance),
            HandBuiltModel.Literal(ErrorType.Instance, 0L))];
        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ["Probe.mist"],
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "Probe",
            NamespaceRoot: "Probe",
            OutputType: ProjectOutputType.Library);

        var result = new CSharpEmitterV2(model, options, [], new DiagnosticBag(), _ => null).Emit();

        Assert.False(result.Succeeded);
        Assert.Null(result.PrimaryOutputArtifactPath);
        Assert.False(Directory.Exists(options.BinDirectory));
        Assert.Equal(BackendDiagnostics.EmissionFallback.Number, Assert.Single(result.Diagnostics).Number);
    }

    private static (ProgramModel.Model.ProgramModel Model, Method Run) Model()
    {
        var built = new HandBuiltModel();
        var model = built.Model;
        var standard = model.AddStandardAssembly();
        var intrinsic = new ClassType("Math", "Standard", null, null, null)
        {
            Assembly = standard,
            Modifiers = new MemberModifiers { IsIntrinsic = true, Accessibility = Accessibility.Public },
        };
        var number = built.Type("Int32");
        var abs = new Method("Abs", number, [new Parameter("value", number)],
            new MemberModifiers { IsIntrinsic = true, IsStatic = true, Accessibility = Accessibility.Public })
        { DeclaringType = intrinsic };
        intrinsic.MethodsList.Add(abs);
        var standardUnit = new CompilationUnit { SourcePath = "Math.mist" };
        standardUnit.Types.Add(intrinsic);
        standard.CompilationUnits.Add(standardUnit);
        _ = model.RegisterType(intrinsic);
        var owner = HandBuiltModel.Class("Probe");
        owner.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        var call = new InvocationExpression(HandBuiltModel.Node(path: "Probe.mist"),
            new MemberAccessExpression(HandBuiltModel.Node(), new TypeReferenceExpression(HandBuiltModel.Node(), intrinsic), "Abs", number),
            [HandBuiltModel.Literal(number, -1L)], number, abs, [number]);
        var run = new Method("Run", built.Type("Void"), [], new MemberModifiers { IsStatic = true, Accessibility = Accessibility.Public })
        {
            DeclaringType = owner,
            Body = HandBuiltModel.Block(new LocalDeclarationStatement(new LocalVariable("magnitude", number), call)),
        };
        owner.MethodsList.Add(run);
        var self = model.AddSelfAssembly("Probe", isLibrary: true);
        owner.Assembly = self;
        var unit = new CompilationUnit { SourcePath = "Probe.mist" };
        unit.Types.Add(owner);
        self.CompilationUnits.Add(unit);
        _ = model.RegisterType(owner);
        return (model, run);
    }
}