using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
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
        n = new LocalVariable("n", slots.Refinements.Between(slots.Int, 11L, 19L, "Small"));
        m = new LocalVariable("m", slots.Refinements.Between(slots.Int, 11L, 29L, "Range"));
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
    public void ARefusedSlotReadIsSpelledByTheSlotsDomain()
    {
        var length = slots.Array.TypeParametersList[1];
        var literal = HandBuiltRefinements.Local(slots.ArrayOf(slots.Literal(4)), "four");
        var captured = HandBuiltRefinements.Local(slots.ArrayOf(slots.Capture(n).Value), "a");
        var ofLiteral = new MemberAccessExpression(HandBuiltModel.Node(), literal, "Length", slots.Literal(4), member: new TypeResult(length));
        var ofCapture = new MemberAccessExpression(HandBuiltModel.Node(), captured, "Length", Assert.IsType<ConstructedType>(captured.Type).TypeArguments[1], member: new TypeResult(length));

        var refused = slots.Refinements.Built.Analyze(new AssignabilityAnalyzer(), HandBuiltModel.Block(
            new LocalDeclarationStatement(new LocalVariable("s", slots.Refinements.String), ofLiteral),
            new LocalDeclarationStatement(new LocalVariable("t", slots.Refinements.String), ofCapture)));

        Assert.Equal(2, refused.Count);
        Assert.All(refused, diagnostic =>
        {
            Assert.Equal("MIST0157", diagnostic.Number.Value);
            Assert.StartsWith("Cannot implicitly convert type 'int' to 'string'", diagnostic.Message, StringComparison.Ordinal);
        });
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
        var (a, declared) = Created(m);

        var refused = Assert.Single(Analyze(declared, Write(m, n), Typed(m, a)));

        Assert.Equal("MIST0506", refused.Number.Value);
        Assert.Contains("'int[m]' is not proven to be 'int[m]': 'm' is not proven to lie in 'int >= 11 and <= 19' (fails when m >= 20)", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnArrayOutsideTheDeclaredRefinementIsRefusedAndOneInsideItIsAdmitted()
    {
        ExpressionStatement write(LocalVariable target, LocalVariable length)
        {
            return new ExpressionStatement(HandBuiltRefinements.Assign(HandBuiltRefinements.Read(target), slots.Create(slots.Capture(length))));
        }

        var (a, declared) = Created(n);
        var (wide, declaredWide) = Created(m, "wide");

        var refused = Assert.Single(Analyze(declared, write(a, m)));

        Assert.Equal("MIST0506", refused.Number.Value);
        Assert.Contains("'m' is not proven to lie in 'int >= 11 and <= 19' (fails when m >= 20)", refused.Message, StringComparison.Ordinal);
        Assert.Empty(Analyze(declaredWide, write(wide, n)));
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
    public void ACaptureMadeInALoopBodyKeepsTheRefinementOfItsIterationAndRefusesAWiderArray()
    {
        var (a, declared) = Created(m);
        var body = HandBuiltModel.Block(Typed(n, a), Write(n, n));

        var refused = Analyze(declared, new WhileStatement(slots.Refinements.Binary(HandBuiltRefinements.Read(n), BinaryOperatorKind.LessThan, slots.Refinements.Literal(15L)), body));

        Assert.Equal("MIST0506", Assert.Single(refused).Number.Value);
    }
}