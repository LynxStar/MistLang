using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class ToStringDeclarationEmissionTests
{
    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    [InlineData("native-base")]
    public void ASourceConcreteZeroArgumentMethodAcknowledgesClrHiding(string ownerKind)
    {
        var model = new HandBuiltModel();
        ConcreteType owner = ownerKind == "struct" ? HandBuiltModel.Struct("Value") : HandBuiltModel.Class("Value");
        owner.Assembly = new MistAssembly("Probe", MistAssemblyKind.Self);
        if(ownerKind == "native-base")
        {
            owner.BaseType = HandBuiltModel.Class("NativeBase");
            owner.BaseType.Assembly = MistAssembly.Ambient;
        }

        var method = CreateMethod(owner, model);
        var emitted = Emit(method);

        Assert.StartsWith("public new string ToString()", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("override", emitted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("formatting", "public string ToString(string format)")]
    [InlineData("generic", "public string ToString<T>()")]
    [InlineData("static", "public static string ToString()")]
    [InlineData("other-name", "public string Describe()")]
    [InlineData("ambient", "public string ToString()")]
    public void OtherDeclarationShapesKeepTheirExistingModifiers(string shape, string expected)
    {
        var model = new HandBuiltModel();
        var owner = HandBuiltModel.Class("Value");
        owner.Assembly = new MistAssembly("Probe", shape == "ambient" ? MistAssemblyKind.Ambient : MistAssemblyKind.Self);
        var method = CreateMethod(owner, model, shape == "static", shape == "other-name" ? "Describe" : "ToString");
        if(shape == "formatting")
        {
            method.ParametersList.Add(new Parameter("format", model.Type("String")));
        }

        if(shape == "generic")
        {
            method.TypeParametersList.Add(new TypeParameter("T", 0, owner.Scope, null));
        }

        Assert.StartsWith(expected, Emit(method), StringComparison.Ordinal);
    }

    [Fact]
    public void AnInterfaceSlotKeepsItsInterfaceSignature()
    {
        var model = new HandBuiltModel();
        var owner = new AbstractType("IText", "Probe", new Scope(null), null, null)
        {
            Assembly = new MistAssembly("Probe", MistAssemblyKind.Self),
        };
        var method = CreateMethod(owner, model);
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        TypeEmitter.EmitInterfaceMethod(method, writer, diagnostics);

        Assert.Equal("string ToString();", writer.ToString().Trim());
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AnExtensionKeepsItsStaticCarrierSignature()
    {
        var model = new HandBuiltModel();
        var receiver = HandBuiltModel.Class("Value");
        receiver.Assembly = new MistAssembly("Probe", MistAssemblyKind.Self);
        var block = new ExtensionBlock("self", "Probe", new Scope(null), new ExtensionBlockDeclarationNode())
        {
            ResolvedReceiverType = receiver,
            CarrierName = "ValueExtensions",
        };
        var method = new Method("ToString", model.Type("String"), [], DeclarationFragments.Public) { DeclaringBlock = block };
        block.MethodsList.Add(method);
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        var emitted = false;
        ExtensionBlockEmitter.Emit(block, writer, diagnostics, ref emitted);

        Assert.Contains("public static string ToString(this Probe.Value self)", writer.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("new string ToString", writer.ToString(), StringComparison.Ordinal);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    private static Method CreateMethod(MemberType owner, HandBuiltModel model, bool isStatic = false, string name = "ToString")
    {
        return new Method(name, model.Type("String"), [], DeclarationFragments.Public with { IsStatic = isStatic }) { DeclaringType = owner };
    }

    private static string Emit(Method method)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        MethodEmitter.Emit(method, emitReadonly: false, writer, diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray());
        return writer.ToString().Trim();
    }
}