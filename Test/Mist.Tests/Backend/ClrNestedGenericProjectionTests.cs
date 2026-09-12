// Tier 3 (Component): how a CLR nested generic type projects. The CLR repeats a declaring type's
// parameters on the nested type, so List`1+Enumerator carries a T that is List's; projected as an
// argument of the nested type it spelled `List<int>.Enumerator<int>`, and before that left Current
// typed as an unsubstituted T. Asserted on the projected signature and its substitution, over the
// runtime's real metadata. No Roslyn compile and no process spawning.

using System.Collections.Immutable;
using System.Reflection;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend;

public sealed class ClrNestedGenericProjectionTests : IDisposable
{
    private readonly ProgramModelClass programModel;
    private readonly AmbientTypeProviderV2 provider;
    private readonly MetadataLoadContext metadataContext;
    private readonly ClassType list;
    private readonly Method getEnumerator;

    public ClrNestedGenericProjectionTests()
    {
        programModel = new ProgramModelClass();
        AmbientProviderTestHelper.RegisterStandardTypes(programModel);

        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var assemblyPaths = Directory.GetFiles(runtimeDir, "*.dll").ToImmutableArray();

        metadataContext = new MetadataLoadContext(new PathAssemblyResolver(assemblyPaths));

        var loaded = AmbientTypeProviderV2Factory.LoadAssemblies(assemblyPaths, metadataContext, new DiagnosticBag());

        provider = new AmbientTypeProviderV2(loaded, null, new DiagnosticBag());
        provider.BuildAndRegister(programModel);
        provider.BridgeWellKnownMembers(programModel);

        list = Assert.IsType<ClassType>(programModel.TypesByFQN["System.Collections.Generic.List<>"]);
        getEnumerator = list.MethodsList.Single(method => method.Name == "GetEnumerator" && method.ParametersList.Count == 0);
    }

    public void Dispose()
    {
        provider.Dispose();
        metadataContext.Dispose();
    }

    [Fact]
    public void TheNestedDefinitionHasNoParametersOfItsOwn()
    {
        var enumerator = Assert.Single(list.NestedTypes, nested => nested.Name == "Enumerator");

        Assert.Empty(Assert.IsAssignableFrom<MemberType>(enumerator).TypeParametersList);
    }

    [Fact]
    public void AReturnedNestedTypeCarriesTheEnclosingInstantiationNotAnArgument()
    {
        var returned = Assert.IsType<ConstructedType>(getEnumerator.ReturnType);

        Assert.Empty(returned.TypeArguments);
        Assert.Same(list, Assert.IsType<ConstructedType>(returned.Enclosing).Definition);
    }

    [Fact]
    public void TheReceiverSubstitutesThroughTheEnclosingAndSpellsAsCSharpDoes()
    {
        var int32 = programModel.TypesByFQN["Standard.Int32"];
        var listOfInt = programModel.TypeInterner.GetOrCreateConstructedType(list, [int32]);
        var substitution = TypeSubstitution.ForReceiver(listOfInt);

        var enumerator = Assert.IsType<ConstructedType>(substitution.Apply(getEnumerator.ReturnType, programModel.TypeInterner));
        var current = Assert.IsAssignableFrom<MemberType>(enumerator.Definition).PropertiesList.Single(property => property.Name == "Current");

        Assert.Equal("List<int>.Enumerator", TypeDisplay.Mist(enumerator));
        Assert.Equal("System.Collections.Generic.List<int>.Enumerator", TypeNameEmitter.Emit(enumerator));
        Assert.Same(int32, TypeSubstitution.ForReceiver(enumerator).Apply(current.Type, programModel.TypeInterner));
    }
}