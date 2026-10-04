// Tuple identity and assignability, asserted on the model: two declarations of the same shape print
// the same whether or not they share an interned instance. Model assertions pin the conversion and
// narrowing decisions independently of C# resolving emitted code.

using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class TupleTypeIdentityTests : ProgramModelProbe
{
    public TupleTypeIdentityTests()
        : base("MistTupleIdentity_")
    {
    }

    protected override string Fixtures { get; } = "";

    protected override string Preamble { get; } = """
        (int, string) plain = (1, "a");
        (int x, int y) named = (x: 1, y: 2);
        (int, int) unnamedPair = (1, 2);
        byte small = 1;
        """;

    [Fact]
    public void ATupleLiteralIsTypedByTheInternedStructuralTuple_NeverByANamedOne()
    {
        var literal = InitializerOf<TupleLiteralExpression>("var probe = (x: 3, y: 4);");

        _ = Assert.IsType<TupleType>(literal.Type);
        Assert.Equal(["x", "y"], literal.ElementNames);
    }

    [Fact]
    public void TwoDeclarationsOfOneShape_ShareOneInternedType()
    {
        var body = BodyIn("(int, string) second = (2, \"b\");");
        var declared = Locals(body, "plain", "second");

        Assert.Same(declared[0], declared[1]);
    }

    [Fact]
    public void ANamedTupleIsNotInterned_ButItsUnderlyingIs()
    {
        var body = BodyIn("(int x, int y) again = (x: 3, y: 4);");
        var declared = Locals(body, "named", "again");

        Assert.NotSame(declared[0], declared[1]);
        Assert.Same(
            Assert.IsType<NamedTupleType>(declared[0]).Underlying,
            Assert.IsType<NamedTupleType>(declared[1]).Underlying);
    }

    [Fact]
    public void ANamedTuplesUnderlyingIsTheSameInstanceAsTheEquivalentUnnamedDeclaration()
    {
        var body = BodyIn("var probe = 0;");
        var declared = Locals(body, "named", "unnamedPair");

        Assert.Same(Assert.IsType<NamedTupleType>(declared[0]).Underlying, declared[1]);
    }

    [Fact]
    public void ANamedTupleIsTheSameTypeAsItsShape_InBothDirections()
    {
        var body = BodyIn("var probe = 0;");
        var declared = Locals(body, "named", "unnamedPair");

        Assert.True(ConversionRelation.IsIdentity(declared[0], declared[1]));
        Assert.True(ConversionRelation.IsWithin(declared[0], declared[1]));
        Assert.True(ConversionRelation.IsWithin(declared[1], declared[0]));
    }

    [Fact]
    public void ATupleIsWithinAnotherWhereverItsElementsAre()
    {
        var body = BodyIn("(byte, string) widening = (small, \"a\");");
        var declared = Locals(body, "widening", "plain");

        Assert.True(ConversionRelation.IsWithin(declared[0], declared[1]));
        Assert.False(ConversionRelation.IsWithin(declared[1], declared[0]));
    }

    [Fact]
    public void TuplesOfDifferentArity_AreUnrelated()
    {
        var body = BodyIn("(int, string, int) triple = (1, \"a\", 2);");
        var declared = Locals(body, "triple", "plain");

        Assert.False(ConversionRelation.IsWithin(declared[0], declared[1]));
        Assert.False(ConversionRelation.IsWithin(declared[1], declared[0]));
    }

    /// <summary>
    /// A target-typed <c>new()</c> has no type of its own, so it binds ONLY if the element target reached
    /// it. Which makes this the one element form that distinguishes a pushed target from an ignored one.
    /// </summary>
    [Fact]
    public void ATargetTypedElementTakesTheTargetsElementType()
    {
        var literal = InitializerOf<TupleLiteralExpression>(
            "(int, Greeter) built = (1, new());",
            extraTypes: "public class Greeter { }");

        Assert.Equal("Probe.Greeter", literal.Elements[1].Type.FullyQualifiedName);
    }

    [Fact]
    public void ALiteralKeepsItsOwnElementTypes_WhereTheTargetsAreMerelyWider()
    {
        var declaration = DeclarationIn("(long, unknown) widened = (1, \"a\");", extraTypes: "");
        var literal = Assert.IsType<TupleLiteralExpression>(declaration.Initializer);
        var elements = Assert.IsType<TupleType>(literal.Type).ElementTypes;

        Assert.Equal(["Standard.Int32", "Standard.String"], elements.Select(e => e.FullyQualifiedName));
        Assert.Equal("(Int64, unknown)", declaration.Local.Type.Name);
    }

    [Fact]
    public void ANestedTupleLiteralIsTypedByANestedInternedTuple()
    {
        var literal = InitializerOf<TupleLiteralExpression>("var probe = ((1, 2), \"label\");");
        var elements = Assert.IsType<TupleType>(literal.Type).ElementTypes;

        Assert.Equal(2, Assert.IsType<TupleType>(elements[0]).ElementTypes.Count);
    }

    private static List<IMistType> Locals(Block body, params string[] names)
    {
        var declared = body.Statements
            .OfType<LocalDeclarationStatement>()
            .ToDictionary(statement => statement.Local.Name, statement => statement.Local.Type);

        return [.. names.Select(name => declared[name])];
    }
}