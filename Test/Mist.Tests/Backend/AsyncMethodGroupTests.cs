using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class AsyncMethodGroupTests
{
    private readonly HandBuiltModel model = new();
    private readonly ClassType owner = HandBuiltModel.Class("Reader");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PromotedMethodGroup_CompletesUnderItsSynchronousDelegateContract(bool instance)
    {
        var integer = model.Type("Int32");
        var method = Method("Read", integer, instance, [new Parameter("value", integer)]);
        var receiver = instance ? Local("reader", owner) : null;
        var reference = Reference(method, receiver, new FunctionType([integer], integer));
        model.Model.TaskDrops.RecordDrop(method);

        using var promotion = AsyncPromotion.Enter(model.Model);
        var actual = Emit(reference);

        Assert.True(RespelledGroups.Applies(reference));
        Assert.Contains(instance
            ? "__receiver.Read(__arg0).GetAwaiter().GetResult()"
            : "Reader.Read(__arg0).GetAwaiter().GetResult()", actual, StringComparison.Ordinal);
        Assert.Contains("System.Func<int, int>", actual, StringComparison.Ordinal);
        Assert.DoesNotContain("await ", actual, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Run", actual, StringComparison.Ordinal);
    }

    [Fact]
    public void PromotedVoidMethodGroup_CompletesUnderAnActionContract()
    {
        var nothing = model.Type("Void");
        var method = Method("Log", nothing, instance: false);
        var reference = Reference(method, null, new FunctionType([], nothing));
        model.Model.TaskDrops.RecordDrop(method);

        using var promotion = AsyncPromotion.Enter(model.Model);

        Assert.Equal("((System.Action)(() => Reader.Log().GetAwaiter().GetResult()))", Emit(reference));
    }

    [Fact]
    public void PromotedClosedGenericMethodGroup_StatesTheClosedCallArguments()
    {
        var integer = model.Type("Int32");
        var parameter = new TypeParameter("T", 0, new Scope(null), null);
        var method = Method("Read", parameter, instance: false, [new Parameter("value", parameter)]);
        method.TypeParametersList.Add(parameter);
        var reference = Reference(method, null, new FunctionType([integer], integer), [integer]);
        model.Model.TaskDrops.RecordDrop(method);

        using var promotion = AsyncPromotion.Enter(model.Model);

        Assert.Equal("((System.Func<int, int>)((__arg0) => Reader.Read<int>(__arg0).GetAwaiter().GetResult()))", Emit(reference));
    }

    [Fact]
    public void PromotedSpecializedMethodGroup_UsesTheSelectedSpecializationName()
    {
        var integer = model.Type("Int32");
        var nothing = model.Type("Void");
        var parameter = new TypeParameter("T", 0, new Scope(null), null);
        var optional = model.Interner.GetOrCreateUnionType([parameter, nothing]);
        var method = Method("Read", integer, instance: false, [new Parameter("value", optional)]);
        method.TypeParametersList.Add(parameter);
        owner.MethodsList.Add(method);
        _ = model.Model.RegisterType(owner);
        model.Model.GenericCalls.Record(method, [integer]);
        var reference = Reference(method, null, new FunctionType([model.Interner.GetOrCreateUnionType([integer, nothing])], integer), [integer]);
        model.Model.TaskDrops.RecordDrop(method);

        using var promotion = AsyncPromotion.Enter(model.Model);
        var diagnostics = new DiagnosticBag();
        using var specialization = UnionSpecialization.Enter(model.Model, diagnostics);
        var actual = Emit(reference);

        Assert.Contains("Reader.Read__Standard_Int32(__arg0).GetAwaiter().GetResult()", actual, StringComparison.Ordinal);
        Assert.DoesNotContain("Read<int>", actual, StringComparison.Ordinal);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PromotedMethodOnAClosedGenericOwner_UsesItsDeclarationPromotion(bool instance)
    {
        var integer = model.Type("Int32");
        var parameter = new TypeParameter("T", 0, owner.Scope, owner);
        owner.TypeParametersList.Add(parameter);
        var declaration = Method("Read", parameter, instance);
        var closedOwner = model.Interner.GetOrCreateConstructedType(owner, [integer]);
        var closed = new Method("Read", integer, [], new MemberModifiers { IsStatic = !instance })
        { DeclaringType = owner, SourceDeclaration = declaration };
        Expression receiver = instance ? Local("reader", closedOwner) : new TypeReferenceExpression(HandBuiltModel.Node(), closedOwner);
        var reference = Reference(closed, receiver, new FunctionType([], integer));
        model.Model.TaskDrops.RecordDrop(declaration);

        using var promotion = AsyncPromotion.Enter(model.Model);
        var actual = Emit(reference);

        Assert.Contains(instance ? "__receiver.Read().GetAwaiter().GetResult()" : "Probe.Reader<int>.Read().GetAwaiter().GetResult()", actual, StringComparison.Ordinal);
        Assert.Contains("System.Func<int>", actual, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Func<T>", actual, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImplicitStructMethodGroup_CapturesThisBeforeEnteringItsLambda(bool generic)
    {
        var integer = model.Type("Int32");
        var owner = HandBuiltModel.Struct("Reader");
        if(generic)
        {
            owner.TypeParametersList.Add(new TypeParameter("T", 0, owner.Scope, owner));
        }

        var method = new Method("Read", integer, [], new MemberModifiers()) { DeclaringType = owner };
        var reference = Reference(method, null, new FunctionType([], integer));
        model.Model.TaskDrops.RecordDrop(method);

        using var promotion = AsyncPromotion.Enter(model.Model);
        var actual = Emit(reference);

        Assert.Contains(generic ? "System.Func<Probe.Reader<T>, System.Func<int>>" : "System.Func<Reader, System.Func<int>>", actual, StringComparison.Ordinal);
        Assert.EndsWith(")(this)", actual, StringComparison.Ordinal);
        Assert.Contains("__receiver => () => __receiver.Read().GetAwaiter().GetResult()", actual, StringComparison.Ordinal);
        Assert.DoesNotContain("=> this.", actual, StringComparison.Ordinal);
    }

    [Fact]
    public void PromotedInstanceMethodGroup_CapturesASideEffectingReceiverAtFormationOnce()
    {
        var integer = model.Type("Int32");
        var method = Method("Read", integer, instance: true);
        var create = Method("Next", owner, instance: false);
        var receiver = new InvocationExpression(HandBuiltModel.Node(),
            new MemberAccessExpression(HandBuiltModel.Node(), new TypeReferenceExpression(HandBuiltModel.Node(), owner), create.Name, owner),
            [], owner, create, null);
        var reference = Reference(method, receiver, new FunctionType([], integer));
        model.Model.TaskDrops.RecordDrop(method);

        using var promotion = AsyncPromotion.Enter(model.Model);

        Assert.Equal("((System.Func<Reader, System.Func<int>>)(__receiver => () => __receiver.Read().GetAwaiter().GetResult()))(Reader.Next())", Emit(reference));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinarySyncAndDeclaredAsyncMethodGroups_RetainTheirDelegateContract(bool declaredAsync)
    {
        var integer = model.Type("Int32");
        var task = new ClassType("Task<>", "Standard", new Scope(null), null, null);
        task.TypeParametersList.Add(new TypeParameter("T", 0, task.Scope, task));
        var result = declaredAsync ? model.Interner.GetOrCreateConstructedType(task, [integer]) : (IMistType)integer;
        var method = Method("Read", result, instance: false, declaredAsync: declaredAsync);
        var reference = Reference(method, new TypeReferenceExpression(HandBuiltModel.Node(), owner), new FunctionType([], result));
        if(declaredAsync)
        {
            model.Model.TaskDrops.RecordDrop(method);
        }

        using var promotion = AsyncPromotion.Enter(model.Model);

        Assert.False(RespelledGroups.Applies(reference));
        Assert.Equal("Reader.Read", Emit(reference));
    }

    private Method Method(string name, IMistType result, bool instance, List<Parameter>? parameters = null, bool declaredAsync = false)
    {
        return new Method(name, result, parameters ?? [], new MemberModifiers { IsStatic = !instance, IsAsync = declaredAsync })
        { DeclaringType = owner };
    }

    private static FunctionReferenceExpression Reference(Method method, Expression? receiver, FunctionType function, IMistType[]? typeArguments = null)
    {
        return new FunctionReferenceExpression(HandBuiltModel.Node(), receiver, method, function) { TypeArguments = typeArguments ?? [] };
    }

    private static LocalReferenceExpression Local(string name, IMistType type)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable(name, type));
    }

    private static string Emit(Expression expression)
    {
        var diagnostics = new DiagnosticBag();
        var actual = BodyEmitter.Render(expression, diagnostics);
        Assert.Empty(diagnostics.ToImmutableArray());
        return actual;
    }
}