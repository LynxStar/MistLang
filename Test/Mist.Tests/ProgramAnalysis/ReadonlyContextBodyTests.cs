using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class ReadonlyContextBodyTests
{
    private readonly HandBuiltModel model = new();
    private readonly ClassType counter = HandBuiltModel.Class("Counter");
    private readonly ClassType integer;
    private readonly Field count;

    public ReadonlyContextBodyTests()
    {
        integer = model.Type("Int32");
        count = new Field("Count", integer, default, default) { DeclaringType = counter };
        counter.FieldsList.Add(count);
    }

    [Theory]
    [InlineData(true, "Cannot assign to field 'field' within readonly member 'Level'.")]
    [InlineData(false, null)]
    public void OnlyTheSetterOfAReadonlyPropertyMayWriteItsBackingField(bool inGetter, string? message)
    {
        var getter = HandBuiltModel.Block();
        var setter = HandBuiltModel.Block();
        var level = new Property("Level", integer, new MemberModifiers { IsReadonly = true })
        {
            Getter = new PropertyAccessor { Body = getter },
            Setter = new PropertyAccessor { Body = setter },
            DeclaringType = counter,
        };
        getter.Statements = [Run(Write(new BackingFieldReferenceExpression(HandBuiltModel.Node(37), level)))];
        setter.Statements = [Run(Write(new BackingFieldReferenceExpression(HandBuiltModel.Node(37), level)))];

        var findings = model.Analyze(new ReadonlyAnalyzer(), inGetter ? getter : setter, owner: counter, member: level);

        Assert.Equal(message is null ? [] : [message], findings.Select(finding => finding.Message));
    }

    [Theory]
    [InlineData(true, "MIST0261: Cannot write through 'Slot' from readonly method 'Peek': it aliases storage outside what the context owns, and a readonly context writes nothing external.")]
    [InlineData(false, null)]
    public void AWriteThroughARefReturnedByACallIsRefusedOnlyInAReadonlyContext(bool isReadonly, string? expected)
    {
        var slot = new Method("Slot", integer, [], new MemberModifiers { IsRef = true, IsReadonly = true }) { DeclaringType = counter };
        var call = new InvocationExpression(
            HandBuiltModel.Node(37), new MemberAccessExpression(HandBuiltModel.Node(), new ThisExpression(HandBuiltModel.Node(), counter), slot.Name, integer), [], integer, slot, []);

        var findings = Analyze(isReadonly, Run(Write(call)));

        Assert.Equal(expected is null ? [] : [expected], findings.Select(finding => $"{finding.Number.Value}: {finding.Message}"));
    }

    [Theory]
    [InlineData(true, "MIST0259: Cannot set 'Level' from readonly method 'Peek': a readonly context calls no setter.")]
    [InlineData(false, null)]
    public void SettingAnExtensionPropertyIsRefusedLikeAnyOtherSetterInAReadonlyContext(bool isReadonly, string? expected)
    {
        var level = new Property("Level", integer, default)
        {
            Getter = new PropertyAccessor { IsAuto = true },
            Setter = new PropertyAccessor { IsAuto = true },
            DeclaringType = counter,
        };
        var set = new ExtensionPropertyAccessExpression(HandBuiltModel.Node(37), Handed(), level, "CounterExtensions", integer);

        var findings = Analyze(isReadonly, Run(Write(set)));

        Assert.Equal(expected is null ? [] : [expected], findings.Select(finding => $"{finding.Number.Value}: {finding.Message}"));
    }

    [Theory]
    [InlineData(false, "Cannot call 'operator +' from readonly method 'Peek': it is not readonly. A non-readonly call requires a local member on an owned receiver.")]
    [InlineData(true, null)]
    public void ACompoundAssignmentToALocalIsJudgedByTheOperatorItCalls(bool readonlyOperator, string? message)
    {
        var plus = new Operator(
            OperatorKind.Plus, counter, [new Parameter("a", counter), new Parameter("b", counter)], new MemberModifiers { IsStatic = true, IsReadonly = readonlyOperator })
        {
            Body = Mutates(),
            DeclaringType = counter,
        };
        var total = new LocalVariable("total", counter);
        var body = new Statement[]
        {
            new LocalDeclarationStatement(total, Handed()),
            Run(new CompoundAssignmentExpression(
                HandBuiltModel.Node(37), new LocalReferenceExpression(HandBuiltModel.Node(), total), BinaryOperatorKind.Add, Handed(), counter, plus, IncrementForm.None)),
        };

        var findings = Analyze(isReadonly: true, body);

        Assert.Equal(message is null ? [] : [message], findings.Select(finding => finding.Message));
    }

    [Theory]
    [InlineData(false, "Cannot call 'operator -' from readonly method 'Peek': it is not readonly. A non-readonly call requires a local member on an owned receiver.")]
    [InlineData(true, null)]
    public void AUnaryOperatorIsJudgedLikeTheMethodItIs(bool readonlyOperator, string? message)
    {
        var negate = new Operator(OperatorKind.Minus, counter, [new Parameter("a", counter)], new MemberModifiers { IsStatic = true, IsReadonly = readonlyOperator })
        {
            Body = Mutates(),
            DeclaringType = counter,
        };
        var negated = new UnaryExpression(HandBuiltModel.Node(37), UnaryOperatorKind.Negate, Handed(), counter) { Operator = negate };

        var findings = Analyze(isReadonly: true, new LocalDeclarationStatement(new LocalVariable("n", counter), negated));

        Assert.Equal(message is null ? [] : [message], findings.Select(finding => finding.Message));
    }

    [Theory]
    [InlineData(true, false, "Current")]
    [InlineData(false, false, "MoveNext")]
    [InlineData(false, true, "MoveNext")]
    [InlineData(true, true, null)]
    public void AForEachInAReadonlyContextNamesTheFirstMutableMemberItCalls(bool readonlyMoveNext, bool readonlyCurrent, string? named)
    {
        var cursor = HandBuiltModel.Class("Cursor");
        var position = new Field("position", integer, default, default) { DeclaringType = cursor };
        cursor.FieldsList.Add(position);
        var advances = HandBuiltModel.Block(Run(Write(new FieldReferenceExpression(HandBuiltModel.Node(), position))));
        var moveNext = new Method("MoveNext", integer, [], new MemberModifiers { IsReadonly = readonlyMoveNext }) { Body = advances, DeclaringType = cursor };
        var current = new Property("Current", integer, default)
        {
            Getter = readonlyCurrent ? new PropertyAccessor { IsAuto = true } : new PropertyAccessor { Body = advances },
            DeclaringType = cursor,
        };
        cursor.MethodsList.Add(moveNext);
        var getEnumerator = new Method("GetEnumerator", cursor, [], new MemberModifiers { IsReadonly = true }) { DeclaringType = counter };
        var loop = new ForEachStatement(new LocalVariable("item", integer), Handed(37), HandBuiltModel.Block())
        {
            GetEnumerator = getEnumerator,
            MoveNext = moveNext,
            Current = current,
        };

        var findings = Analyze(isReadonly: true, loop);

        Assert.Equal(
            named is null ? [] : [$"Cannot call '{named}' from readonly method 'Peek': it is not readonly. A non-readonly call requires a local member on an owned receiver."],
            findings.Select(finding => finding.Message));
    }

    [Theory]
    [InlineData(false, "Cannot call 'Length' from readonly method 'Peek': it is not readonly. A non-readonly call requires a local member on an owned receiver.")]
    [InlineData(true, null)]
    public void AFromEndIndexCallsTheCountGetterItCountsFrom(bool readonlyLength, string? message)
    {
        var length = new Property("Length", integer, default)
        {
            Getter = readonlyLength ? new PropertyAccessor { IsAuto = true } : new PropertyAccessor { Body = Mutates() },
            DeclaringType = counter,
        };
        var slots = new Indexer(integer, [new Parameter("index", integer)], default)
        {
            Getter = new PropertyAccessor { IsAuto = true },
            DeclaringType = counter,
        };
        var last = new IndexExpression(HandBuiltModel.Node(37), Handed(), HandBuiltModel.Literal(integer, 1L), integer)
        {
            Indexer = slots,
            FromEnd = new CountedIndexer(slots, integer, length),
        };

        var findings = Analyze(isReadonly: true, new LocalDeclarationStatement(new LocalVariable("last", integer), last));

        Assert.Equal(message is null ? [] : [message], findings.Select(finding => finding.Message));
    }

    private IReadOnlyList<Diagnostic> Analyze(bool isReadonly, params Statement[] statements)
    {
        var body = HandBuiltModel.Block(statements);
        var method = new Method("Peek", integer, [], new MemberModifiers { IsReadonly = isReadonly }) { Body = body, DeclaringType = counter };

        return model.Analyze(new ReadonlyAnalyzer(), body, owner: counter, member: method);
    }

    private LocalReferenceExpression Handed(int offset = 0)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(offset), new LocalVariable("c", counter) { Parameter = new Parameter("c", counter) });
    }

    private Block Mutates()
    {
        return HandBuiltModel.Block(Run(Write(new FieldReferenceExpression(HandBuiltModel.Node(), count))));
    }

    private AssignmentExpression Write(Expression target)
    {
        return new AssignmentExpression(HandBuiltModel.Node(37), target, HandBuiltModel.Literal(integer, 1L), integer);
    }

    private static ExpressionStatement Run(Expression expression)
    {
        return new ExpressionStatement(expression);
    }
}