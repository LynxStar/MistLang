using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class UnknownUnionEmissionTests
{
    private readonly HandBuiltModel model = new();
    private static readonly UnknownType Unknown = UnknownType.Instance;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WideningACarriedValueOrReference_ReadsTheStoredPayload(bool referencePayload)
    {
        var payload = referencePayload ? HandBuiltModel.Class("Payload") : model.Type("Int32");
        var union = new UnionType([payload, model.Type("String")]);
        var conversion = new ImplicitConversionExpression(HandBuiltModel.Node(), Local("u", union), Unknown, []);

        Assert.Equal("(u).Payload", CastLowering.Implicit(conversion, "u"));
        Assert.Equal("(u).Payload", Emit(conversion));
        var carrier = Carrier(union);
        Assert.Contains("public object Payload => _value;", carrier);
        Assert.Contains("_value = value;", carrier);
        Assert.DoesNotContain(".Clone(", carrier);
    }

    [Fact]
    public void UnknownSlots_UnwrapLocalsAssignmentsReturnsAndArguments()
    {
        var union = new UnionType([model.Type("Int32"), model.Type("String")]);
        var source = Local("u", union);
        var owner = HandBuiltModel.Class("Holder");
        var field = new MemberAccessExpression(HandBuiltModel.Node(), Local("holder", owner), "Value", Unknown);
        var method = new Method("Accept", model.Type("Void"), [new Parameter("value", Unknown)], new MemberModifiers { IsStatic = true }) { DeclaringType = owner };
        var call = new InvocationExpression(HandBuiltModel.Node(),
            new MemberAccessExpression(HandBuiltModel.Node(), new TypeReferenceExpression(HandBuiltModel.Node(), owner), "Accept", method.ReturnType),
            [source], method.ReturnType, method, [Unknown]);

        Assert.Equal("object x = (u).Payload;", Statement(new LocalDeclarationStatement(new LocalVariable("x", Unknown), source)));
        Assert.Equal("holder.Value = (u).Payload", Emit(new AssignmentExpression(HandBuiltModel.Node(), field, source, Unknown)));
        Assert.Equal("return (u).Payload;", Statement(new ReturnStatement(source, Unknown)));
        Assert.Equal("Holder.Accept((u).Payload)", Emit(call));
    }

    [Fact]
    public void UnknownPayloadTests_BindTheConcreteValueAndLeaveFormattingOnThePayload()
    {
        var integer = model.Type("Int32");
        var text = model.Type("String");
        var union = new UnionType([integer, text]);
        var widened = new ImplicitConversionExpression(HandBuiltModel.Node(), Local("u", union), Unknown, []);

        Assert.Equal("(u).Payload is int n", Test(widened, integer, "n"));
        Assert.Equal("x is string t", Test(Local("x", Unknown), text, "t"));
        var console = HandBuiltModel.Class("Console");
        var print = new Method("WriteLine", model.Type("Void"), [new Parameter("value", Unknown)], new MemberModifiers { IsStatic = true }) { DeclaringType = console };
        var call = new InvocationExpression(HandBuiltModel.Node(),
            new MemberAccessExpression(HandBuiltModel.Node(), new TypeReferenceExpression(HandBuiltModel.Node(), console), "WriteLine", print.ReturnType),
            [widened], print.ReturnType, print, [Unknown]);

        Assert.Equal("Console.WriteLine((u).Payload)", Emit(call));
    }

    [Fact]
    public void RetestingUnknownAgainstAUnion_ReentersTheCarrierAndBindsIt()
    {
        var union = new UnionType([model.Type("Int32"), model.Type("String")]);

        var actual = Test(Local("x", Unknown), union, "k");

        Assert.Contains("System.Func<object, Union_Int32_String?>", actual);
        Assert.Contains("__payload is int __part0", actual);
        Assert.Contains("__payload is string __part1", actual);
        Assert.Contains("((Union_Int32_String)(__part0))", actual);
        Assert.Contains("((Union_Int32_String)(__part1))", actual);
        Assert.EndsWith("(__e1) is Union_Int32_String k)", actual);
        Assert.DoesNotContain(".Clone(", actual);
    }

    [Fact]
    public void PlainWideningOptionalsUnionConversionsAndDirectTests_KeepTheirRepresentation()
    {
        var integer = model.Type("Int32");
        var text = model.Type("String");
        var union = new UnionType([integer, text]);
        var optional = new UnionType([integer, model.Type("Void")]);

        Assert.Equal("number", CastLowering.Implicit(new ImplicitConversionExpression(HandBuiltModel.Node(), Local("number", integer), Unknown, []), "number"));
        Assert.Equal("object x = number;", Statement(new LocalDeclarationStatement(new LocalVariable("x", Unknown), Local("number", integer))));
        Assert.Equal("object x = maybe;", Statement(new LocalDeclarationStatement(new LocalVariable("x", Unknown), Local("maybe", optional))));
        Assert.Equal("Union_Boolean_Int32_String wide = u;", Statement(new LocalDeclarationStatement(
            new LocalVariable("wide", new UnionType([integer, text, model.Type("Boolean")])), Local("u", union))));
        Assert.Equal("u.AsInt32OrNull is int n", Test(Local("u", union), integer, "n", integer));
    }

    private string Test(Expression value, IMistType type, string binding, IMistType? constituent = null)
    {
        return Emit(new IsPatternExpression(HandBuiltModel.Node(), value,
            new TypeTestPattern(HandBuiltModel.Node(), type, new LocalVariable(binding, type), constituent), model.Type("Boolean")));
    }

    private static string Carrier(UnionType union)
    {
        var writer = new CSharpWriter();
        using var oblivious = TypeNameEmitter.Oblivious();
        UnionCarrierEmitter.Emit(UnionCarrierNames.CarrierName(union), union,
            new Dictionary<string, UnionType> { [UnionCarrierNames.CarrierName(union)] = union }, writer);
        return writer.ToString();
    }

    private static LocalReferenceExpression Local(string name, IMistType type)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable(name, type));
    }

    private static string Emit(Expression expression)
    {
        BodyEmitter.ResetTemporaries();
        var diagnostics = new DiagnosticBag();
        var result = BodyEmitter.Render(expression, diagnostics);
        Assert.Empty(diagnostics.ToImmutableArray());
        return result;
    }

    private static string Statement(Statement statement)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        BodyEmitter.EmitStatement(statement, writer, diagnostics);
        Assert.Empty(diagnostics.ToImmutableArray());
        return writer.ToString().Trim();
    }
}