// Anonymous types: the shape a literal resolves to, and what unifies two of them. Asserted on the
// MODEL because C# identifies its own anonymous types by the same rule, so a program that runs
// proves only that the two pipelines agreed -- whether Mist DECIDED the two literals were one type,
// or merely emitted something C# then unified, is visible nowhere else.

using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class AnonymousTypeResolutionTests : ProgramModelProbe
{
    public AnonymousTypeResolutionTests()
        : base("MistAnonymousType_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Holder
        {
            public string Name { get; set; }

            public int Count()
            {
                return 1;
            }
        }
        """;

    protected override string Preamble { get; } = """
        var city = "Portland";
        var held = new Holder();
        """;

    [Fact]
    public void AMemberTakesTheTypeOfTheValueWrittenForIt()
    {
        var anonymous = TypeOf("var v = new { N = 1, S = \"s\" };");

        Assert.Equal(["N", "S"], anonymous.MemberNames);
        Assert.Equal(
            ["Standard.Int32", "Standard.String"],
            anonymous.Fields.Select(field => field.Type.FullyQualifiedName));
    }

    [Fact]
    public void AMemberIsReachableByName()
    {
        Assert.Equal("Standard.Int32", TypeOfLocal("var read = v.N;", "var v = new { N = 1 };"));
    }

    [Fact]
    public void TwoLiteralsOfOneShapeReachTheSameInternedType()
    {
        var (first, second) = TwoLocals("var second = new { X = 1, Y = 2 };", "var first = new { X = 1, Y = 2 };");

        Assert.Same(first, second);
    }

    [Theory]
    [InlineData("var second = new { Y = 2, X = 1 };")]
    [InlineData("var second = new { X = 1, Z = 2 };")]
    public void ALiteralOfAnotherShapeReachesAnotherType(string second)
    {
        var (one, other) = TwoLocals(second, "var first = new { X = 1, Y = 2 };");

        Assert.NotSame(one, other);
    }

    [Fact]
    public void AProjectedMemberTakesTheNameOfTheIdentifierProjected()
    {
        Assert.Equal(["city"], TypeOf("var v = new { city };").MemberNames);
    }

    [Fact]
    public void AProjectedMemberTakesTheNameOfTheMemberAccessTail()
    {
        Assert.Equal(["Name"], TypeOf("var v = new { held.Name };").MemberNames);
    }

    [Fact]
    public void AProjectionEndingInNoName_IsRefused()
    {
        var diagnostics = BuildRun("var v = new { held.Count() };");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_ANONYMOUS_MEMBER_NOT_NAMEABLE"));
    }

    // ── Harness ─────────────────────────────────────────────────────────

    private AnonymousType TypeOf(string statement)
    {
        return InitializerOf<AnonymousObjectExpression>(statement).AnonymousType;
    }

    private string TypeOfLocal(string statement, string extraPreamble)
    {
        return SingleLocal(statement, extraTypes: "", extraPreamble).Type.FullyQualifiedName;
    }

    /// <summary>
    /// Both locals out of ONE build, because interning is per model: two builds have two interners and
    /// would answer NotSame however well the interning works.
    /// </summary>
    private (object First, object Second) TwoLocals(string statement, string extraPreamble)
    {
        var declared = BodyIn(statement, extraTypes: "", extraPreamble)
            .Statements.OfType<LocalDeclarationStatement>()
            .ToList();

        return (
            declared.First(declaration => declaration.Local.Name == "first").Local.Type,
            declared.First(declaration => declaration.Local.Name == "second").Local.Type);
    }
}