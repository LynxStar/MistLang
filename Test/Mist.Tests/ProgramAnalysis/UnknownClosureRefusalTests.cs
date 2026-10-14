// Tier 1 decisions and Tier 2 report sites for the values a program cannot make of `unknown`
// (unknown-type Requirements 5.6, 5.7, 10.5) and the type test that narrows to a closure with it
// (Requirement 11.4), on types and trees built directly.

using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using Mist.Tests.Semantics;

namespace Mist.Tests.ProgramAnalysis;

public sealed class UnknownClosureRefusalTests
{
    private readonly HandBuiltModel built = new();

    private ClassType Int32 => built.Type("Int32");

    [Fact]
    [Trait("Tier", "1")]
    public void AConstructionClosedWithUnknownIsRefusedAndItsPositionsNamed()
    {
        var box = UnknownViewDecisionTests.Generic("Box<>", null, "T");
        var pair = UnknownViewDecisionTests.Generic("Pair<,>", null, "A", "B");
        var array = UnknownViewDecisionTests.Generic("Array<,>", null, "TValue", "Length");
        array.TypeParametersList[1].IsDependent = true;

        Assert.Equal("MIST_PA_UNKNOWN_CLOSURE_CONSTRUCTED", UnknownClosureAnalyzer.Judge(new ConstructedType(box, [UnknownType.Instance]))?.Id.Value);
        Assert.Equal("MIST_PA_UNKNOWN_CLOSURE_CONSTRUCTED", UnknownClosureAnalyzer.Judge(new ConstructedType(box, [new ConstructedType(box, [UnknownType.Instance])]))?.Id.Value);
        Assert.Null(UnknownClosureAnalyzer.Judge(new ConstructedType(box, [Int32])));
        Assert.Null(UnknownClosureAnalyzer.Judge(new ConstructedType(array, [Int32, UnknownType.Instance])));
        Assert.Null(UnknownClosureAnalyzer.Judge(box));
        Assert.Equal("'A' and 'B'", UnknownClosureAnalyzer.UnknownPositions(new ConstructedType(pair, [UnknownType.Instance, UnknownType.Instance])));
        Assert.Equal("'B'", UnknownClosureAnalyzer.UnknownPositions(new ConstructedType(pair, [Int32, UnknownType.Instance])));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void TheConstructionAndTheDefaultReportAtTheirExpressions()
    {
        var box = UnknownViewDecisionTests.Generic("Box<>", null, "T");
        var closed = new ConstructedType(box, [UnknownType.Instance]);
        var creation = new NewExpression(HandBuiltModel.Node(7), closed, [], constructor: null, initializer: [], parameterTypes: null);
        var defaulted = new DefaultExpression(HandBuiltModel.Node(9), UnknownType.Instance);
        var concrete = new NewExpression(HandBuiltModel.Node(11), new ConstructedType(box, [Int32]), [], constructor: null, initializer: [], parameterTypes: null);

        var reported = built.Analyze(new UnknownClosureAnalyzer(), HandBuiltModel.Block(
            new ExpressionStatement(creation), new ExpressionStatement(defaulted), new ExpressionStatement(concrete)));

        Assert.Equal(
            ["MIST_PA_UNKNOWN_CLOSURE_CONSTRUCTED", "MIST_PA_UNKNOWN_HAS_NO_DEFAULT"],
            reported.Select(diagnostic => diagnostic.Id.Value));
        Assert.Contains("'Box<unknown>' cannot be constructed: its type argument for 'T' is 'unknown'", reported[0].Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every <c>Box&lt;int&gt;</c> and every <c>Box&lt;unknown&gt;</c> already widens to <c>Box&lt;unknown&gt;</c>, so testing
    /// for it learns nothing; a bare <c>unknown</c> or an unrelated construction does not widen to it, so
    /// the test is a real question and the narrowing recovers full access.
    /// </summary>
    [Fact]
    [Trait("Tier", "1")]
    public void NarrowingToAClosureWithUnknownIsRefusedOnlyWhereItIsTautological()
    {
        var box = UnknownViewDecisionTests.Generic("Box<>", null, "T");
        var closed = new ConstructedType(box, [UnknownType.Instance]);
        var concrete = new ConstructedType(box, [Int32]);
        var toClosure = new TypeTestPattern(HandBuiltModel.Node(), closed, binding: null);
        var toConcrete = new TypeTestPattern(HandBuiltModel.Node(), concrete, binding: null);

        Assert.Equal("MIST_PA_PATTERN_NARROWS_TO_UNKNOWN_CLOSURE", PatternAnalyzer.TypeFinding(toClosure, concrete, built.Interner)?.Id.Value);
        Assert.Equal("MIST_PA_PATTERN_NARROWS_TO_UNKNOWN_CLOSURE", PatternAnalyzer.TypeFinding(toClosure, closed, built.Interner)?.Id.Value);
        Assert.Null(PatternAnalyzer.TypeFinding(toClosure, UnknownType.Instance, built.Interner));
        Assert.Null(PatternAnalyzer.TypeFinding(toConcrete, closed, built.Interner));
        Assert.Null(PatternAnalyzer.TypeFinding(toConcrete, concrete, built.Interner));
    }
}