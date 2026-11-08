using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
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

    [Fact]
    public void ACarriedOperatorBindsTheReceiverAndIndexOnceAndReadsAndWritesThroughThem()
    {
        var number = HandBuiltModel.Class("Number");
        number.Modifiers = new MemberModifiers { IsIntrinsic = true };
        var plus = new Operator(OperatorKind.Plus, number, [new Parameter("left", number), new Parameter("right", Integer)], new MemberModifiers { IsStatic = true }) { DeclaringType = number };
        var holder = HandBuiltModel.Class("Holder");
        var indexer = new Indexer(number, [new Parameter("position", Integer)], default) { DeclaringType = holder };
        var element = new IndexExpression(HandBuiltModel.Node(), Call(holder, "Make", holder), Call(holder, "Next", Integer), number) { Indexer = indexer };

        var emitted = Emit(new CompoundAssignmentExpression(HandBuiltModel.Node(), element, BinaryOperatorKind.Add, HandBuiltModel.Literal(Integer, 5L), number, plus, IncrementForm.None));

        Assert.Equal(1, emitted.Split("Make(").Length - 1);
        Assert.Equal(1, emitted.Split("Next(").Length - 1);
        Assert.EndsWith(") switch { (var __e1, var __e2) => __e1[__e2] = MistIntrinsics.Number_op_Addition(__e1[__e2], 5) })", emitted, StringComparison.Ordinal);
        Assert.StartsWith("_ = ((", EmitStatement(new CompoundAssignmentExpression(HandBuiltModel.Node(), element, BinaryOperatorKind.Add,
            HandBuiltModel.Literal(Integer, 5L), number, plus, IncrementForm.None)), StringComparison.Ordinal);
    }

    [Fact]
    public void ACarriedPostfixIncrementYieldsTheValueBeforeTheWrite()
    {
        var number = HandBuiltModel.Class("Number");
        number.Modifiers = new MemberModifiers { IsIntrinsic = true };
        var plus = new Operator(OperatorKind.Plus, number, [new Parameter("left", number), new Parameter("right", Integer)], new MemberModifiers { IsStatic = true }) { DeclaringType = number };
        var total = new LocalVariable("total", number);

        var emitted = Emit(new CompoundAssignmentExpression(HandBuiltModel.Node(), Ref(total), BinaryOperatorKind.Add, HandBuiltModel.Literal(Integer, 1L), number, plus, IncrementForm.Postfix));

        Assert.Equal("(total switch { var __e1 => (total = MistIntrinsics.Number_op_Addition(__e1, 1), __e1).Item2 })", emitted);
    }

    [Theory]
    [InlineData("None", "((i + 1) switch { var __e1 => this[__e1] = MistIntrinsics.Number_op_Addition(this[__e1], 5) })")]
    [InlineData("Postfix", "((i + 1) switch { var __e1 => (this[__e1] switch { var __e2 => (this[__e1] = MistIntrinsics.Number_op_Addition(__e2, 5), __e2).Item2 }) })")]
    public void ACarriedOperatorBindingOnlyItsIndexParenthesizesTheIndexItSwitchesOn(string increment, string expected)
    {
        var form = Enum.Parse<IncrementForm>(increment);
        var number = HandBuiltModel.Class("Number");
        number.Modifiers = new MemberModifiers { IsIntrinsic = true };
        var plus = new Operator(OperatorKind.Plus, number, [new Parameter("left", number), new Parameter("right", Integer)], new MemberModifiers { IsStatic = true }) { DeclaringType = number };
        var holder = HandBuiltModel.Class("Holder");
        var indexer = new Indexer(number, [new Parameter("position", Integer)], default) { DeclaringType = holder };
        var index = new BinaryExpression(HandBuiltModel.Node(), Ref(new LocalVariable("i", Integer)), BinaryOperatorKind.Add, HandBuiltModel.Literal(Integer, 1L), Integer);
        var element = new IndexExpression(HandBuiltModel.Node(), new ThisExpression(HandBuiltModel.Node(), holder), index, number) { Indexer = indexer };

        var emitted = Emit(new CompoundAssignmentExpression(HandBuiltModel.Node(), element, BinaryOperatorKind.Add, HandBuiltModel.Literal(Integer, 5L), number, plus, form));

        Assert.Equal(expected, emitted);
    }

    [Theory]
    [InlineData("None", "__o + (2), false)")]
    [InlineData("Postfix", "__o + (1), true)")]
    public void APropertyAConversionReachesThroughAccessorsIsReadAndWrittenThroughBoth(string increment, string ending)
    {
        var form = Enum.Parse<IncrementForm>(increment);
        var cat = HandBuiltModel.Class("Cat");
        var value = new Property("Value", Integer, new MemberModifiers { Accessibility = Accessibility.Private })
        {
            DeclaringType = cat,
            Getter = new PropertyAccessor { Body = HandBuiltModel.Block() },
            Setter = new PropertyAccessor { Body = HandBuiltModel.Block() },
        };
        var target = new PropertyReferenceExpression(HandBuiltModel.Node(), value);

        var emitted = Reached(cat, Compound(target, BinaryOperatorKind.Add, HandBuiltModel.Literal(Integer, form == IncrementForm.None ? 2L : 1L), form));

        Assert.Contains("_get_Value(__t)", emitted, StringComparison.Ordinal);
        Assert.Contains("_set_Value(__t, __v)", emitted, StringComparison.Ordinal);
        Assert.EndsWith(ending, emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIndexerAConversionReachesThroughAccessorsEvaluatesItsIndexOnce()
    {
        var cat = HandBuiltModel.Class("Cat");
        var indexer = new Indexer(Integer, [new Parameter("position", Integer)], new MemberModifiers { Accessibility = Accessibility.Private }) { DeclaringType = cat };
        var element = new IndexExpression(HandBuiltModel.Node(), Ref(new LocalVariable("cat", cat)), Call(cat, "Next", Integer), Integer) { Indexer = indexer };

        var emitted = Reached(cat, Compound(element, BinaryOperatorKind.Multiply, HandBuiltModel.Literal(Integer, 3L), IncrementForm.None));

        Assert.Equal(1, emitted.Split("Next(").Length - 1);
        Assert.Contains("get_Item(__t, __k)", emitted, StringComparison.Ordinal);
        Assert.Contains("set_Item(__t, __k, __v)", emitted, StringComparison.Ordinal);
        Assert.EndsWith("__o => __o * (3), false)", emitted, StringComparison.Ordinal);
    }

    private static InvocationExpression Call(ClassType owner, string name, IMistType returns)
    {
        var method = new Method(name, returns, [], new MemberModifiers { IsStatic = true }) { DeclaringType = owner };
        return new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), method), [], returns, method, []);
    }

    private static string Reached(ClassType source, Expression expression)
    {
        var copy = HandBuiltModel.Class("Copy");
        var conversion = HandBuiltModel.Conversion(copy, source, copy);
        var diagnostics = new DiagnosticBag();

        using(UnsafeReach.Enter(copy, conversion, "__Out", diagnostics))
        {
            BodyEmitter.ResetTemporaries();
            var emitted = BodyEmitter.Render(expression, diagnostics);
            Assert.Empty(diagnostics.ToImmutableArray());
            return emitted;
        }
    }

    private static string EmitStatement(Expression expression)
    {
        BodyEmitter.ResetTemporaries();
        var writer = new CSharpWriter();
        BodyEmitter.EmitBlock(HandBuiltModel.Block(new ExpressionStatement(expression)), writer, new DiagnosticBag());
        return writer.ToString().Trim();
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