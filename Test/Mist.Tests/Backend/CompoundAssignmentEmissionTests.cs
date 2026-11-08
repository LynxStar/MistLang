using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class CompoundAssignmentEmissionTests
{
    private readonly HandBuiltModel model = new();

    private IMistType Integer => model.Type("Int32");

    [Theory]
    [InlineData("Add", "None", "total += 2")]
    [InlineData("Subtract", "None", "total -= 2")]
    [InlineData("Multiply", "None", "total *= 2")]
    [InlineData("Divide", "None", "total /= 2")]
    [InlineData("Modulo", "None", "total %= 2")]
    [InlineData("Add", "Prefix", "++total")]
    [InlineData("Subtract", "Prefix", "--total")]
    [InlineData("Add", "Postfix", "total++")]
    [InlineData("Subtract", "Postfix", "total--")]
    public void EachFormIsTheCSharpOperator(string operation, string increment, string expected)
    {
        var kind = Enum.Parse<BinaryOperatorKind>(operation);
        var form = Enum.Parse<IncrementForm>(increment);
        var total = new LocalVariable("total", Integer);

        Assert.Equal(expected, Emit(Compound(Ref(total), kind, HandBuiltModel.Literal(Integer, form == IncrementForm.None ? 2L : 1L), form)));
    }

    [Fact]
    public void AnElementTargetIsWrittenOnceWithItsIndexCall()
    {
        var holder = HandBuiltModel.Class("Holder");
        var indexer = new Indexer(Integer, [new Parameter("position", Integer)], default) { DeclaringType = holder };
        var next = new Method("Next", Integer, [], new MemberModifiers { IsStatic = true }) { DeclaringType = holder };
        var position = new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), next), [], Integer, next, []);
        var element = new IndexExpression(HandBuiltModel.Node(), Ref(new LocalVariable("items", holder)), position, Integer) { Indexer = indexer };

        var emitted = Emit(Compound(element, BinaryOperatorKind.Add, HandBuiltModel.Literal(Integer, 1L), IncrementForm.None));

        Assert.EndsWith("+= 1", emitted, StringComparison.Ordinal);
        Assert.Equal(1, emitted.Split("Next(").Length - 1);
    }

    [Fact]
    public void AGuardedRemainderMapsADivisorOfMinusOneToOne()
    {
        var total = new LocalVariable("total", Integer);
        var divisor = new LocalVariable("divisor", Integer);
        var emitted = Emit(new CompoundAssignmentExpression(HandBuiltModel.Node(), Ref(total), BinaryOperatorKind.Modulo, Ref(divisor), Integer, null, IncrementForm.None)
        {
            GuardsRemainder = true,
        });

        Assert.Equal("total %= (divisor switch { -1 => 1, var __e1 => __e1 })", emitted);
    }

    [Fact]
    public void AppendedNumbersAreFormattedInvariantly()
    {
        var text = model.Type("String");
        var line = new LocalVariable("line", text);

        var emitted = Emit(new CompoundAssignmentExpression(HandBuiltModel.Node(), Ref(line), BinaryOperatorKind.Add,
            HandBuiltModel.Literal(model.Type("Double"), 1.5), text, null, IncrementForm.None));

        Assert.StartsWith("line += ", emitted, StringComparison.Ordinal);
        Assert.Contains("InvariantCulture", emitted, StringComparison.Ordinal);
    }

    private static CompoundAssignmentExpression Compound(Expression target, BinaryOperatorKind kind, Expression value, IncrementForm form)
    {
        return new CompoundAssignmentExpression(HandBuiltModel.Node(), target, kind, value, target.Type, null, form);
    }

    private static LocalReferenceExpression Ref(LocalVariable local)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(), local);
    }

    private static string Emit(Expression expression)
    {
        BodyEmitter.ResetTemporaries();
        return BodyEmitter.Render(expression, new DiagnosticBag());
    }
}