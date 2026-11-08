using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class CompoundAssignmentAnalysisTests
{
    private readonly HandBuiltModel model = new();

    private IMistType Integer => model.Type("Int32");

    [Fact]
    public void DefiniteAssignmentRefusesACompoundWriteOfAnUnassignedLocal()
    {
        var total = new LocalVariable("total", Integer);

        var findings = model.Analyze(new DefiniteAssignmentAnalyzer(), HandBuiltModel.Block(
            new LocalDeclarationStatement(total, null), Run(Add(Ref(total, 37), 1))));

        var finding = Assert.Single(findings);
        Assert.Equal("MIST0220", finding.Number.Value);
        Assert.Equal(38, finding.Position().Start.Column);
    }

    [Fact]
    public void DefiniteAssignmentAdmitsACompoundWriteAfterAssignmentAndCountsItAsAWrite()
    {
        var total = new LocalVariable("total", Integer);
        var counted = new LocalVariable("counted", Integer);

        var findings = model.Analyze(new DefiniteAssignmentAnalyzer(), HandBuiltModel.Block(
            new LocalDeclarationStatement(total, HandBuiltModel.Literal(Integer, 0L)),
            Run(Add(Ref(total), 1)),
            new LocalDeclarationStatement(counted, Ref(total))));

        Assert.Empty(findings);
    }

    [Theory]
    [InlineData("struct")]
    [InlineData("class")]
    public void OwnershipJudgesACompoundMemberWriteOfAMovedBindingAsAWrite(string shape)
    {
        var f = new LifetimeFragments();
        var type = f.DisciplinedValue(shape, SubstructuralDiscipline.Linear);
        var source = f.Local("source", type);
        var destination = f.Local("destination", type);
        var write = new CompoundAssignmentExpression(HandBuiltModel.Node(37), f.Count(source), BinaryOperatorKind.Add,
            HandBuiltModel.Literal(f.Int32, 2L), f.Int32, null, IncrementForm.None);

        var diagnostic = Assert.Single(f.AnalyzeMoves(HandBuiltModel.Block(
            LifetimeFragments.Declare(source, LifetimeFragments.Fresh(type)),
            LifetimeFragments.Declare(destination, LifetimeFragments.Ref(source)),
            LifetimeFragments.Run(write))));

        Assert.Equal("MIST0227", diagnostic.Number.Value);
        Assert.Equal(38, diagnostic.Position().Start.Column);
    }

    [Theory]
    [InlineData(OwnershipKind.Owned, "MIST0264")]
    [InlineData(OwnershipKind.Unrestricted, null)]
    public void AnOwnedBindingRefusesASharedOperatorResultAsTheLongFormDoes(OwnershipKind ownership, string? expected)
    {
        var box = HandBuiltModel.Class("Box");
        var constructor = new Constructor(box.Name, box, [], default) { DeclaringType = box, Body = HandBuiltModel.Block() };
        box.ConstructorsList.Add(constructor);
        var plus = new Operator(OperatorKind.Plus, box, [new Parameter("left", box), new Parameter("right", Integer)], new MemberModifiers { IsStatic = true }) { DeclaringType = box };
        var held = new LocalVariable("held", box) { Ownership = ownership };

        IReadOnlyList<string> findings(Expression write)
        {
            return model.Analyze(new OwnershipAnalyzer(), HandBuiltModel.Block(
                new LocalDeclarationStatement(held, new NewExpression(HandBuiltModel.Node(), box, [], constructor, [], [])),
                Run(write))).Select(finding => finding.Number.Value).ToList();
        }

        var shortForm = findings(new CompoundAssignmentExpression(HandBuiltModel.Node(37), Ref(held), BinaryOperatorKind.Add,
            HandBuiltModel.Literal(Integer, 1L), box, plus, IncrementForm.None));
        var longForm = findings(new AssignmentExpression(HandBuiltModel.Node(), Ref(held),
            new BinaryExpression(HandBuiltModel.Node(37), Ref(held), BinaryOperatorKind.Add, HandBuiltModel.Literal(Integer, 1L), box) { Operator = plus }, box));

        Assert.Equal(expected is null ? [] : [expected], shortForm);
        Assert.Equal(longForm, shortForm);
    }

    [Fact]
    public void AReadonlyMethodRefusesACompoundFieldWriteAsItRefusesTheAssignment()
    {
        var owner = HandBuiltModel.Class("Counter");
        var field = new Field("Count", Integer, default, default) { DeclaringType = owner };
        owner.FieldsList.Add(field);
        var target = new MemberAccessExpression(HandBuiltModel.Node(37), new ThisExpression(HandBuiltModel.Node(), owner), field.Name, Integer,
            member: new FieldResult(field, owner, []));

        var compound = Readonly(owner, new CompoundAssignmentExpression(HandBuiltModel.Node(37), target, BinaryOperatorKind.Multiply,
            HandBuiltModel.Literal(Integer, 2L), Integer, null, IncrementForm.None));
        var assignment = Readonly(owner, new AssignmentExpression(HandBuiltModel.Node(37), target, HandBuiltModel.Literal(Integer, 2L), Integer));

        Assert.NotEmpty(assignment);
        Assert.Equal(assignment.Select(finding => finding.Number.Value), compound.Select(finding => finding.Number.Value));
    }

    [Fact]
    public void AReadonlyLocalRefusesAnIncrement()
    {
        var fixedCount = new LocalVariable("fixedCount", Integer) { IsReadonly = true };

        var findings = model.Analyze(new PositionAnalyzer(), HandBuiltModel.Block(
            new LocalDeclarationStatement(fixedCount, HandBuiltModel.Literal(Integer, 0L)),
            Run(new CompoundAssignmentExpression(HandBuiltModel.Node(37), Ref(fixedCount), BinaryOperatorKind.Add, HandBuiltModel.Literal(Integer, 1L),
                Integer, null, IncrementForm.Postfix))));

        _ = Assert.Single(findings);
    }

    [Fact]
    public void AResultTheTargetCannotHoldIsRefusedAndOneItCanHoldIsNot()
    {
        var money = HandBuiltModel.Struct("Money");
        var wallet = new LocalVariable("wallet", money);
        var refused = new CompoundAssignmentExpression(HandBuiltModel.Node(37), Ref(wallet), BinaryOperatorKind.Divide,
            HandBuiltModel.Literal(Integer, 2L), money, null, IncrementForm.None)
        { ResultType = model.Type("String") };
        var admitted = new CompoundAssignmentExpression(HandBuiltModel.Node(37), Ref(wallet), BinaryOperatorKind.Add,
            HandBuiltModel.Literal(Integer, 2L), money, null, IncrementForm.None);

        var finding = Assert.Single(model.Analyze(new AssignabilityAnalyzer(), HandBuiltModel.Block(Run(refused))));
        Assert.Equal("MIST0157", finding.Number.Value);
        Assert.Empty(model.Analyze(new AssignabilityAnalyzer(), HandBuiltModel.Block(Run(admitted))));
    }

    [Fact]
    public void IncrementingTextIsRefusedByTheOperatorRule()
    {
        var text = model.Type("String");
        var line = new LocalVariable("line", text);
        var increment = new CompoundAssignmentExpression(HandBuiltModel.Node(37), Ref(line), BinaryOperatorKind.Add,
            HandBuiltModel.Literal(Integer, 1L), text, null, IncrementForm.Postfix)
        { ResultType = ErrorType.Instance };

        var finding = Assert.Single(model.Analyze(new OperatorAnalyzer(), HandBuiltModel.Block(Run(increment))));

        Assert.Equal("MIST0200", finding.Number.Value);
        Assert.Contains("'++'", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("i++", 4L)]
    [InlineData("++i", 4L)]
    [InlineData("i += 1", 4L)]
    [InlineData("i--", 2L)]
    [InlineData("i -= 1", 2L)]
    public void RefinementFlowKnowsWhatItKnowsOfTheLongForm(string spelling, long expected)
    {
        var i = new LocalVariable("i", Integer);
        var kind = spelling.Contains('-', StringComparison.Ordinal) ? BinaryOperatorKind.Subtract : BinaryOperatorKind.Add;
        var form = spelling switch
        {
            "i++" or "i--" => IncrementForm.Postfix,
            "++i" => IncrementForm.Prefix,
            _ => IncrementForm.None,
        };
        var compound = new CompoundAssignmentExpression(HandBuiltModel.Node(), Ref(i), kind, HandBuiltModel.Literal(Integer, 1L), Integer, null, form);
        var longForm = new AssignmentExpression(HandBuiltModel.Node(), Ref(i),
            new BinaryExpression(HandBuiltModel.Node(), Ref(i), kind, HandBuiltModel.Literal(Integer, 1L), Integer), Integer);

        var short_ = Known(i, compound);
        var long_ = Known(i, longForm);

        Assert.Equal(long_.Values, short_.Values);
        Assert.Equal(Admitted.Exactly(expected), short_.Values);
    }

    [Fact]
    public void TheFlowEvaluatesTheTargetsPartsThenTheValueThenTheNodeOnce()
    {
        var holder = HandBuiltModel.Class("Holder");
        var indexer = new Indexer(Integer, [new Parameter("position", Integer)], default) { DeclaringType = holder };
        var next = new Method("Next", Integer, [], new MemberModifiers { IsStatic = true });
        var items = new LocalVariable("items", holder);
        var receiver = Ref(items);
        var position = new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), next, null), [], Integer, next, []);
        var value = HandBuiltModel.Literal(Integer, 1L);
        var compound = new CompoundAssignmentExpression(HandBuiltModel.Node(),
            new IndexExpression(HandBuiltModel.Node(), receiver, position, Integer) { Indexer = indexer }, BinaryOperatorKind.Add, value, Integer, null, IncrementForm.None);

        var evaluated = FlowGraphBuilder.Build(HandBuiltModel.Block(Run(compound))).Blocks
            .SelectMany(block => block.Steps).Where(step => step.Kind == FlowStepKind.Evaluate).Select(step => step.Node).ToList();

        var order = new object[] { receiver, position, value, compound }.Select(node => evaluated.FindIndex(step => ReferenceEquals(step, node))).ToList();
        Assert.DoesNotContain(-1, order);
        Assert.Equal(order.Order(), order);
        _ = Assert.Single(evaluated, node => ReferenceEquals(node, compound));
        Assert.DoesNotContain(evaluated, node => ReferenceEquals(node, compound.Target));
    }

    private Knowledge Known(LocalVariable local, Expression write)
    {
        var body = HandBuiltModel.Block(new LocalDeclarationStatement(local, HandBuiltModel.Literal(Integer, 3L)), Run(write));
        var solution = FlowSolution<FactState>.Solve(
            FlowGraphBuilder.Build(body), new FactFlow(UnseenWrites.Of(body), new LiftedReturns()), FactFlow.Nothing);
        var known = Knowledge.Unknown;
        solution.Replay((state, step) =>
        {
            if(step.Kind == FlowStepKind.Leave && ReferenceEquals(step.Node, body))
            {
                Assert.True(state.TryGetValue(local, out known));
            }
        });
        return known;
    }

    private IReadOnlyList<Diagnostic> Readonly(ConcreteType owner, Expression write)
    {
        var method = new Method("Peek", Integer, [], new MemberModifiers { IsReadonly = true })
        {
            Body = HandBuiltModel.Block(Run(write)),
            DeclaringType = owner,
        };
        return model.Analyze(new ReadonlyAnalyzer(), method.Body, owner: owner, member: method);
    }

    private CompoundAssignmentExpression Add(Expression target, long by)
    {
        return new CompoundAssignmentExpression(HandBuiltModel.Node(), target, BinaryOperatorKind.Add, HandBuiltModel.Literal(Integer, by), Integer, null, IncrementForm.None);
    }

    private static LocalReferenceExpression Ref(LocalVariable local, int offset = 0)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(offset), local);
    }

    private static ExpressionStatement Run(Expression expression)
    {
        return new ExpressionStatement(expression);
    }
}

[Trait("Tier", "1")]
public sealed class CompoundAssignmentDecisionTests
{
    private readonly HandBuiltModel model = new();

    [Theory]
    [InlineData("Int32", true)]
    [InlineData("Int64", true)]
    [InlineData("Double", true)]
    [InlineData("Byte", true)]
    [InlineData("String", false)]
    [InlineData("Boolean", false)]
    public void IncrementsAreDefinedOnTheIntegralAndRealFamiliesAlone(string type, bool admitted)
    {
        Assert.Equal(admitted, ProgramModel.Construction.Binding.Expressions.CompoundAssignmentBinder.Increments(model.Type(type)));
        Assert.Equal(admitted ? null : "MIST0200", OperatorDecision.Increment(model.Type(type), BinaryOperatorKind.Add, wellKnown => model.Type(wellKnown.Name))?.Number.Value);
    }
}