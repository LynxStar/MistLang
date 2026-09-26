// Tier 3 (Component): what PatternAnalyzer deliberately does NOT report -- a test of an indeterminate
// input, of an unsealed class against an abstract, of a refinement, a comparison against a numeric or
// enum input, a legitimate widening arm, a catch-all that only the last arm is -- and the reach facts the
// corpus cannot phrase. WHY each is silent is recorded once, on order 8 of
// backlog/analysis-v2-capability-progression.yaml.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class PatternDecisionTests : ProgramModelProbe
{
    private static readonly string[] Ids =
    [
        "MIST_PA_PATTERN_NEVER_MATCHES", "MIST_PA_PATTERN_NARROWS_TO_UNKNOWN",
        "MIST_PA_PATTERN_NOT_COMPARABLE", "MIST_PA_PATTERN_CONSTANT_MISMATCH", "MIST_PA_PATTERN_BOUND_NOT_CONSTANT",
        "MIST_PA_SWITCH_ARM_UNREACHABLE",
    ];

    public PatternDecisionTests()
        : base("MistPattern_")
    {
    }

    protected override string Fixtures { get; } = """
        public abstract IShape
        {
            int Sides();
        }

        public class Animal
        {
        }

        public class Dog : Animal
        {
        }

        public class Cat : Animal
        {
        }

        public class Square : IShape
        {
            public int Sides() { return 4; }
        }

        public class Person
        {
            public string Name = "p";
            public int Age = 3;
        }

        public enum Color : byte { Red, Green }

        type AdultAge = int >= 18;

        public class Generic<T>
        {
            public int Kind(T value)
            {
                if(value is Dog d) { return 1; }
                return 0;
            }
        }
        """;

    protected override string Preamble { get; } = """
        Animal animal = new Dog();
        Dog dog = new Dog();
        IShape shape = new Square();
        Person person = new Person();
        unknown any = 1;
        int n = 5;
        long l = 5;
        Color color = Color.Red;
        int | string mixed = 1;
        (int, int) pair = (1, 2);
        var box = new { X = 1 };
        """;

    [Theory]
    [InlineData("if(animal is Dog d) { n = 1; }")]
    [InlineData("if(dog is Animal a) { n = 1; }")]
    [InlineData("if(dog is Dog same) { n = 1; }")]
    [InlineData("if(shape is Dog d) { n = 1; }")]
    [InlineData("if(dog is IShape sh) { n = 1; }")]
    [InlineData("if(any is Dog d) { n = 1; }")]
    [InlineData("if(animal is Dog { } d) { n = 1; }")]
    [InlineData("if(any is Person { Name: var name } person) { n = 1; }")]
    [InlineData("if(any is int i) { n = 1; }")]
    [InlineData("if(n is AdultAge age) { n = 1; }")]
    [InlineData("if(mixed is string s) { n = 1; }")]
    [InlineData("if(person is { Name: string name }) { n = 1; }")]
    [InlineData("if(person is { Age: > 2 }) { n = 1; }")]
    [InlineData("if(pair is (int a, int b)) { n = 1; }")]
    [InlineData("if(box is { X: > 3 }) { n = 1; }")]
    public void ATestSomeValueCouldPass_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("if(n is > 3 and < 10) { n = 1; }")]
    [InlineData("if(l is > 3) { n = 1; }")]
    [InlineData("if(n is 3) { n = 1; }")]
    [InlineData("if(l is 3) { n = 1; }")]
    [InlineData("if(color is Color.Red) { n = 1; }")]
    [InlineData("if(mixed is 3) { n = 1; }")]
    public void AComparisonTheInputSupports_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("var r = animal switch { Dog d => 1, Cat c => 2, _ => 3 };")]
    [InlineData("var r = animal switch { Dog d => 1, Animal x => 2 };")]
    [InlineData("var r = n switch { 1 => 1, 2 => 2, _ => 3 };")]
    [InlineData("var r = animal switch { Dog d when n > 1 => 1, Dog e => 2, _ => 3 };")]
    [InlineData("var r = mixed switch { int i => 1, string s => 2 };")]
    public void AnArmSomeValueReaches_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("if(nothing is string s) { n = 1; }")]
    [InlineData("if(n is < nothing) { n = 1; }")]
    public void AnInputThatAlreadyFailed_IsNotReportedAgain(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("if(n is string s) { n = 1; }")]
    [InlineData("var r = animal switch { Animal x => 1, Dog d => 2 };")]
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