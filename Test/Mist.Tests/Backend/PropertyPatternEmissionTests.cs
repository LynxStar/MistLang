using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

public sealed class PropertyPatternEmissionTests
{
    private static readonly ClassType Dog = Type("Dog");
    private static readonly ClassType Animal = Type("Animal");
    private static readonly ClassType String = Type("String", "Standard");
    private static readonly ClassType Integer = Type("Int32", "Standard");
    private static readonly ClassType Boolean = Type("Boolean", "Standard");

    [Theory]
    [InlineData(true, true, "value is Dog { Breed: var breed } whole")]
    [InlineData(true, false, "value is Dog { } whole")]
    [InlineData(false, true, "value is { Breed: var breed } whole")]
    [InlineData(false, false, "value is { } whole")]
    public void PropertyClause_RetainsItsTypeTestAndWholeBinding(bool typed, bool withMember, string expected)
    {
        var pattern = Property(typed, withMember);
        var tested = new IsPatternExpression(null!, Reference("value", Animal), pattern, Boolean);

        Assert.Equal(expected, Emit(tested));
    }

    [Fact]
    public void UnionConstituentTest_GatesThePropertyClauseInConditionsAndArms()
    {
        var union = new UnionType([Dog, String]);
        var pattern = Property(typed: true, withMember: true, constituent: Dog);
        var value = Reference("value", union);

        Assert.Equal("value.AsDogOrNull is Dog { Breed: var breed } whole", Emit(new IsPatternExpression(null!, value, pattern, Boolean)));

        var switched = new SwitchExpression(null!, value,
            [new SwitchArm(null!, pattern, null, Number(1)), new SwitchArm(null!, new CatchAllPattern(null!, union, null), null, Number(0))], Integer);

        Assert.Contains("{ AsDogOrNull: Dog { Breed: var breed } whole } => 1", Emit(switched));
    }

    [Fact]
    public void AWholeBindingAndSubbinding_ReachConditionConjunctsAndSwitchGuardsAndResults()
    {
        var pattern = Property(typed: true, withMember: true);
        var whole = Assert.IsType<LocalVariable>(pattern.Binding);
        var breed = Assert.IsType<LocalVariable>(Assert.Single(pattern.Subpatterns).Pattern.Binding);
        var guard = Guard(whole, breed);
        var test = new IsPatternExpression(null!, Reference("value", Animal), pattern, Boolean);

        Assert.Equal(
            "value is Dog { Breed: var breed } whole && (breed == \"collie\" && whole.Rank == 2)",
            Emit(Binary(test, BinaryOperatorKind.LogicalAnd, guard, Boolean)));

        var switched = new SwitchExpression(null!, Reference("value", Animal),
            [new SwitchArm(null!, pattern, guard, Result(whole, breed)), new SwitchArm(null!, new CatchAllPattern(null!, Animal, null), null, Number(-1))], Integer);

        Assert.Equal(
            "value switch { Dog { Breed: var breed } whole when breed == \"collie\" && whole.Rank == 2 => whole.Rank + breed.Length, var _ => -1 }",
            Emit(switched));
    }

    [Fact]
    public void ARefinedTypePrefix_RetainsItsPredicateBeforeThePropertyClause()
    {
        var predicate = new PropertyPattern(null!, Dog,
            [new PropertySubpattern(null!, "Breed", new ComparisonPattern(null!, String, ComparisonKind.Equal, Text("collie")), String, new NotFoundResult())]);
        var refined = new RefinedType(Dog, null!, "Collie");
        var ordinary = Property(typed: true, withMember: true);
        var whole = new LocalVariable("whole", refined);
        var pattern = new PropertyPattern(null!, refined, ordinary.Subpatterns, whole,
            new TypeTestPattern(null!, refined, null, refinement: predicate));

        Assert.Equal("value is Dog and { Breed: \"collie\" } and { Breed: var breed } whole",
            Emit(new IsPatternExpression(null!, Reference("value", Animal), pattern, Boolean)));
    }

