using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class ExtensionMethodGroupTests
{
    private readonly HandBuiltModel model = new();

    [Theory]
    [InlineData("Trim", false)]
    [InlineData("Tally", true)]
    public void BoundExtensionGroups_CallTheirCarrierWithTheCapturedReceiver(string name, bool hasArgument)
    {
        var text = model.Type("String");
        var integer = model.Type("Int32");
        var method = Extension(name, text, hasArgument ? integer : text,
            hasArgument ? [new Parameter("suffix", text)] : []);
        var reference = Reference(method, Local("text", text));

        Assert.True(RespelledGroups.Applies(reference));
        Assert.Equal(hasArgument
            ? "((System.Func<string, System.Func<string, int>>)(__receiver => (__arg0) => Probe.StringExtensions.Tally(__receiver, __arg0)))(text)"
            : "((System.Func<string, System.Func<string>>)(__receiver => () => Probe.StringExtensions.Trim(__receiver)))(text)", Emit(reference));
    }

    [Fact]
    public void SideEffectingReceiver_IsEvaluatedOnceAtGroupFormation()
    {
        var text = model.Type("String");
        var method = Extension("Trim", text, text);
        var owner = HandBuiltModel.Class("Factory");
        var next = new Method("Next", text, [], new MemberModifiers { IsStatic = true }) { DeclaringType = owner };
        var receiver = new InvocationExpression(HandBuiltModel.Node(),
            new MemberAccessExpression(HandBuiltModel.Node(), new TypeReferenceExpression(HandBuiltModel.Node(), owner), "Next", text),
            [], text, next, null);

        Assert.Equal("((System.Func<string, System.Func<string>>)(__receiver => () => Probe.StringExtensions.Trim(__receiver)))(Factory.Next())",
            Emit(Reference(method, receiver)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosedBlockAndMethodArguments_FollowTheCallNaming(bool isStatic)
    {
        var text = model.Type("String");
        var integer = model.Type("Int32");
        var declaration = Extension("Read", text, integer, isStatic: isStatic);
        var block = declaration.DeclaringBlock!;
        var blockParameter = new TypeParameter("T", 0, block.Scope, null);
        block.TypeParametersList.Add(blockParameter);
        declaration.TypeParametersList.Add(new TypeParameter("U", 0, block.Scope, null));
        var method = new Method("Read", integer, [], declaration.Modifiers)
        {
            DeclaringBlock = block,
            SourceDeclaration = declaration,
            ClosedBlock = block,
            BlockClosing = TypeSubstitution.ForParameters([blockParameter], [text]),
        };
        method.TypeParametersList.AddRange(declaration.TypeParametersList);
        Expression receiver = isStatic ? new TypeReferenceExpression(HandBuiltModel.Node(), text) : Local("text", text);
        var reference = Reference(method, receiver);
        reference = new FunctionReferenceExpression(HandBuiltModel.Node(), receiver, method, reference.Type) { TypeArguments = [integer] };

        Assert.Equal(isStatic
            ? "((System.Func<int>)(() => Probe.StringExtensions.Read<string, int>()))"
            : "((System.Func<string, System.Func<int>>)(__receiver => () => Probe.StringExtensions.Read<string, int>(__receiver)))(text)", Emit(reference));
    }

    [Theory]
    [InlineData("ToString", 0)]
    [InlineData("GetHashCode", 0)]
    [InlineData("Equals", 1)]
    public void ObjectMembers_KeepTheCallPathsInstanceSpelling(string name, int arity)
    {
        var owner = model.Type("String");
        var method = Extension(name, owner, model.Type("Int32"), arity == 0 ? [] : [new Parameter("other", owner)]);
        var reference = Reference(method, Local("value", owner));

        Assert.Null(ExtensionCalls.Carrier(method));
        Assert.False(RespelledGroups.Applies(reference));
        Assert.Equal($"value.{name}", Emit(reference));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BoundInstanceAndStaticGroups_KeepTheirSelectedMember(bool isStatic)
    {
        var owner = HandBuiltModel.Class("Reader");
        var method = new Method("Read", model.Type("Int32"), [], new MemberModifiers { IsStatic = isStatic }) { DeclaringType = owner };
        Expression receiver = isStatic ? new TypeReferenceExpression(HandBuiltModel.Node(), owner) : Local("reader", owner);
        var reference = Reference(method, receiver);

        Assert.False(RespelledGroups.Applies(reference));
        Assert.Equal(isStatic ? "Reader.Read" : "reader.Read", Emit(reference));
    }

    private static Method Extension(string name, IMistType receiver, IMistType result, List<Parameter>? parameters = null, bool isStatic = false)
    {
        var block = new ExtensionBlock("self", "Probe", new Scope(null), new ExtensionBlockDeclarationNode())
        { ResolvedReceiverType = receiver, CarrierName = "StringExtensions" };
        return new Method(name, result, parameters ?? [], new MemberModifiers { IsStatic = isStatic }) { DeclaringBlock = block };
    }

    private static FunctionReferenceExpression Reference(Method method, Expression receiver)
    {
        return new FunctionReferenceExpression(HandBuiltModel.Node(), receiver, method,
            new FunctionType([.. method.ParametersList.Select(parameter => parameter.Type)], method.ReturnType));
    }

    private static LocalReferenceExpression Local(string name, IMistType type)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable(name, type));
    }

    private static string Emit(Expression expression)
    {
        var diagnostics = new DiagnosticBag();
        var result = BodyEmitter.Render(expression, diagnostics);
        Assert.Empty(diagnostics.ToImmutableArray());
        return result;
    }
}