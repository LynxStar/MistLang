using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class UniversalToStringInvariantCultureEmissionTests
{
    [Theory]
    [InlineData("class", "value.ToString();")]
    [InlineData("formattable", "MistInvariant.Text(value);")]
    [InlineData("primitive", "value.ToString(System.Globalization.CultureInfo.InvariantCulture);")]
    [InlineData("unknown", "MistInvariant.Text(value);")]
    public void UniversalZeroArgumentCallsKeepTheirFormattingBehavior(string receiverKind, string expected)
    {
        var model = new HandBuiltModel();
        var owner = SourceClass(receiverKind == "formattable");
        IMistType receiver = receiverKind switch
        {
            "primitive" => model.Type("Double"),
            "unknown" => UnknownType.Instance,
            _ => owner,
        };
        var block = new ExtensionBlock("self", "Standard", new Scope(null), new ExtensionBlockDeclarationNode())
        {
            ResolvedReceiverType = UnknownType.Instance,
            CarrierName = "UnknownExtensions",
        };
        var method = new Method("ToString", model.Type("String"), [], DeclarationFragments.Public) { DeclaringBlock = block };

        Assert.Equal(expected, Emit(receiver, method));
    }

    [Fact]
    public void ASourceDeclaredZeroArgumentImplementationKeepsItsOrdinaryCall()
    {
        var model = new HandBuiltModel();
        var owner = SourceClass(formattable: true);
        var method = new Method("ToString", model.Type("String"), [], DeclarationFragments.Public) { DeclaringType = owner };

        Assert.Equal("value.ToString();", Emit(owner, method));
        Assert.False(InvariantCulture.Used());
    }

    [Theory]
    [InlineData(1, "value.ToString(format);")]
    [InlineData(2, "value.ToString(format, provider);")]
    public void FormattingOverloadsKeepTheirExplicitArguments(int arity, string expected)
    {
        var model = new HandBuiltModel();
        var owner = SourceClass(arity == 2);
        var text = model.Type("String");
        var provider = new AbstractType("IFormatProvider", "System", new Scope(null), null, null);
        var parameters = arity == 1 ? new[] { new Parameter("format", text) } : [new Parameter("format", text), new Parameter("provider", provider)];
        var method = new Method("ToString", text, [.. parameters], DeclarationFragments.Public) { DeclaringType = owner };
        var arguments = parameters.Select(parameter => (Expression)Local(parameter.Name, parameter.Type)).ToArray();

        Assert.Equal(expected, Emit(owner, method, arguments));
        Assert.False(InvariantCulture.Used());
    }

    private static ClassType SourceClass(bool formattable)
    {
        var owner = HandBuiltModel.Class("Temp");
        owner.Assembly = new MistAssembly("Probe", MistAssemblyKind.Self);
        if(formattable)
        {
            owner.BaseAbstractTypes.Add(new AbstractType("IFormattable", "System", new Scope(null), null, null));
        }

        return owner;
    }

    private static LocalReferenceExpression Local(string name, IMistType type)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable(name, type));
    }

    private static string Emit(IMistType receiver, Method method, params Expression[] arguments)
    {
        InvariantCulture.Reset();
        var access = new MemberAccessExpression(HandBuiltModel.Node(), Local("value", receiver), method.Name, method.ReturnType);
        var invocation = new InvocationExpression(HandBuiltModel.Node(), access, arguments, method.ReturnType, method, null);
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        BodyEmitter.EmitStatement(new ExpressionStatement(invocation), writer, diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray());
        return writer.ToString().Trim();
    }
}