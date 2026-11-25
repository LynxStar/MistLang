using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

using CompilationUnit = Mist.ProgramModel.Model.CompilationUnit;

namespace Mist.Tests.Backend;

[Trait("Tier", "3")]
public sealed class CompilationUnitLayoutTests : IDisposable
{
    private readonly string outputDir = Path.Combine(Path.GetTempPath(), "mist-unit-layout-" + Guid.NewGuid().ToString("N"));
    private readonly ProgramModel.Model.ProgramModel model = new HandBuiltModel().Model;
    private readonly MistAssembly self;

    public CompilationUnitLayoutTests()
    {
        self = model.AddSelfAssembly("Probe", isLibrary: true);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public void AFileOfTwoNamespaces_WritesEachAsABlockHoldingOnlyItsOwnDeclarations()
    {
        var unit = Unit("Shapes.mist");
        unit.Types.Add(Class("Circle", "Alpha"));
        unit.Types.Add(Class("Square", "Beta"));
        unit.Types.Add(Class("Triangle", "Alpha"));
        unit.Extensions.Add(new ExtensionBlock("self", "Beta", new Scope(null), null)
        {
            ResolvedReceiverType = unit.Types[1],
            CarrierName = "SquareExtensions",
        });

        Transpile();

        var outline = File.ReadAllLines(Path.Combine(outputDir, "Probe", "Shapes.cs"))
            .Where(line => line.StartsWith("namespace", StringComparison.Ordinal)
                || line is "{" or "}"
                || line.StartsWith("    public ", StringComparison.Ordinal));
        Assert.Equal(
            [
                "namespace Alpha", "{", "    public class Circle", "    public class Triangle", "}",
                "namespace Beta", "{", "    public class Square", "    public static class SquareExtensions", "}",
            ],
            outline);
    }

    [Fact]
    public void SameNamedFilesInDifferentFolders_AreEmittedApartUnderTheirFolders()
    {
        Unit("Model.mist").Types.Add(Class("Root", "Probe"));
        Unit("Model.mist", Path.Combine("Data", "Stores")).Types.Add(Class("Nested", "Probe.Data"));

        Transpile();

        Assert.Contains("public class Root", File.ReadAllText(Path.Combine(outputDir, "Probe", "Model.cs")));
        Assert.Contains("public class Nested", File.ReadAllText(Path.Combine(outputDir, "Probe", "Data", "Stores", "Model.cs")));
    }

    private CompilationUnit Unit(string sourcePath, string relativePath = "")
    {
        var unit = new CompilationUnit { SourcePath = sourcePath, RelativePath = relativePath };
        self.CompilationUnits.Add(unit);
        return unit;
    }

    private ClassType Class(string name, string namespaceName)
    {
        var type = new ClassType(name, namespaceName, new Scope(null), null, null)
        {
            Assembly = self,
            Modifiers = new MemberModifiers { Accessibility = Accessibility.Public },
        };
        _ = model.RegisterType(type);
        return type;
    }

    private void Transpile()
    {
        var diagnostics = new DiagnosticBag();
        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create("Shapes.mist"),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "Probe",
            NamespaceRoot: "Probe",
            OutputType: ProjectOutputType.Library);

        Assert.NotNull(new CSharpEmitterV2(model, options, [], diagnostics, _ => null).Transpile());
        Assert.Empty(diagnostics.ToImmutableArray());
    }
}