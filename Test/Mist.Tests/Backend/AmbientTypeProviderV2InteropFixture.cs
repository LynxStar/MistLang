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
    private readonly ProgramModelClass programModel;

    public AmbientTypeProviderV2InteropFixture()
    {
        programModel = new ProgramModelClass();
        AmbientProviderTestHelper.RegisterStandardTypes(programModel);

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
        provider.BuildAndRegister(programModel);
    }

    internal ClassType Type(string name)
    {
        return (ClassType)programModel.TypesByFQN[$"Mist.Tests.InteropFixtures.{name}"];
    }

    public void Dispose()
    {
        provider.Dispose();
        metadataContext.Dispose();
    }
}