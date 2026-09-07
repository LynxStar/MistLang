using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

/// <summary>
/// Tier 3: the value side of a pack -- a flat argument list collapsing into one parameter slot, and
/// the spread that puts one back.
/// <para>
/// ARITY STOPS BEING A NUMBER AND BECOMES A FLOOR, which is what every assertion here turns on.
/// Overload resolution filtered candidates by an exact count, so a method taking a pack matched
/// only at the one arity where its slots happened to equal its arguments -- and a spread, being one
/// expression, counted as one argument however many it stood for.
/// </para>
/// </summary>
public sealed class ValuePackTests : ProgramModelProbe
{
    public ValuePackTests()
        : base("MistValuePack_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Calls
        {
            public static int Describe<params TArgs>(params TArgs args) { return TArgs.Length; }
            public static int Middle<TFirst, params TMid, TLast>(TFirst f, params TMid m, TLast l) { return TMid.Length; }
            public static int Add2(int a, int b) { return a + b; }
        }

        public class Packs
        {
            public class Bundle<params TArgs> { public TArgs... Item; }
        }
        """;

    protected override string Preamble { get; } = "";

    private ImmutableArray<Diagnostic> Run(string statement)
    {
        return BuildRun(statement);
    }

    [Theory]
    [InlineData("var n = Calls.Describe(1, \"x\", true);")]
    [InlineData("var n = Calls.Describe(1);")]
    [InlineData("var n = Calls.Describe();")]
    public void FlatArgumentsCollapseIntoThePackSlot(string statement)
    {
        Assert.Empty(Run(statement).Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void FixedParametersBindInwardFromBothEnds()
    {
        Assert.Empty(
            Run("var n = Calls.Middle(1, \"a\", \"b\", true);")
                .Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    /// <summary>
    /// The floor still holds: a pack absorbing nothing does not excuse an unfilled fixed slot.
    /// </summary>
    [Fact]
    public void TooFewForTheFixedParameters_IsStillRefused()
    {
        _ = Assert.Single(Errors(Run("var n = Calls.Middle(1);"), "MIST_PM_NO_APPLICABLE_OVERLOAD"));
    }

    [Fact]
    public void TupleSpread_ExpandsIntoArgumentSlots()
    {
        var body = BodyIn("var pair = (10, 20); var sum = Calls.Add2(pair...);");
        var call = body.Statements
            .OfType<LocalDeclarationStatement>()
            .Select(d => d.Initializer)
            .OfType<InvocationExpression>()
            .Single();

        // Two arguments from one written expression, which is the whole of what a spread does.
        Assert.Equal(2, call.Arguments.Count);
    }

    /// <summary>
    /// The homogeneous form is C#'s, position rule included: its arguments are whatever runs off
    /// the end of the fixed ones, which no earlier slot can collect.
    /// </summary>
    [Fact]
    public void AParamsArrayThatIsNotLast_IsRefused()
    {
        _ = Assert.Single(Errors(
            BuildRun("", "public class Bad { public static int M(params int[] xs, string tail) { return 0; } }"),
            "MIST_VAR_0012"));
    }

    /// <summary>
    /// The boundary the rule must not cross. A value PACK binds inward from both ends, so the
    /// middle is exactly where the Binding_Rule expects it -- and MiddleCount, which the banked
    /// sample runs, is declared that way.
    /// </summary>
    [Theory]
    [InlineData("public static int M(params int[] xs) { return 0; }")]
    [InlineData("public static int M(string head, params int[] xs) { return 0; }")]
    [InlineData("public static int M<TFirst, params TMid, TLast>(TFirst f, params TMid m, TLast l) { return 0; }")]
    public void ADeclarationCSharpWouldAccept_AndAMiddlePack_AreNot(string declaration)
    {
        Assert.Empty(Errors(BuildRun("", $"public class Fine {{ {declaration} }}"), "MIST_VAR_0012"));
    }

    /// <summary>
    /// The projections a spread member stands for, found per CONSTRUCTION: the same Bundle has
    /// three where its pack holds three, so they cannot be declared with the type.
    /// </summary>
    [Theory]
    [InlineData("b.Item1")]
    [InlineData("b.Item2")]
    [InlineData("b.Item3")]
    public void SpreadProjections_ExistPerConstruction(string access)
    {
        Assert.Empty(
            Run($"var b = new Packs.Bundle<int, string, bool>(); var x = {access};")
                .Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    /// <summary>
    /// One past the end names nothing, which keeps the family bounded by the pack rather than open
    /// to any index a caller writes.
    /// </summary>
    [Fact]
    public void ProjectionPastTheEndOfThePack_IsRefused()
    {
        _ = Assert.Single(
            Errors(
                Run("var b = new Packs.Bundle<int, string, bool>(); var x = b.Item4;"),
                "MIST_PM_UNRESOLVED_MEMBER"));
    }

    [Fact]
    public void SpreadOfSomethingWithNoKnownLength_IsRefused()
    {
        _ = Assert.Single(
            Errors(Run("var n = 5; var s = Calls.Add2(n...);"), "MIST_VAR_0011"));
    }
}