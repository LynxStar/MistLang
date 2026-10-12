using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class NativeDefaultCallTests
{
    private readonly HandBuiltModel built = new();

    [Fact]
    public void ACallOnAConformerToAProjectedInterfacesMember_IsWrittenThroughTheInterface()
    {
        var (greeter, greet) = Greeter(ambient: true);
        var conformer = Conformer(greeter);

        Assert.Same(greeter, NativeDefaultCall.ReceiverType(conformer, greet));
        Assert.Same(greeter, NativeDefaultCall.ReceiverType(built.Interner.GetOrCreateUnionType([conformer, built.Type("Void")]), greet));
    }

    [Fact]
    public void AConformerReachingTheInterfaceThroughAnother_IsWrittenThroughTheOneDeclaringTheMember()
    {
        var (greeter, greet) = Greeter(ambient: true);
        var polite = new AbstractType("IPolite", "Probe", new Scope(null), null, null);
        polite.BaseAbstractTypes.Add(greeter);

        Assert.Same(greeter, NativeDefaultCall.ReceiverType(Conformer(polite), greet));
    }

    [Fact]
    public void AGenericInterface_IsWrittenClosedAsTheConformerReachesIt()
    {
        var converter = new AbstractType("IConverter<>", "Native", new Scope(null), null, null) { Assembly = MistAssembly.Ambient };
        var element = new TypeParameter("T", 0, converter.Scope, converter);
        converter.TypeParametersList.Add(element);
        var convert = new Method("Convert", element, [], default) { DeclaringType = converter };
        var closed = built.Interner.GetOrCreateConstructedType(converter, [built.Type("Int32")]);

        var receiver = NativeDefaultCall.ReceiverType(Conformer(closed), convert);

        Assert.Equal(closed.FullyQualifiedName, receiver?.FullyQualifiedName);
    }

    [Fact]
    public void AnythingElse_IsWrittenAsItWas()
    {
        var (greeter, greet) = Greeter(ambient: true);
        var (source, sourceGreet) = Greeter(ambient: false);
        var conformer = Conformer(greeter);
        var shared = new Method("Bracket", built.Type("String"), [], new MemberModifiers { IsStatic = true }) { DeclaringType = greeter };

        Assert.Null(NativeDefaultCall.ReceiverType(greeter, greet));
        Assert.Null(NativeDefaultCall.ReceiverType(Conformer(source), sourceGreet));
        Assert.Null(NativeDefaultCall.ReceiverType(conformer, shared));
        Assert.Null(NativeDefaultCall.ReceiverType(conformer, new Method("Name", built.Type("String"), [], default) { DeclaringType = conformer }));
    }

    private (AbstractType Greeter, Method Greet) Greeter(bool ambient)
    {
        var greeter = new AbstractType("IGreeter", "Native", new Scope(null), null, null)
        {
            Assembly = ambient ? MistAssembly.Ambient : MistAssembly.Synthetic,
        };
        var greet = new Method("Greet", built.Type("String"), [], default) { DeclaringType = greeter, HasNativeBody = true };
        greeter.MethodsList.Add(greet);

        return (greeter, greet);
    }

    private static ClassType Conformer(MistType reached)
    {
        var conformer = new ClassType("Plain", "Probe", new Scope(null), null, null);
        conformer.BaseAbstractTypes.Add(reached);

        return conformer;
    }
}