using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.HandBuiltModel;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class ResultLoweringTests
{
    [Fact]
    public void PropagationIsSequencedBetweenEarlierAndLaterOperands()
    {
        var shapes = new ResultModel();
        var result = shapes.Result(shapes.Integer, shapes.Text);
        var left = Call("Before", shapes.Integer);
        var source = Call("Read", result);
        var right = Call("After", shapes.Integer);
        var expression = new TupleLiteralExpression(Node(), [left, shapes.Propagate(source, result), right],
            [], shapes.Built.Interner.GetOrCreateTupleType([shapes.Integer, shapes.Integer, shapes.Integer]));
        var block = Block(new LocalDeclarationStatement(new LocalVariable("values", expression.Type), expression));
        var diagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();
        BodyEmitter.EmitBlock(block, writer, diagnostics);
        var text = writer.ToString();
        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.True(text.IndexOf("Before(", StringComparison.Ordinal) < text.IndexOf("Read(", StringComparison.Ordinal));
        Assert.True(text.IndexOf("return ", StringComparison.Ordinal) < text.IndexOf("After(", StringComparison.Ordinal));
        Assert.Equal(1, text.Split("Read(").Length - 1);
        Assert.Contains(".Failure(", text);
    }

    [Fact]
    public void PropagationInAShortCircuitedOperandStaysInsideItsBranch()
    {
        var shapes = new ResultModel();
        var result = shapes.Result(shapes.Boolean, shapes.Text);
        var expression = new BinaryExpression(Node(), Literal(shapes.Boolean, false),
            BinaryOperatorKind.LogicalAnd, shapes.Propagate(Call("Read", result), result), shapes.Boolean);
        var block = Block(new LocalDeclarationStatement(new LocalVariable("value", shapes.Boolean), expression));
        var writer = new CSharpWriter();
        BodyEmitter.EmitBlock(block, writer, new DiagnosticBag());
        var text = writer.ToString();
        Assert.True(text.IndexOf("if (false)", StringComparison.Ordinal) < text.IndexOf("Read(", StringComparison.Ordinal));
    }

    [Fact]
    public void ADiscardedPropagationLowersToStatementsWithoutAnIllegalPayloadStatement()
    {
        var shapes = new ResultModel();
        var result = shapes.Result(shapes.Void, shapes.Text);
        var propagation = shapes.Propagate(Call("Complete", result), result);
        var writer = new CSharpWriter();
        BodyEmitter.EmitBlock(Block(new ExpressionStatement(propagation)), writer, new DiagnosticBag());
        Assert.DoesNotMatch(@"\.As\w+\.Item2;", writer.ToString());
        Assert.Contains(".Inspect()", writer.ToString());
    }

    [Fact]
    public void AnEarlierLocalReadIsCapturedBeforeAPropagationCanChangeIt()
    {
        var shapes = new ResultModel();
        var local = new LocalVariable("original", shapes.Integer);
        var propagation = shapes.Propagate(Call("Mutate", shapes.Result(shapes.Integer, shapes.Text)), shapes.Result(shapes.Integer, shapes.Text));
        var tuple = new TupleLiteralExpression(Node(), [new LocalReferenceExpression(Node(), local), propagation], [],
            shapes.Built.Interner.GetOrCreateTupleType([shapes.Integer, shapes.Integer]));
        var body = Block(new LocalDeclarationStatement(local, Literal(shapes.Integer, 1L)),
            new LocalDeclarationStatement(new LocalVariable("values", tuple.Type), tuple));
        var writer = new CSharpWriter();
        BodyEmitter.EmitBlock(body, writer, new DiagnosticBag());
        var text = writer.ToString();
        Assert.True(text.IndexOf("= original;", StringComparison.Ordinal) < text.IndexOf("Mutate(", StringComparison.Ordinal));
        Assert.Contains("= original;", text);
    }

    [Fact]
    public void VoidIdentityEvaluatesOnceWithoutAnErrorReturnDestination()
    {
        var shapes = new ResultModel();
        var identity = new ResultIdentityExpression(Node(), Call("Plain", shapes.Void));
        var writer = new CSharpWriter();
        BodyEmitter.EmitBlock(Block(new ExpressionStatement(identity)), writer, new DiagnosticBag());
        var text = writer.ToString();
        Assert.Equal(1, text.Split("Plain(").Length - 1);
        Assert.Contains("_ = Standard.Void.Instance;", text);
    }

    private static InvocationExpression Call(string name, ProgramModel.Contracts.Types.IMistType type)
    {
        return new InvocationExpression(Node(), new MethodReferenceExpression(Node(), new Method(name, type, [], new MemberModifiers { IsStatic = true })), [], type, null, null);
    }
}