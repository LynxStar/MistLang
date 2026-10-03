using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Construction;

// Tier 1: a pack rebound to one element, as a whole-pack constraint is judged and emitted per element
// (variadics 11.1). The bare name becomes the element; a spread of the pack or an index into it is not an
// occurrence of the element and keeps reading the pack -- the closing's where one bound it, and itself
// where none did, which is what the backend's per-element clauses start from.
[Trait("Tier", "1")]
public sealed class ElementwiseRebindingTests
{
    private readonly TypeParameter pack = new("Ts", 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed, PackLength = 2 };

    private readonly ClassType rank = HandBuiltModel.Class("IRank");

    private readonly ClassType box = HandBuiltModel.Class("Box");

    private readonly ClassType score = HandBuiltModel.Class("Score");

    private readonly ClassType grade = HandBuiltModel.Class("Grade");

    [Theory]
    [InlineData("bare", "IRank<Score>")]
    [InlineData("nested", "IRank<Box<Score>>")]
    [InlineData("spread", "IRank<(Score, Grade)>")]
    [InlineData("index", "IRank<Grade>")]
    [InlineData("spread-in-a-pack-slot", "IRank<[Score, Grade]>")]
    [InlineData("spread-in-a-function", "IRank<func<Score, Grade, Score>>")]
    public void AgainstTheClosing_OnlyTheBareNameIsTheElement(string shape, string expected)
    {
        var closing = TypeSubstitution.Empty.With(pack, Pack(score, grade));

        Assert.Equal(expected, TypeDisplay.Mist(closing.With(pack, score).Apply(Constraint(shape), interner: null)));
    }

    [Theory]
    [InlineData("bare", "IRank<Score>")]
    [InlineData("nested", "IRank<Box<Score>>")]
    [InlineData("spread", "IRank<(Ts...)>")]
    [InlineData("index", "IRank<Ts[1]>")]
    public void WithNoClosing_ASpreadAndAnIndexStayThePacks(string shape, string expected)
    {
        Assert.Equal(expected, TypeDisplay.Mist(TypeSubstitution.Empty.With(pack, score).Apply(Constraint(shape), interner: null)));
    }

    [Fact]
    public void AParameterThatIsNoPack_IsReboundOutright()
    {
        var plain = new TypeParameter("T", 0, new Scope(null), null);
        var constraint = new ConstructedType(rank, [plain]);

        Assert.Equal(
            "IRank<Grade>",
            TypeDisplay.Mist(TypeSubstitution.Empty.With(plain, score).With(plain, grade).Apply(constraint, interner: null)));
    }

    [Fact]
    public void ExtendingByABindingOfTheSamePack_RestoresItsSpread()
    {
        var rebound = TypeSubstitution.Empty.With(pack, score);
        var closing = TypeSubstitution.ForParameters([pack], [Pack(grade, score)]);

        Assert.Equal("IRank<(Grade, Score)>", TypeDisplay.Mist(rebound.Extend(closing).Apply(Constraint("spread"), interner: null)));
    }

    private IMistType Constraint(string shape)
    {
        IMistType argument = shape switch
        {
            "bare" => pack,
            "nested" => new ConstructedType(box, [pack]),
            "spread" => new TupleType([new SpreadType(pack)]),
            "index" => pack.ElementAt(1),
            "spread-in-a-pack-slot" => new LiftedType(HandBuiltModel.Class("Type"), new TypePackValue([new SpreadType(pack)])),
            "spread-in-a-function" => new FunctionType([new SpreadType(pack)], pack),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        return new ConstructedType(rank, [argument]);
    }

    private static LiftedType Pack(params IMistType[] elements)
    {
        return new LiftedType(HandBuiltModel.Class("Type"), new TypePackValue(elements));
    }
}