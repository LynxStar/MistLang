using System.Collections.Immutable;
using System.Reflection;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.InteropFixtures;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend;

public sealed class AmbientTypeProviderV2InteropFixture : IDisposable
{
    private readonly MetadataLoadContext metadataContext;
    private readonly AmbientTypeProviderV2 provider;

    public AmbientTypeProviderV2InteropFixture()
    {
        Model = new ProgramModelClass();
        AmbientProviderTestHelper.RegisterStandardTypes(Model);

        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var assemblyPaths = Directory.GetFiles(runtimeDirectory, "*.dll")
            .Append(typeof(TupleSignatures).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
        metadataContext = new MetadataLoadContext(new PathAssemblyResolver(assemblyPaths));
        var loadedAssemblies = AmbientTypeProviderV2Factory.LoadAssemblies(
            assemblyPaths,
            metadataContext,
            new DiagnosticBag());

        provider = new AmbientTypeProviderV2(loadedAssemblies, null, new DiagnosticBag());
        provider.BuildAndRegister(Model);
    }

    internal ProgramModelClass Model { get; }

    internal ClassType Type(string name)
    {
        return (ClassType)Model.TypesByFQN[$"Mist.Tests.InteropFixtures.{name}"];
    }

    internal ClassType AmbientType(string fullyQualifiedName)
    {
        return (ClassType)Model.TypesByFQN[fullyQualifiedName];
    }

    public void Dispose()
    {
        provider.Dispose();
        metadataContext.Dispose();
    }
}