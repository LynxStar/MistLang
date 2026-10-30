// Tier 3: literal proofs follow runtime precision and decoded values; unknown predicates remain refused.

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class LiteralPredicateDecisionTests : ProgramModelProbe
{
    public LiteralPredicateDecisionTests()
        : base("MistLiteralPredicate_")
    {
    }

    protected override string Fixtures { get; } = """
        type Positive = double > 0.0;
        type Unit = double >= 0.0 and <= 1.0;
        type Outside = double (< 0.0 or > 1.0);
        type NotZero = double not 0.0;
        type FloatUnit = float >= 0.0f and <= 1.0f;
        type Tenth = double <= 0.1;
        type FloatTenth = float <= 0.1;
        type DecimalUnit = decimal >= 0.0m and <= 1.0m;
        type Named = string "x";
        type Other = string not "x";
        type Escaped = string "a\n\"b\\c";
        type Letters = string `[a-z]+`;
        type Either = string `a|b`;
        type Digits = string `\d+`;
        type Quoted = string `a"b`;
        type Apostrophe = string "a'";
        type NotLetters = string not `[a-z]+`;
        type LetterOrDigit = string (`[a-z]+` or `\d+`);
        type IgnoreCase = string `(?i:i)`;
        type GlobalIgnoreCase = string `(?i)i`;
        type Slow = string `(a+)+b`;
        """;

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("Positive value = 2.5;")]
    [InlineData("Positive value = 3;")]
    [InlineData("Positive value = 3u;")]
    [InlineData("Positive value = 3ul;")]
    [InlineData("Positive value = 4294967296u;")]
    [InlineData("Unit value = 0.5;")]
    [InlineData("Unit value = 0.0;")]
    [InlineData("Unit value = 1.0;")]
    [InlineData("Outside value = -2.5;")]
    [InlineData("NotZero value = -2.5;")]
    [InlineData("FloatUnit value = 0.5f;")]
    [InlineData("DecimalUnit value = 0.5m;")]
    [InlineData("Named value = \"x\";")]
    [InlineData("Other value = \"y\";")]
    [InlineData("Escaped value = \"a\\n\\\"b\\\\c\";")]
    [InlineData("Letters value = \"abc\";")]
    [InlineData("Either value = \"a\";")]
    [InlineData("Either value = \"b\";")]
    [InlineData("Digits value = \"12\";")]
    [InlineData("Quoted value = \"a\\\"b\";")]
    [InlineData("double number = 2.5; var copy = number; Positive value = copy;")]
    [InlineData("float number = 0.5f; double copy = number; Unit value = copy;")]
    [InlineData("string text = \"abc\"; var copy = text; Letters value = copy;")]
    [InlineData("double number = 0.5; if(flag) { number = 0.5; } Unit value = number;")]
    [InlineData("string stored = \"a\\'\"; if(flag) { stored = \"a'\"; } Apostrophe value = stored;")]
    [InlineData("Positive first = 2.5; Positive value = first;")]
    [InlineData("Letters first = \"abc\"; Letters value = first;")]
    [InlineData("Positive Make() { return 2.5; } Positive value = Make();")]
    [InlineData("double Take(Positive p) { return p; } var value = Take(2.5);")]
    [InlineData("string Take(Letters p) { return p; } var value = Take(\"abc\");")]
    [InlineData("double Take(double p) where p > 0.0 { return p; } var value = Take(2.5);")]
    [InlineData("string Take(string p) where p `[a-z]+` { return p; } var value = Take(\"abc\");")]
    [InlineData("FloatTenth value = 0.05f;")]
    [InlineData("NotLetters value = \"123\";")]
    [InlineData("LetterOrDigit value = \"abc\";")]
    [InlineData("LetterOrDigit value = \"123\";")]
    [InlineData("string number = \"123\"; var copy = number; NotLetters value = copy;")]
    [InlineData("NotLetters first = \"123\"; NotLetters value = first;")]
    [InlineData("NotLetters Make() { return \"123\"; } NotLetters value = Make();")]
    [InlineData("string Take(LetterOrDigit p) { return p; } var value = Take(\"abc\");")]
    [InlineData("float Take(FloatTenth p) { return p; } var value = Take(0.05f);")]
    public void AConstantThatMeetsThePredicate_IsAdmitted(string statement)
    {
        Assert.Empty(Analyse(statement));
    }

    [Theory]
    [InlineData("Positive value = -2.5;", "MIST0271")]
    [InlineData("Positive first = 2.5; Positive value = -first;", "MIST0271")]
    [InlineData("Positive value = 0u;", "MIST0271")]
    [InlineData("Unit value = 1.5;", "MIST0271")]
    [InlineData("NotZero value = -0.0;", "MIST0271")]
    [InlineData("FloatUnit value = -0.5f;", "MIST0271")]
    [InlineData("DecimalUnit value = 1.5m;", "MIST0271")]
    [InlineData("Tenth value = 0.1f;", "MIST0271")]
    [InlineData("float number = 0.1f; double copy = number; Tenth value = copy;", "MIST0271")]
    [InlineData("Named value = \"anything\";", "MIST0271")]
    [InlineData("Other value = \"x\";", "MIST0271")]
    [InlineData("Escaped value = \"a\\\\n\\\"b\\\\c\";", "MIST0271")]
    [InlineData("Letters value = \"abc1\";", "MIST0271")]
    [InlineData("Letters value = \"abc\\n\";", "MIST0271")]
    [InlineData("Either value = \"ab\";", "MIST0271")]
    [InlineData("Digits value = \"x12\";", "MIST0271")]
    [InlineData("double number = 2.5; number = -1.0; Positive value = number;", "MIST0271")]
    [InlineData("Positive Make() { return -2.5; }", "MIST0271")]
    [InlineData("double Take(Positive p) { return p; } var value = Take(-2.5);", "MIST0271")]
    [InlineData("double Take(double p) where p > 0.0 { return p; } var value = Take(-2.5);", "MIST0276")]
    [InlineData("string Take(string p) where p `[a-z]+` { return p; } var value = Take(\"123\");", "MIST0276")]
    [InlineData("FloatTenth value = 0.1f;", "MIST0271")]
    [InlineData("NotLetters value = \"abc\";", "MIST0271")]
    [InlineData("LetterOrDigit value = \"abc1\";", "MIST0271")]
    [InlineData("NotLetters Make() { return \"abc\"; }", "MIST0271")]
    [InlineData("string Take(LetterOrDigit p) { return p; } var value = Take(\"abc1\");", "MIST0271")]
    [InlineData("float Take(FloatTenth p) { return p; } var value = Take(0.1f);", "MIST0271")]
    public void AConstantThatFailsThePredicate_IsRefusedAsViolated(string statement, string expected)
    {
        Assert.Equal(expected, Assert.Single(Analyse(statement)).Number.Value);
    }

    [Theory]
    [InlineData("Positive value = input;")]
    [InlineData("void Forward(Positive first) { Positive value = -first; }")]
    [InlineData("Letters value = inputText;")]
    [InlineData("double number = 0.5; if(flag) { number = -0.75; } Unit value = number;")]
    [InlineData("double number = 2.5; var write = () => { number = -1.0; }; write(); Positive value = number;")]
    [InlineData("IgnoreCase value = \"I\";")]
    [InlineData("GlobalIgnoreCase value = \"I\";")]
    [InlineData("if(input >= 0.0) { Positive value = input; }")]
    [InlineData("void Forward(Positive first) { Unit value = first; }")]
    [InlineData("double Special() { return input; } var number = Special(); Positive value = number;")]
    [InlineData("NotLetters value = inputText;")]
    [InlineData("LetterOrDigit value = inputText;")]
    public void AValueWithoutASoundLiteralProof_IsStillUnproven(string statement)
    {
        Assert.Equal("MIST0272", Assert.Single(Analyse(statement)).Number.Value);
    }

    [Fact]
    public void ARegexThatExceedsTheEvaluationBudget_RemainsUnproven()
    {
        var input = new string('a', 10000);
        Assert.Equal("MIST0272", Assert.Single(Analyse($"Slow value = \"{input}\";")).Number.Value);
    }

    private ImmutableArray<Diagnostic> Analyse(string statement)
    {
        var model = BuildModel("", $$"""
            public class Guarded
            {
                public static void Check(double input, string inputText, bool flag)
                {
                    {{statement}}
                }
            }
            """, "", out var buildDiagnostics);
        Assert.Empty(buildDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var guarded = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Guarded"]);
        var nodes = guarded.MethodsList.SelectMany(method => BodyWalker.DescendantsAndSelf(Assert.IsType<Block>(method.Body))).ToList();
        Assert.DoesNotContain(nodes.OfType<Expression>(), expression => expression.Type is ErrorType);

        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);
        return findings.ToImmutableArray();
    }
}