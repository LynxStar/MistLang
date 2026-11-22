using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;

namespace Mist.Tests.Backend;

[Trait("Tier", "4")]
public sealed class CSharpCompilationDriverOutputTests : IDisposable
{
    private const string DataManifest = "/repo/App.Data/mist.toml";

    private readonly string root = Path.Combine(Path.GetTempPath(), "mist-compilation-driver-" + Guid.NewGuid().ToString("N"));

    public CSharpCompilationDriverOutputTests()
    {
        _ = Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        if(Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AnExternalAssemblyReferencingTheRootStopsBeforeRoslynWithTheCycleAndTheEmittedCount()
    {
        var project = Source("out", "Widget.cs", "public class Widget { }");
        var bin = Path.Combine(root, "bin");
        var consumer = Consumer("App");
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);

        var result = CSharpCompilationDriver.Compile(project, bin, "App", diagnostics, emittedFileCount: 7,
            outputType: ProjectOutputType.Library, externalAssemblies: [consumer]);

        Assert.False(result.Succeeded);
        Assert.Null(result.PrimaryArtifactPath);
        Assert.Equal(7, result.EmittedFileCount);
        Assert.Equal(DriverDiagnostics.ReferenceCycle.Number, Assert.Single(result.Diagnostics).Number);
        Assert.False(File.Exists(Path.Combine(bin, "App.dll")));
    }

    [Fact]
    public void AProjectDirectoryWithNoCSharpSourceIsOneBuildError()
    {
        var project = Source("out", "Program.mist", "func Main() { }");
        var bin = Path.Combine(root, "bin");
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);

        var result = CSharpCompilationDriver.Compile(project, bin, "App", diagnostics, emittedFileCount: 3);

        Assert.False(result.Succeeded);
        Assert.Null(result.PrimaryArtifactPath);
        Assert.Equal(3, result.EmittedFileCount);
        Assert.Equal(BackendDiagnostics.BuildError.Number, Assert.Single(result.Diagnostics).Number);
        Assert.Empty(Directory.GetFiles(bin));
    }

    [Fact]
    public void AFailedEmitLeavesNothingInTheOutputDirectory()
    {
        var project = Source("out", "Widget.cs", "public class Widget { public void Run() { Missing(); } }");
        var bin = Path.Combine(root, "bin");
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);

        var result = CSharpCompilationDriver.Compile(project, bin, "App", diagnostics, emittedFileCount: 2,
            outputType: ProjectOutputType.Library);

        Assert.False(result.Succeeded);
        Assert.Null(result.PrimaryArtifactPath);
        Assert.Equal(2, result.EmittedFileCount);
        Assert.Empty(Directory.GetFiles(bin));
    }

    [Fact]
    public void ALibraryIsWrittenAsADllWithNoEntryPointIntoAnOutputDirectoryItCreates()
    {
        var project = Source("out", "Widget.cs", "public class Widget { }");
        var bin = Path.Combine(root, "build", "bin");
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);

        var result = CSharpCompilationDriver.Compile(project, bin, "App", diagnostics, outputType: ProjectOutputType.Library);

        Assert.True(result.Succeeded);
        Assert.Equal(Path.Combine(bin, "App.dll"), result.PrimaryArtifactPath);
        Assert.True(File.Exists(Path.Combine(bin, "App.runtimeconfig.json")));
        using var image = new PEReader(File.OpenRead(result.PrimaryArtifactPath!));
        Assert.True(image.PEHeaders.IsDll);
        Assert.Equal(0, image.PEHeaders.CorHeader!.EntryPointTokenOrRelativeVirtualAddress);
    }

    [Fact]
    public void TheFacadeForwardsTheEmittedNamesOfPublicTypesInFilesWithAnOrigin()
    {
        var stage = Path.Combine(root, "stage");
        _ = Directory.CreateDirectory(stage);
        var project = FacadeProject();
        Dictionary<string, DeclarationOrigin> origins = new()
        {
            [Path.Combine(project, "Support", "..", "Data", "Store.cs")] = new(DeclarationOriginKind.ComposedProject, DataManifest),
            [Path.Combine(project, "Support", "__Void.g.cs")] = DeclarationOrigin.SupportFile,
        };

        var forwarded = CompileWithFacade(Path.Combine(stage, "..", "out"), origins);

        Assert.Equal(
            [
                ("", "__Void"),
                ("App.Data.Stores", "Pair`1"),
                ("App.Data.Stores", "StoreContext"),
                ("App.Data.Stores", "StoreContext/Options"),
                ("App.Data.Stores", "StoreContext/Options/Kind"),
            ],
            forwarded.OrderBy(type => type.Namespace, StringComparer.Ordinal).ThenBy(type => type.Name, StringComparer.Ordinal));
    }

    [Fact]
    public void WithNoDeclarationOriginsTheFacadeForwardsNothing()
    {
        var forwarded = CompileWithFacade(FacadeProject(), declarationOrigins: null);

        Assert.Empty(forwarded);
    }

    private string FacadeProject()
    {
        var project = Source("out", Path.Combine("Data", "Store.cs"), """
            namespace App.Data.Stores
            {
                public class StoreContext { public class Options { private class Kind { } } }
                internal class Cache { }
                public class Pair<T> { }
            }
            """);
        _ = Source("out", Path.Combine("Support", "__Void.g.cs"), "public class __Void { }");
        _ = Source("out", Path.Combine("Loose", "Loose.cs"), "namespace App.Data.Stores { public class Loose { } }");
        return project;
    }

    private List<(string Namespace, string Name)> CompileWithFacade(
        string project, IReadOnlyDictionary<string, DeclarationOrigin>? declarationOrigins)
    {
        var bin = Path.Combine(root, "bin");
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);

        var result = CSharpCompilationDriver.Compile(project, bin, "App", diagnostics,
            outputType: ProjectOutputType.Library,
            externalAssemblies: [Consumer("App.Data")],
            composedProjects: [new ComposedProject("App.Data", DataManifest)],
            declarationOrigins: declarationOrigins);

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        using var image = new PEReader(File.OpenRead(Path.Combine(bin, "App.Data.dll")));
        var reader = image.GetMetadataReader();
        return [.. reader.ExportedTypes.Select(handle => QualifiedName(reader, reader.GetExportedType(handle)))];
    }

    private static (string Namespace, string Name) QualifiedName(MetadataReader reader, ExportedType type)
    {
        if(type.Implementation.Kind != HandleKind.ExportedType)
        {
            return (reader.GetString(type.Namespace), reader.GetString(type.Name));
        }

        var (namespaceName, encloser) = QualifiedName(reader, reader.GetExportedType((ExportedTypeHandle)type.Implementation));
        return (namespaceName, $"{encloser}/{reader.GetString(type.Name)}");
    }

    private ExternalAssembly Consumer(string referencedName)
    {
        var path = Path.Combine(root, "external", "Consumer.dll");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, FacadeEmitter.Emit("Consumer", referencedName, new Version(0, 0, 0, 0), []).AsSpan());
        return new ExternalAssembly("Consumer", path, ImmutableArray<string>.Empty, null, ReferenceKind.Assembly);
    }

    private string Source(string directory, string relativePath, string text)
    {
        var path = Path.Combine(root, directory, relativePath);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return Path.Combine(root, directory);
    }
}