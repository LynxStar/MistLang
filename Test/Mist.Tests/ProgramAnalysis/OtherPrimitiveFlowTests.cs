using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Facts;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class OtherPrimitiveFlowTests
{
    [Theory]
    [InlineData(0.0, false, true)]
    [InlineData(-1.0, false, false)]
    [InlineData(0.0, true, true)]
    [InlineData(-1.0, true, false)]
    public void ADoubleRefinedAboveABoundMeetsANonNegativeParameter(double bound, bool negatedTwice, bool admitted)
    {
        var h = new HandBuiltRefinements();
        var x = Parameter(h.Refine(h.Double, ComparisonKind.GreaterThan, bound), "x");
        Expression value = negatedTwice ? Negated(h, Negated(h, x)) : x;
        var body = HandBuiltModel.Block(HandBuiltRefinements.Evaluate(h.Call(value, h.Refine(h.Double, ComparisonKind.GreaterThanOrEqual, 0.0))));

        AssertAdmitted(admitted, h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HalfOfABoundedDoubleIsRecordedAndUsed(bool bounded)
    {
        var h = new HandBuiltRefinements();
        var x = Parameter(bounded ? h.Between(h.Double, 1.0, 1000.0) : h.Refine(h.Double, ComparisonKind.GreaterThanOrEqual, 1.0), "x");
        var half = HandBuiltRefinements.Local(h.Double, "half");
        var body = HandBuiltModel.Block(HandBuiltRefinements.Give(half, h.Binary(x, BinaryOperatorKind.Divide, h.Literal(2.0), h.Double)),
            HandBuiltRefinements.Evaluate(h.Call(half, h.Refine(h.Double, ComparisonKind.LessThanOrEqual, 500.0))));

        AssertAdmitted(bounded, h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    [Fact]
    public void ExhaustingTheProofOfARealContractIsReportedAsExhaustion()
    {
        var h = new HandBuiltRefinements();
        var x = Parameter(h.Between(h.Double, 1.0, 1000.0), "x");
        var half = HandBuiltRefinements.Local(h.Double, "half");
        var body = HandBuiltModel.Block(HandBuiltRefinements.Give(half, h.Binary(x, BinaryOperatorKind.Divide, h.Literal(2.0), h.Double)),
            HandBuiltRefinements.Evaluate(h.Call(half, h.Refine(h.Double, ComparisonKind.LessThanOrEqual, 500.0))));

        AssertAdmitted(false, h.Built.Analyze(new RefinementAnalyzer(new Budget(0)), body), refused: "MIST0485");
    }

    [Theory]
    [InlineData("Single", 1.0, false)]
    [InlineData("Double", 1.0, false)]
    [InlineData("Decimal", 1.0, false)]
    [InlineData("Single", 0.0, true)]
    [InlineData("Double", 0.0, true)]
    [InlineData("Decimal", 0.0, true)]
    public void AWholeNumberCutoffOnARealIsReadAsARealBound(string type, double required, bool admitted)
    {
        var h = new HandBuiltRefinements();
        var real = h.Built.Type(type);
        object bound = type switch { "Decimal" => (decimal)required, "Single" => (float)required, _ => required };
        var x = Parameter(h.Refine(real, ComparisonKind.GreaterThan, 0), "x");
        var body = HandBuiltModel.Block(HandBuiltRefinements.Evaluate(h.Call(x, h.Refine(real, ComparisonKind.GreaterThanOrEqual, bound))));

        AssertAdmitted(admitted, h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    [Theory]
    [InlineData(1.0, true)]
    [InlineData(0.0, false)]
    public void ARealDeclaredBelowAnotherKeepsTheStrictRelation(double required, bool admitted)
    {
        var h = new HandBuiltRefinements();
        var y = Parameter(h.Refine(h.Double, ComparisonKind.Equal, 1.0), "y");
        var x = Parameter(h.Refine(h.Double, ComparisonKind.LessThan, y), "x");
        var kind = admitted ? ComparisonKind.LessThan : ComparisonKind.LessThanOrEqual;
        var body = HandBuiltModel.Block(HandBuiltRefinements.Evaluate(h.Call(x, h.Refine(h.Double, kind, required))));

        AssertAdmitted(admitted, h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AGuardedRealComparisonMeetsASymbolicRealContract(bool admitted)
    {
        var guard = admitted ? BinaryOperatorKind.LessThan : BinaryOperatorKind.LessThanOrEqual;
        var h = new HandBuiltRefinements();
        var y = Parameter(h.Double, "y");
        var x = Parameter(h.Double, "x");
        var body = HandBuiltModel.Block(HandBuiltRefinements.If(h.Binary(x, guard, y),
            HandBuiltRefinements.Evaluate(h.Call(x, h.Refine(h.Double, ComparisonKind.LessThan, y)))));

        AssertAdmitted(admitted, h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    [Theory]
    [InlineData(1.0, true)]
    [InlineData(-1.0, false)]
    public void ARealContractJoiningAWholeNumberCutoffAndASymbolIsProvenAsAReal(double low, bool admitted)
    {
        var h = new HandBuiltRefinements();
        var y = Parameter(h.Double, "y");
        var x = Parameter(h.Refine(h.Double, ComparisonKind.GreaterThanOrEqual, low), "x");
        var required = HandBuiltRefinements.RefinePattern(h.Double, new ConjunctionPattern(HandBuiltModel.Node(), h.Double,
            [h.Compare(h.Double, ComparisonKind.GreaterThanOrEqual, 0), h.Compare(h.Double, ComparisonKind.LessThan, y)]));
        var body = HandBuiltModel.Block(HandBuiltRefinements.If(h.Binary(x, BinaryOperatorKind.LessThan, y),
            HandBuiltRefinements.Evaluate(h.Call(x, required))));

        AssertAdmitted(admitted, h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    [Theory]
    [InlineData(-1.0, false)]
    [InlineData(0.0, true)]
    public void ARefusedRealContractNamesItsFailingCase(double bound, bool admitted)
    {
        var h = new HandBuiltRefinements();
        var x = Parameter(h.Refine(h.Double, ComparisonKind.GreaterThan, bound), "x");
        var body = HandBuiltModel.Block(HandBuiltRefinements.Evaluate(h.Call(x, h.Refine(h.Double, ComparisonKind.GreaterThanOrEqual, 0.0))));

        var findings = h.Built.Analyze(new RefinementAnalyzer(), body);

        AssertAdmitted(admitted, findings);
        if(!admitted)
        {
            Assert.True(findings[0].Message.Contains("(fails when x < 0)", StringComparison.Ordinal), findings[0].Message);
        }
    }

    [Theory]
    [InlineData("negate", 10, 11, "LessThanOrEqual", -10, true)]
    [InlineData("negate", 10, 11, "LessThan", -11, false)]
    [InlineData("add", 10, 11, "GreaterThan", 10, false)]
    [InlineData("subtract", 9, 10, "LessThan", 10, false)]
    [InlineData("multiply", 1, 2, "LessThan", 8, false)]
    public void DecimalArithmeticOtherThanNegationIsNeverRecorded(string operation, int low, int high, string kind, int bound, bool admitted)
    {
        var h = new HandBuiltRefinements();
        var real = h.Built.Type("Decimal");
        var x = Parameter(h.Between(real, (decimal)low, (decimal)high), "x");
        var y = HandBuiltRefinements.Local(real, "y");
        var tiny = h.Literal(0.0000000000000000000000000001m, real);
        var result = operation switch
        {
            "negate" => new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, x, real),
            "add" => h.Binary(x, BinaryOperatorKind.Add, tiny, real),
            "subtract" => h.Binary(x, BinaryOperatorKind.Subtract, tiny, real),
            _ => h.Binary(x, BinaryOperatorKind.Multiply, h.Literal(3m, real), real),
        };
        var body = HandBuiltModel.Block(HandBuiltRefinements.Give(y, result),
            HandBuiltRefinements.Evaluate(h.Call(y, h.Refine(real, Enum.Parse<ComparisonKind>(kind), (decimal)bound))));

        AssertAdmitted(admitted, h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    [Theory]
    [InlineData("abc", "def", true)]
    [InlineData("abc", "DEF", false)]
    public void StringsJoinedFromLiteralsMeetARegexEveryLiteralMatches(string first, string second, bool admitted)
    {
        var h = new HandBuiltRefinements();
        var flag = Parameter(h.Bool, "flag");
        var s = HandBuiltRefinements.Local(h.String, "s");
        var body = HandBuiltModel.Block(new LocalDeclarationStatement(s.Local, null),
            new IfStatement(flag, HandBuiltModel.Block(HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(s, h.Literal(first)))),
                HandBuiltModel.Block(HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(s, h.Literal(second))))),
            HandBuiltRefinements.Evaluate(h.Call(s, h.Regex("^[a-z]+$"))));

        AssertAdmitted(admitted, h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void SelectingAJoinedPathSelectsTheLiteralItSupplied(bool selected, bool admitted)
    {
        var h = new HandBuiltRefinements();
        var flag = Parameter(h.Bool, "flag");
        var s = HandBuiltRefinements.Local(h.String, "s");
        var body = HandBuiltModel.Block(new LocalDeclarationStatement(s.Local, null),
            new IfStatement(flag, HandBuiltModel.Block(HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(s, h.Literal("abc")))),
                HandBuiltModel.Block(HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(s, h.Literal("123"))))),
            HandBuiltRefinements.If(selected ? flag : h.Not(flag), HandBuiltRefinements.Evaluate(h.Call(s, h.Regex("^[a-z]+$")))));

        AssertAdmitted(admitted, h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void SelectingAJoinedPathSelectsTheMatchItNarrowed(bool selected, bool admitted)
    {
        var h = new HandBuiltRefinements();
        var flag = Parameter(h.Bool, "flag");
        var p = Parameter(h.String, "p");
        var test = new IsPatternExpression(HandBuiltModel.Node(), p, new RegexPattern(HandBuiltModel.Node(), h.String, "^[a-z]+$"), h.Bool);
        var body = HandBuiltModel.Block(
            new IfStatement(flag, HandBuiltModel.Block(new IfStatement(h.Not(test), HandBuiltModel.Block(new ReturnStatement(null, null)), null)), null),
            HandBuiltRefinements.If(selected ? flag : h.Not(flag), HandBuiltRefinements.Evaluate(h.Call(p, h.Regex("^[a-z]+$")))));

        AssertAdmitted(admitted, h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    [Theory]
    [InlineData("^[a-z]+$", true)]
    [InlineData("^[a-z]*$", false)]
    public void AStringJoinedFromALiteralAndANarrowedStringMeetsARegexBothProve(string tested, bool admitted)
    {
        var h = new HandBuiltRefinements();
        var p = Parameter(h.String, "p");
        var s = HandBuiltRefinements.Local(h.String, "s");
        var test = new IsPatternExpression(HandBuiltModel.Node(), p, new RegexPattern(HandBuiltModel.Node(), h.String, tested), h.Bool);
        var body = HandBuiltModel.Block(HandBuiltRefinements.Give(s, h.Literal("abc")),
            HandBuiltRefinements.If(test, HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(s, p))),
            HandBuiltRefinements.Evaluate(h.Call(s, h.Regex("^[a-z]+$"))));

        AssertAdmitted(admitted, h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    [Theory]
    [InlineData("^[a-z]+$", true)]
    [InlineData("^[a-z]*$", false)]
    public void AStringNarrowedByARegexMeetsARegexParameter(string tested, bool admitted)
    {
        var h = new HandBuiltRefinements();
        var s = Parameter(h.String, "s");
        var test = new IsPatternExpression(HandBuiltModel.Node(), s, new RegexPattern(HandBuiltModel.Node(), h.String, tested), h.Bool);
        var body = HandBuiltModel.Block(HandBuiltRefinements.If(test, HandBuiltRefinements.Evaluate(h.Call(s, h.Regex("^[a-z]+$")))));

        AssertAdmitted(admitted, h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    [Theory]
    [InlineData("abc", true)]
    [InlineData("ABC", false)]
    public void AStringEqualToALiteralMeetsARegexParameterTheLiteralMatches(string literal, bool admitted)
    {
        var h = new HandBuiltRefinements();
        var s = Parameter(h.String, "s");
        var body = HandBuiltModel.Block(HandBuiltRefinements.If(h.Binary(s, BinaryOperatorKind.Equal, h.Literal(literal)),
            HandBuiltRefinements.Evaluate(h.Call(s, h.Regex("^[a-z]+$")))));

        AssertAdmitted(admitted, h.Built.Analyze(new RefinementAnalyzer(), body), refused: "MIST0271");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void AStringEqualityKeptInABooleanProvesTheLiteralsMatch(bool selected, bool admitted)
    {
        var h = new HandBuiltRefinements();
        var s = Parameter(h.String, "s");
        var ok = HandBuiltRefinements.Local(h.Bool, "ok");
        var body = HandBuiltModel.Block(HandBuiltRefinements.Give(ok, h.Binary(s, BinaryOperatorKind.Equal, h.Literal("abc"))),
            HandBuiltRefinements.If(selected ? ok : h.Not(ok), HandBuiltRefinements.Evaluate(h.Call(s, h.Regex("^[a-z]+$")))));

        AssertAdmitted(admitted, h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void NotOkSelectsTheAlternativeWhereTheComparisonFailed(bool below, bool admitted)
    {
        var h = new HandBuiltRefinements();
        var i = HandBuiltRefinements.Local(h.Int, "i");
        var n = HandBuiltRefinements.Local(h.Int, "n");
        var ok = HandBuiltRefinements.Local(h.Bool, "ok");
        var required = h.Refine(h.Int, below ? ComparisonKind.LessThan : ComparisonKind.GreaterThanOrEqual, n);
        var body = HandBuiltModel.Block(HandBuiltRefinements.Give(ok, h.Binary(i, BinaryOperatorKind.LessThan, n)),
            HandBuiltRefinements.If(h.Not(ok), HandBuiltRefinements.Need(i, required)));

        AssertAdmitted(admitted, h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    [Theory]
    [InlineData("and", true)]
    [InlineData("or", false)]
    [InlineData("not-or", true)]
    [InlineData("not-and", false)]
    public void AndOrAndNotOverBooleanLocalsFollowTheirPaths(string shape, bool admitted)
    {
        var h = new HandBuiltRefinements();
        var i = HandBuiltRefinements.Local(h.Int, "i");
        var j = HandBuiltRefinements.Local(h.Int, "j");
        var n = HandBuiltRefinements.Local(h.Int, "n");
        var ok = HandBuiltRefinements.Local(h.Bool, "ok");
        var fine = HandBuiltRefinements.Local(h.Bool, "fine");
        var below = h.Refine(h.Int, ComparisonKind.LessThan, n);
        var atLeast = h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, n);
        var (condition, required) = shape switch
        {
            "and" => (h.And(ok, fine), below),
            "or" => (h.Binary(ok, BinaryOperatorKind.LogicalOr, fine), below),
            "not-or" => (h.Not(h.Binary(ok, BinaryOperatorKind.LogicalOr, fine)), atLeast),
            _ => (h.Not(h.And(ok, fine)), atLeast),
        };
        var body = HandBuiltModel.Block(HandBuiltRefinements.Give(ok, h.Binary(i, BinaryOperatorKind.LessThan, n)),
            HandBuiltRefinements.Give(fine, h.Binary(j, BinaryOperatorKind.LessThan, n)),
            HandBuiltRefinements.If(condition, HandBuiltRefinements.Need(j, required)));

        AssertAdmitted(admitted, h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    [Theory]
    [InlineData("Single", 0, true)]
    [InlineData("Single", -1, false)]
    [InlineData("Double", 0, true)]
    [InlineData("Double", 0.0, true)]
    [InlineData("Double", -1, false)]
    [InlineData("Double", -1.0, false)]
    [InlineData("Decimal", 0, true)]
    [InlineData("Decimal", -1, false)]
    public void ARealDeclaredWithACutoffAndASymbolicBoundKeepsTheCutoff(string kind, object cutoff, bool admitted)
    {
        var h = new HandBuiltRefinements();
        var real = h.Built.Type(kind);
        object zero = kind switch { "Single" => 0f, "Decimal" => 0m, _ => 0.0 };
        var y = Parameter(real, "y");
        var x = Parameter(HandBuiltRefinements.RefinePattern(real, new ConjunctionPattern(HandBuiltModel.Node(), real,
            [h.Compare(real, ComparisonKind.GreaterThan, cutoff), h.Compare(real, ComparisonKind.LessThan, y)])), "x");
        var body = HandBuiltModel.Block(HandBuiltRefinements.Evaluate(h.Call(x, h.Refine(real, ComparisonKind.GreaterThanOrEqual, zero))));

        AssertAdmitted(admitted, h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    [Theory]
    [InlineData(0, true, true)]
    [InlineData(0.0, true, true)]
    [InlineData(0, false, false)]
    [InlineData(0.0, false, false)]
    public void AGuardedDoubleMeetsAConjunctionOfACutoffAndASymbolicBound(object cutoff, bool positive, bool admitted)
    {
        var h = new HandBuiltRefinements();
        var y = Parameter(h.Double, "y");
        var x = Parameter(h.Double, "x");
        var required = HandBuiltRefinements.RefinePattern(h.Double, new ConjunctionPattern(HandBuiltModel.Node(), h.Double,
            [h.Compare(h.Double, ComparisonKind.GreaterThan, cutoff), h.Compare(h.Double, ComparisonKind.LessThan, y)]));
        var below = h.Binary(x, BinaryOperatorKind.LessThan, y);
        var guard = positive ? h.And(h.Binary(x, BinaryOperatorKind.GreaterThan, h.Literal(0.0)), below) : below;
        var body = HandBuiltModel.Block(HandBuiltRefinements.If(guard, HandBuiltRefinements.Evaluate(h.Call(x, required))));

        AssertAdmitted(admitted, h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    [Theory]
    [InlineData("guard", "<=", true)]
    [InlineData("guard", ">=", true)]
    [InlineData("guard", "<", false)]
    [InlineData("remembered", "<=", true)]
    [InlineData("remembered", "<", false)]
    [InlineData("copy", "<=", false)]
    public void ASuccessfulDoubleEqualityOrdersItsOperandsButACopyDoesNot(string shape, string relation, bool admitted)
    {
        var h = new HandBuiltRefinements();
        var y = Parameter(h.Double, "y");
        var x = shape == "copy" ? HandBuiltRefinements.Local(h.Double, "x") : Parameter(h.Double, "x");
        var required = relation switch { "<=" => ComparisonKind.LessThanOrEqual, ">=" => ComparisonKind.GreaterThanOrEqual, _ => ComparisonKind.LessThan };
        var call = HandBuiltRefinements.Evaluate(h.Call(x, h.Refine(h.Double, required, y)));
        var ok = HandBuiltRefinements.Local(h.Bool, "ok");
        var equal = h.Binary(x, BinaryOperatorKind.Equal, y);
        var body = shape switch
        {
            "guard" => HandBuiltModel.Block(HandBuiltRefinements.If(equal, call)),
            "remembered" => HandBuiltModel.Block(HandBuiltRefinements.Give(ok, equal), HandBuiltRefinements.If(ok, call)),
            _ => HandBuiltModel.Block(HandBuiltRefinements.Give(x, y), call),
        };

        AssertAdmitted(admitted, h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    private static LocalReferenceExpression Parameter(IMistType type, string name)
    {
        return HandBuiltRefinements.Read(new LocalVariable(name, type) { Parameter = new Parameter(name, type) });
    }

    private static UnaryExpression Negated(HandBuiltRefinements h, Expression operand)
    {
        return new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, operand, h.Double);
    }

    private static void AssertAdmitted(bool admitted, IReadOnlyList<Diagnostic> findings, string refused = "MIST0272")
    {
        if(admitted)
        {
            Assert.Empty(findings);
        }
        else
        {
            Assert.Equal(refused, Assert.Single(findings).Number.Value);
        }
    }
}