using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
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
public sealed class OptionalAsyncPromotionTests
{
    private readonly HandBuiltModel model = new();
    private readonly ClassType reader = HandBuiltModel.Class("Reader");

    [Theory]
    [InlineData("Int32", "int?")]
    [InlineData("String", "string?")]
    public void ConditionalPromotedResult_NarrowsBeforeAwaitAndRetainsAbsence(string resultName, string spelling)
    {
        var result = Type(resultName);
        var method = Method("Read", result);
        var receiver = Local("reader", Optional(reader));
        var call = Call(receiver, method, Optional(result));
        model.Model.TaskDrops.RecordDrop(method);

        using var promotion = AsyncPromotion.Enter(model.Model);

        Assert.Equal($"(reader is {{ }} __e1 ? await __e1.Read() : default({spelling}))", Emit(call));
    }

    [Fact]
    public void ConditionalPromotedResult_EvaluatesReceiverOnceAndArgumentsOnlyWhenPresent()
    {
        var integer = Type("Int32");
        var method = Method("Read", integer, [new Parameter("value", integer)]);
        var next = Method("Next", Optional(reader));
        var argument = Method("Argument", integer);
        var receiver = Direct(next);
        var call = Call(receiver, method, Optional(integer), [Direct(argument)]);
        model.Model.TaskDrops.RecordDrop(method);

        using var promotion = AsyncPromotion.Enter(model.Model);

        Assert.Equal("(Next() is { } __e1 ? await __e1.Read(Argument()) : default(int?))", Emit(call));
    }

    [Fact]
    public void ConditionalPromotedResult_CompletesInsideASynchronousLambda()
    {
        var integer = Type("Int32");
        var method = Method("Read", integer);
        var optional = Optional(integer);
        var call = Call(Local("reader", Optional(reader)), method, optional);
        var lambda = new LambdaExpression(HandBuiltModel.Node(), [], call, null, false, new FunctionType([], optional));
        model.Model.TaskDrops.RecordDrop(method);

        using var promotion = AsyncPromotion.Enter(model.Model);

        Assert.Equal("() => (reader is { } __e1 ? __e1.Read().GetAwaiter().GetResult() : default(int?))", Emit(lambda));
    }

    [Fact]
    public void ConditionalPromotedGenericResult_RetainsClosedCallArguments()
    {
        var integer = Type("Int32");
        var method = Method("Read", integer);
        var access = new MemberAccessExpression(HandBuiltModel.Node(), Local("reader", Optional(reader)),
            "Read", Optional(integer), [integer], isConditional: true);
        var call = new InvocationExpression(HandBuiltModel.Node(), access, [], Optional(integer), method, null);
        model.Model.TaskDrops.RecordDrop(method);

        using var promotion = AsyncPromotion.Enter(model.Model);

        Assert.Equal("(reader is { } __e1 ? await __e1.Read<int>() : default(int?))", Emit(call));
    }

    [Fact]
    public void ConditionalPromotedStatement_GuardsCompletionWithoutAConditionalExpressionStatement()
    {
        var method = Method("Read", Type("Int32"));
        var call = Call(Local("reader", Optional(reader)), method, Optional(method.ReturnType));
        model.Model.TaskDrops.RecordDrop(method);

        using var promotion = AsyncPromotion.Enter(model.Model);
        BodyEmitter.ResetTemporaries();
        var writer = new CSharpWriter();
        BodyEmitter.EmitStatement(new ExpressionStatement(call), writer, new DiagnosticBag());

        Assert.Contains("if (reader is { } __e1)", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("await __e1.Read();", writer.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("?.Read", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ConditionalPromotedVoidUsedAsAValue_IsGuardedAndHoistedToTheUnit()
    {
        var nothing = model.Type("Void");
        var method = Method("Log", nothing);
        var call = Call(Local("reader", Optional(reader)), method, nothing);
        var local = new LocalVariable("unit", nothing);
        model.Model.TaskDrops.RecordDrop(method);

        using var promotion = AsyncPromotion.Enter(model.Model);
        BodyEmitter.ResetTemporaries();
        var writer = new CSharpWriter();
        BodyEmitter.EmitStatement(new LocalDeclarationStatement(local, call), writer, new DiagnosticBag());

        Assert.Contains("if (reader is { }", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains(".Log();", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Void unit = Standard.Void.Instance;", writer.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(" ? await ", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ConditionalPromotedResult_UnderOptionalChainingKeepsTheCompletedValueParenthesized()
    {
        var text = model.Type("String");
        var integer = Type("Int32");
        var method = Method("Read", text);
        var call = Call(Local("reader", Optional(reader)), method, Optional(text));
        var length = new MemberAccessExpression(HandBuiltModel.Node(), call, "Length", Optional(integer), isConditional: true);
        model.Model.TaskDrops.RecordDrop(method);

        using var promotion = AsyncPromotion.Enter(model.Model);

        Assert.Equal("((reader is { } __e1 ? await __e1.Read() : default(string?)))?.Length", Emit(length));
    }

    [Fact]
    public void OrdinarySynchronousConditionalCall_RetainsConditionalAccess()
    {
        var integer = Type("Int32");
        var method = Method("Read", integer);
        var call = Call(Local("reader", Optional(reader)), method, Optional(integer));

        using var promotion = AsyncPromotion.Enter(model.Model);

        Assert.Equal("reader?.Read()", Emit(call));
    }

    private IMistType Type(string name)
    {
        return name == "Int32" ? new StructType(name, "Standard", new Scope(null), null, null) : model.Type(name);
    }

    private Method Method(string name, IMistType result, List<Parameter>? parameters = null)
    {
        return new Method(name, result, parameters ?? [], new MemberModifiers()) { DeclaringType = reader };
    }

    private IMistType Optional(IMistType type)
    {
        return model.Interner.GetOrCreateUnionType([type, model.Type("Void")]);
    }

    private static LocalReferenceExpression Local(string name, IMistType type)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable(name, type));
    }

    private static InvocationExpression Call(Expression receiver, Method method, IMistType result, Expression[]? arguments = null)
    {
        var access = new MemberAccessExpression(HandBuiltModel.Node(), receiver, method.Name, result, isConditional: true);
        return new InvocationExpression(HandBuiltModel.Node(), access, arguments ?? [], result, method, null);
    }

    private static InvocationExpression Direct(Method method)
    {
        return new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), method),
            [], method.ReturnType, method, null);
    }

    private static string Emit(Expression expression)
    {
        BodyEmitter.ResetTemporaries();
        var diagnostics = new DiagnosticBag();
        var emitted = BodyEmitter.Render(expression, diagnostics);
        Assert.Empty(diagnostics.ToImmutableArray());
        return emitted;
    }
}