using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.RuntimeDependentValues;

[Trait("Tier", "2")]
public sealed class SlotIdentityFlowTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly LocalVariable n;
    private readonly LocalVariable m;

    public SlotIdentityFlowTests()
    {
        n = new LocalVariable("n", slots.ZeroOrPositive);
        m = new LocalVariable("m", slots.ZeroOrPositive);
    }

    private IReadOnlyList<Diagnostic> Analyze(params Statement[] statements)
    {
        return slots.Refinements.Built.Analyze(new RefinementAnalyzer(), HandBuiltModel.Block(statements));
    }

    private (LocalVariable Local, LocalDeclarationStatement Declaration) Created(LocalVariable length, string name = "a")
    {
        var creation = slots.Create(slots.Capture(length));
        var local = new LocalVariable(name, creation.Type);
        return (local, new LocalDeclarationStatement(local, creation));
    }

    private LocalDeclarationStatement Typed(LocalVariable length, LocalVariable source)
    {
        var written = slots.Capture(length);
        return HandBuiltSlots.Declare(new LocalVariable("b", slots.ArrayOf(written.Value)), HandBuiltRefinements.Read(source), written);
    }

    private static ExpressionStatement Write(LocalVariable target, LocalVariable value)
    {
        return new ExpressionStatement(HandBuiltRefinements.Assign(HandBuiltRefinements.Read(target), HandBuiltRefinements.Read(value)));
    }

    [Fact]
    public void ATypeWrittenWithTheValueTheArrayWasCreatedWithIsThatArraysType()
    {
        var (a, declared) = Created(n);

        Assert.Empty(Analyze(declared, Typed(n, a)));
    }

    [Fact]
    public void AWriteToTheNamedVariableLeavesTheEarlierClosingBehind()
    {
        var (a, declared) = Created(n);

        var refused = Assert.Single(Analyze(declared, Write(n, m), Typed(n, a)));

        Assert.Equal("MIST0486", refused.Number.Value);
        Assert.Contains("'int[n]' is not proven to be 'int[n]': 'n' and 'n' are not proven identical", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnArrayOfAnotherLengthIsRefusedIntoTheDeclaredOne()
    {
        var (a, declared) = Created(n);
        var other = slots.Create(slots.Capture(m));
        var write = new ExpressionStatement(HandBuiltRefinements.Assign(HandBuiltRefinements.Read(a), other));

        var refused = Assert.Single(Analyze(declared, write));

        Assert.Equal("MIST0486", refused.Number.Value);
        Assert.Contains("'m' and 'n' are not proven identical", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnArrayOfTheSameUnchangedLengthIsAdmittedIntoTheDeclaredOne()
    {
        var (a, declared) = Created(n);
        var again = slots.Create(slots.Capture(n));

        Assert.Empty(Analyze(declared, new ExpressionStatement(HandBuiltRefinements.Assign(HandBuiltRefinements.Read(a), again))));
    }

    [Fact]
    public void ALengthOfTheSlotsContractIsAdmitted()
    {
        Assert.Empty(Analyze(Created(n).Declaration));
    }

    [Fact]
    public void APlainIntLengthOwesTheSlotsContract()
    {
        var plain = new LocalVariable("n", slots.Int);

        var refused = Assert.Single(Analyze(Created(plain).Declaration));

        Assert.Equal("MIST0272", refused.Number.Value);
        Assert.Contains("fails when n <= -1", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACaptureMadeInALoopBodyIsTheValueOfItsOwnIteration()
    {
        var (a, declared) = Created(n);
        var body = HandBuiltModel.Block(declared, Typed(n, a));

        Assert.Empty(Analyze(new WhileStatement(slots.Refinements.Binary(HandBuiltRefinements.Read(m), BinaryOperatorKind.LessThan, slots.Refinements.Literal(10L)), body)));
    }

    [Fact]
    public void ACaptureMadeInALoopBodyIsNotTheValueAnEarlierIterationLeft()
    {
        var (a, declared) = Created(n);
        var body = HandBuiltModel.Block(Typed(n, a), Write(n, m));

        var refused = Analyze(declared, new WhileStatement(slots.Refinements.Binary(HandBuiltRefinements.Read(m), BinaryOperatorKind.LessThan, slots.Refinements.Literal(10L)), body));

        Assert.Equal("MIST0486", Assert.Single(refused).Number.Value);
    }
}