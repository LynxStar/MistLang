using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.InteropFixtures;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend;

public class AmbientTypeProviderV2InteropFixture : IDisposable
{
    public AmbientTypeProviderV2InteropFixture()
        : this([])
    {
    }

    protected AmbientTypeProviderV2InteropFixture(string[] besideTheFixture)
    {
        Model = new ProgramModelClass();
        AmbientProviderTestHelper.RegisterStandardTypes(Model);
        Provider = AmbientProviderTestHelper.ProviderOver(
            new DiagnosticBag(), [typeof(TupleSignatures).Assembly.Location, .. besideTheFixture]);
        Provider.BuildAndRegister(Model);
    }

    internal ProgramModelClass Model { get; }

    internal AmbientTypeProviderV2 Provider { get; }

    /// <summary>
    /// The projection of the CLR type named <paramref name="clrFullName"/>, found the way the type
    /// inspector finds it: the loaded assemblies name the CLR type, and the provider answers its projection.
    /// </summary>
    internal MistType? Projected(string clrFullName)
    {
        return Provider.FindClrType(clrFullName) is { } clrType ? Provider.ProjectionOf(clrType) : null;
    }

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
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if(disposing)
        {
            Provider.Dispose();
        }
    }
}

/// <summary>
/// System.Linq beside the fixture, for the claims that are about Enumerable's own signatures.
/// </summary>
public sealed class AmbientTypeProviderV2LinqInteropFixture : AmbientTypeProviderV2InteropFixture
{
    public AmbientTypeProviderV2LinqInteropFixture()
        : base([typeof(Enumerable).Assembly.Location])
    {
    }
}