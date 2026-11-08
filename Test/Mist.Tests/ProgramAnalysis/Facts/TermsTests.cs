using System.Numerics;
using Mist.ProgramAnalysis.Facts;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis.Facts;

[Trait("Tier", "1")]
public sealed class TermsTests
{
    [Theory]
    [InlineData("literal")]
    [InlineData("largeUnsigned")]
    [InlineData("enum")]
    [InlineData("local")]
    [InlineData("member")]
    [InlineData("field")]
    [InlineData("bareField")]
    [InlineData("bareFieldMember")]
    [InlineData("add")]
    [InlineData("subtract")]
    [InlineData("multiplyLeft")]
    [InlineData("multiplyRight")]
    [InlineData("negate")]
    [InlineData("intrinsic")]
    public void AdmittedShapesReadAsLinearTerms(string shape)
    {
        var model = new FactModel();
        var h = model.Expressions;
        var local = HandBuiltRefinements.Local(h.Int);
        var value = model.Value("value");
        var bindings = Bindings.Empty.Set(local.Local, value);
        var expected = new Term(value);
        Expression expression = local;
        switch(shape)
        {
            case "literal":
                expression = h.Literal(7);
                expected = new Term(7);
                break;
            case "largeUnsigned":
                expression = h.Literal(ulong.MaxValue, h.Built.Type("UInt64"));
                expected = new Term(new BigInteger(ulong.MaxValue));
                break;
            case "enum":
                var enums = new EnumRefinementModel();
                expression = EnumRefinementModel.Member(enums.SourceEnum(), "Seven", enums.Facts.Literal(7));
                expected = new Term(7);
                break;
            case "member":
                var receiver = HandBuiltRefinements.Local(h.Sequence());
                expression = h.Length(receiver);
                bindings = bindings.Set(receiver.Local, value);
                bindings = bindings.Set(Terms.Holder(expression, bindings)!, value);
                break;
            case "field":
                var owner = h.Built.Type("Owner");
                var field = new Field("count", h.Int, default, new MemberModifiers());
                expression = new MemberAccessExpression(HandBuiltModel.Node(), new ThisExpression(HandBuiltModel.Node(), owner),
                    "count", h.Int, member: new FieldResult(field, owner, []));
                bindings = bindings.Set(field, value);
                break;
            case "bareField":
                var count = new Field("count", h.Int, default, new MemberModifiers());
                expression = new FieldReferenceExpression(HandBuiltModel.Node(), count);
                bindings = bindings.Set(count, value);
                break;
            case "bareFieldMember":
                var backing = new Field("backing", h.Sequence(), default, new MemberModifiers());
                expression = h.Length(new FieldReferenceExpression(HandBuiltModel.Node(), backing));
                bindings = bindings.Set(backing, value);
                bindings = bindings.Set(Terms.Holder(expression, bindings)!, value);
                break;
            case "add":
                expression = h.Binary(local, BinaryOperatorKind.Add, h.Literal(2), h.Int);
                expected = expected.Add(new Term(2));
                break;
            case "subtract":
                expression = h.Binary(local, BinaryOperatorKind.Subtract, h.Literal(2), h.Int);
                expected = expected.Add(new Term(-2));
                break;
            case "multiplyLeft":
                expression = h.Binary(h.Literal(2), BinaryOperatorKind.Multiply, local, h.Int);
                expected = expected.Scale(2);
                break;
            case "multiplyRight":
                expression = h.Binary(local, BinaryOperatorKind.Multiply, h.Literal(2), h.Int);
                expected = expected.Scale(2);
                break;
            case "negate":
                expression = new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, local, h.Int);
                expected = expected.Scale(-1);
                break;
            case "intrinsic":
                expression = new BinaryExpression(HandBuiltModel.Node(), local, BinaryOperatorKind.Add, h.Literal(1), h.Int)
                {
                    Operator = new Operator(OperatorKind.Plus, h.Int, [], new MemberModifiers { IsIntrinsic = true }),
                };
                expected = expected.Add(new Term(1));
                break;
        }

        Assert.Equal(expected, Terms.Read(expression, bindings));
    }

    [Theory]
    [InlineData("divide")]
    [InlineData("modulo")]
    [InlineData("product")]
    [InlineData("call")]
    [InlineData("userBinary")]
    [InlineData("userUnary")]
    [InlineData("unknown")]
    [InlineData("unreadableOperand")]
    public void UnlistedShapesDoNotInventTerms(string shape)
    {
        var model = new FactModel();
        var h = model.Expressions;
        var local = HandBuiltRefinements.Local(h.Int);
        var call = h.Call(local, h.Int);
        var bindings = Bindings.Empty.Set(local.Local, model.Value("value"));
        var expression = shape switch
        {
            "divide" => h.Binary(local, BinaryOperatorKind.Divide, h.Literal(2), h.Int),
            "modulo" => h.Binary(local, BinaryOperatorKind.Modulo, h.Literal(2), h.Int),
            "product" => h.Binary(local, BinaryOperatorKind.Multiply, local, h.Int),
            "call" => call,
            "userBinary" => new BinaryExpression(HandBuiltModel.Node(), local, BinaryOperatorKind.Add, h.Literal(1), h.Int)
            {
                Operator = new Operator(OperatorKind.Plus, h.Int, [], new MemberModifiers()),
            },
            "userUnary" => new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, local, h.Int)
            {
                Operator = new Operator(OperatorKind.Minus, h.Int, [], new MemberModifiers()),
            },
            "unreadableOperand" => h.Binary(local, BinaryOperatorKind.Add, call, h.Int),
            _ => HandBuiltRefinements.Local(h.Int, "unbound"),
        };

        Assert.Null(Terms.Read(expression, bindings));
    }

    [Fact]
    public void BareAndExplicitFieldReadsShareHolders()
    {
        var model = new FactModel();
        var h = model.Expressions;
        var owner = h.Built.Type("Owner");
        var backing = new Field("backing", h.Sequence(), default, new MemberModifiers());
        var bare = new FieldReferenceExpression(HandBuiltModel.Node(), backing);
        var explicitRead = new MemberAccessExpression(HandBuiltModel.Node(), new ThisExpression(HandBuiltModel.Node(), owner),
            "backing", backing.Type, member: new FieldResult(backing, owner, []));
        var value = model.Value("backing");
        var length = model.Value("length");
        var bindings = Bindings.Empty.Set(backing, value);
        bindings = bindings.Set(Terms.Holder(h.Length(bare), bindings)!, length);

        Assert.Same(backing, Terms.Holder(bare, bindings));
        Assert.Same(backing, Terms.Holder(explicitRead, bindings));
        Assert.Equal(new Term(value), Terms.Read(bare, bindings));
        Assert.Equal(Terms.Read(explicitRead, bindings), Terms.Read(bare, bindings));
        Assert.Equal(Terms.Holder(h.Length(explicitRead), bindings), Terms.Holder(h.Length(bare), bindings));
        Assert.Equal(new Term(length), Terms.Read(h.Length(explicitRead), bindings));
    }
}