    [Fact]
    public void ConditionBindings_AreRenamedApartFromLaterLocalsAndTheElseBranch()
    {
        var block = ScopedBody();
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        BodyEmitter.EmitBlock(block, ["value"], writer, diagnostics);

        Assert.False(diagnostics.HasErrors);
        var emitted = writer.ToString();
        Assert.Contains("Dog { Breed: var breed_1 } whole_1", emitted);
        Assert.Contains("breed_1 == \"collie\"", emitted);
        Assert.Contains("return whole_1.Rank + breed_1.Length;", emitted);
        Assert.Contains("int whole_2 = 20;", emitted);
        Assert.Contains("int whole = -1;", emitted);
        Assert.Contains("string breed = \"outside\";", emitted);
    }

    internal static PropertyPattern Property(bool typed, bool withMember, IMistType? constituent = null)
    {
        var whole = new LocalVariable("whole", typed ? Dog : Animal);
        var breed = new LocalVariable("breed", String);
        IReadOnlyList<PropertySubpattern> subpatterns = withMember
            ? [new PropertySubpattern(null!, "Breed", new CatchAllPattern(null!, String, breed), String, new NotFoundResult())]
            : [];

        return new PropertyPattern(null!, whole.Type, subpatterns, whole,
            typed ? new TypeTestPattern(null!, Dog, null, constituent) : null);
    }

    internal static Expression Guard(LocalVariable whole, LocalVariable breed)
    {
        return Binary(
            Binary(new LocalReferenceExpression(null!, breed), BinaryOperatorKind.Equal, Text("collie"), Boolean),
            BinaryOperatorKind.LogicalAnd,
            Binary(Member(whole, "Rank", Integer), BinaryOperatorKind.Equal, Number(2), Boolean), Boolean);
    }

    internal static Expression Result(LocalVariable whole, LocalVariable breed)
    {
        return Binary(Member(whole, "Rank", Integer), BinaryOperatorKind.Add, Member(breed, "Length", Integer), Integer);
    }

    internal static Block ScopedBody()
    {
        var pattern = Property(typed: true, withMember: true);
        var whole = pattern.Binding!;
        var breed = pattern.Subpatterns[0].Pattern.Binding!;
        var condition = Binary(
            new IsPatternExpression(null!, Reference("value", Animal), pattern, Boolean),
            BinaryOperatorKind.LogicalAnd, Guard(whole, breed), Boolean);
        var outerWhole = new LocalVariable("whole", Integer);
        var outerBreed = new LocalVariable("breed", String);
        var elseWhole = new LocalVariable("whole", Integer);

        return Block(
            new IfStatement(condition,
                Block(new ReturnStatement(Result(whole, breed), Integer)),
                Block(new LocalDeclarationStatement(elseWhole, Number(20)),
                    new IfStatement(Binary(new LocalReferenceExpression(null!, elseWhole), BinaryOperatorKind.Equal, Number(20), Boolean), Block(), null)),
                conditionBinds: true),
            new LocalDeclarationStatement(outerWhole, Number(-1)),
            new LocalDeclarationStatement(outerBreed, Text("outside")),
            new ReturnStatement(Binary(new LocalReferenceExpression(null!, outerWhole), BinaryOperatorKind.Add, Member(outerBreed, "Length", Integer), Integer), Integer));
    }

    internal static string Emit(Expression expression)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        BodyEmitter.ResetTemporaries();
        BodyEmitter.EmitExpression(expression, writer, diagnostics);
        Assert.False(diagnostics.HasErrors);
        return writer.ToString();
    }

    internal static LocalReferenceExpression Reference(string name, IMistType type)
    {
        return new LocalReferenceExpression(null!, new LocalVariable(name, type));
    }

    internal static ClassType Type(string name, string namespaceName = "")
    {
        return new ClassType(name, namespaceName, new Scope(null), null, null!);
    }

    internal static BinaryExpression Binary(Expression left, BinaryOperatorKind kind, Expression right, IMistType type)
    {
        return new BinaryExpression(null!, left, kind, right, type);
    }

    internal static LiteralExpression Number(int value)
    {
        return new LiteralExpression(null!, value, Integer);
    }

    private static LiteralExpression Text(string value)
    {
        return new LiteralExpression(null!, value, String);
    }

    private static MemberAccessExpression Member(LocalVariable local, string member, IMistType type)
    {
        return new MemberAccessExpression(null!, new LocalReferenceExpression(null!, local), member, type);
    }

    private static Block Block(params Statement[] statements)
    {
        return new Block(null!) { Statements = statements };
    }
}