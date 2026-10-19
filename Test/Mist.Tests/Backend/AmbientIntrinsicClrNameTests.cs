using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using ProgramModelType = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend;

/// <summary>
/// The CLR names a qualified lookup answers with a Standard intrinsic are the provider's own
/// substitution, so the two cannot disagree; an entry generic on either side has no such name.
/// </summary>
[Trait("Tier", "3")]
public sealed class AmbientIntrinsicClrNameTests
{
    [Theory]
    [InlineData("System", "Int32", "Standard.Int32")]
    [InlineData("System", "String", "Standard.String")]
    [InlineData("System.Threading.Tasks", "Task", "Standard.Task")]
    public void ANonGenericIntrinsicIsNamedByItsClrName(string clrNamespace, string clrName, string standardName)
    {
        var model = Projected();

        var found = model.LookupQualified(clrNamespace, clrName);

        Assert.Same(model.TypesByFQN[standardName], Assert.IsType<TypeResult>(found).Type);
    }

    [Theory]
    [InlineData("System", "Action")]
    [InlineData("System", "Array")]
    public void AnIntrinsicGenericInMistIsNotNamedByItsClrName(string clrNamespace, string clrName)
    {
        _ = Assert.IsType<NotFoundResult>(Projected().LookupQualified(clrNamespace, clrName));
    }

    private static ProgramModelType Projected()
    {
        var model = new ProgramModelType();
        foreach(var (namespaceName, name) in WellKnownTypeMap.Entries.Values.Distinct())
        {
            _ = model.RegisterType(new ClassType(name, namespaceName, null, null, null));
        }

        using var provider = new AmbientTypeProviderV2(ImmutableArray<LoadedAssemblyInfo>.Empty, null, new DiagnosticBag());
        provider.BuildAndRegister(model);
        return model;
    }
}