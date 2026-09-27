using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.InteropFixtures.ConstraintProjection;
using ProgramModelType = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend;

[Trait("Tier", "3")]
public sealed class AmbientConstraintRegistrationTests
{
    [Fact]
    public void RegistrationDoesNotPublishAnUnavailableTypeOrItsDescendant()
    {
        var model = new ProgramModelType();
        foreach(var (namespaceName, name) in WellKnownTypeMap.Entries.Values.Distinct())
        {
            _ = model.RegisterType(new ClassType(name, namespaceName, null, null, null));
        }

        var int32 = model.TypesByFQN["Standard.Int32"];
        var diagnostics = new DiagnosticBag();
        var assembly = new LoadedAssemblyInfo("<fixture>", typeof(Methods).Assembly);
        using var provider = new AmbientTypeProviderV2([assembly], null, diagnostics);

        provider.BuildAndRegister(model);

        var fixtureNamespace = typeof(AliasConstrained<>).Namespace!;
        Assert.False(model.TypesByFQN.ContainsKey($"{fixtureNamespace}.AliasConstrained<>"));
        Assert.False(model.TypesByFQN.ContainsKey($"{fixtureNamespace}.AliasConstrained<>.Child"));
        var methods = Assert.IsType<ClassType>(model.TypesByFQN[$"{fixtureNamespace}.Methods"]);
        Assert.Same(int32, methods.MethodsList.Single(method => method.Name == nameof(Methods.Valid)).ReturnType);
        Assert.Same(int32, model.TypesByFQN["Standard.Int32"]);
        Assert.Empty(diagnostics.ToImmutableArray());
    }
}