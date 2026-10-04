// Tier 3 (Component): admitted operators, legal expression positions and captures a static lambda
// may make of its own bindings, plus model reach facts. OperatorAnalyzer and PositionAnalyzer state
// these rules.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model.Expressions;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class OperatorDecisionTests : ProgramModelProbe
{
    private static readonly string[] Ids =
    [
        "MIST_PA_OPERATOR_NOT_APPLICABLE", "MIST_PA_UNKNOWN_HAS_NO_OPERATORS", "MIST_PA_UNKNOWN_HAS_NO_MEMBERS",
        "MIST_PA_CONDITIONAL_ACCESS_ON_NON_OPTIONAL", "MIST_PA_COALESCING_ASSIGNMENT_NOT_A_STATEMENT",
        "MIST_PA_READONLY_LOCAL_REASSIGNED", "MIST_PA_STATIC_LAMBDA_CAPTURE", "MIST_PA_OPTIONAL_OF_OPTIONAL",
    ];

    public OperatorDecisionTests()
        : base("MistOperator_")
    {
    }

    /// <summary>
    /// A declared unary operator answers for itself, as a binary one does: Money's `!` is a bool, so the
    /// expression is not typed as its Money operand.
    /// </summary>
    [Fact]
    public void AUserUnaryOperatorTypesTheExpressionByItsReturnType()
    {
        var negated = InitializerOf<UnaryExpression>("var empty = !new Money();");

        Assert.Equal("Standard.Boolean", negated.Type.FullyQualifiedName);
    }

    protected override string Fixtures { get; } = """
        public class Dog
        {
            public string Name = "d";
        }

        public struct Money
        {
            public int Cents = 0;

            public static Money operator +(Money left, Money right)
            {
                return new Money();
            }

            public static bool operator ==(Money left, Money right)
            {
                return left.Cents == right.Cents;
            }

            public static bool operator !=(Money left, Money right)
            {
                return left.Cents != right.Cents;
            }

            public static bool operator !(Money money)
            {
                return money.Cents == 0;
            }
        }

        public enum Color : byte { Red, Green }

        type AdultAge = int >= 18;

        public class Holder
        {
            public int f = 1;
            public static int s = 2;

            public func<int, int> Plain() { return x => x + f; }
            public func<int, int> Fixed() { return static x => x + s; }
        }
        """;

    protected override string Preamble { get; } = """
        int a = 1;
        long b = 2;
        double d = 1.5;
        string s = "a";
        bool p = true;
        Color color = Color.Red;
        Dog dog = new Dog();
        Money money = new Money();
        AdultAge adult = (AdultAge)20;
        int | void opt = void;
        Dog | void maybe = void;
        var box = new { X = 1 };
        var box2 = new { X = 2 };
        """;

    [Theory]
    [InlineData("bool r = a < b;")]
    [InlineData("bool r = a == d;")]
    [InlineData("bool r = s == \"b\";")]
    [InlineData("bool r = p && false;")]
    [InlineData("bool r = color == Color.Red;")]
    [InlineData("bool r = color < Color.Green;")]
    [InlineData("bool r = money == money;")]
    [InlineData("Money r = money + money;")]
    [InlineData("bool r = box == box2;")]
    [InlineData("int r = adult + 5;")]
    [InlineData("string r = s + 1;")]
    [InlineData("string r = 1 + s;")]
    [InlineData("long r = a * b;")]
    [InlineData("double r = a / d;")]
    public void AnOperatorTheLanguageAdmits_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("var n = maybe?.Name;")]
    [InlineData("opt ??= 1;")]
    [InlineData("int k = 1; k = 2;")]
    [InlineData("func<int, int> f = static x => { int y = x + 1; return y; };")]
    [InlineData("func<int, int> g = static x => { func<int, int> h = y => y + x; return h(1); };")]
    [InlineData("int captured = 1; func<int, int> f = x => x + captured;")]
    [InlineData("int? o = 1;")]
    public void APositionUsedAsIntended_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("bool r = nothing < 1;")]
    [InlineData("var n = nothing?.Name;")]
    public void AnOperandThatAlreadyFailed_IsNotReportedAgain(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("bool r = s < 1;")]
    [InlineData("var n = dog?.Name;")]
    [InlineData("int captured = 1; func<int, int> f = static x => x + captured;")]
    public void AFindingIsReportedOnce(string statement)
    {
        _ = Assert.Single(Analyse(statement));
    }

    // ── Harness ─────────────────────────────────────────────────────────

    private void AssertSilent(string statement)
    {
        Assert.Empty(Analyse(statement));
    }

    private List<Diagnostic> Analyse(string statement)
    {
        var model = BuildModel(statement, extraTypes: "", extraPreamble: "", out _);
        var findings = new DiagnosticBag();

        ProgramAnalysisRunner.Run(model, findings);

        return [.. findings.ToImmutableArray().Where(d => Ids.Contains(d.Id.Value))];
    }
}