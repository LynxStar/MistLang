using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class RefinementCarrierClosingOrderTests
{
    [Fact]
    public void TwoNamedBindings_KeepTheSameRefinementTagInEitherClosingOrder()
    {
        var integer = PropertyPatternEmissionTests.Type("Int32", "Standard");
        var scope = new Scope(null);
        var lower = new TypeParameter("Lower", 0, scope, null) { IsDependent = true, Domain = integer };
        var upper = new TypeParameter("Upper", 1, scope, null) { IsDependent = true, Domain = integer };
        var node = HandBuiltModel.Node();
        var declaration = new RefinedType(integer, node, "Between")
        {
            Predicate = new ConjunctionPattern(node, integer,
            [
                new ComparisonPattern(node, integer, ComparisonKind.GreaterThanOrEqual, new TypeReferenceExpression(node, lower)),
                new ComparisonPattern(node, integer, ComparisonKind.LessThanOrEqual, new TypeReferenceExpression(node, upper)),
            ]),
        };
        var two = new LiftedType(integer, 2L);
        var eight = new LiftedType(integer, 8L);
        var ten = new LiftedType(integer, 10L);
        var lowerFirst = new RefinedType(declaration, integer, new Dictionary<TypeParameter, IMistType> { [lower] = two, [upper] = eight });
        var upperFirst = new RefinedType(declaration, integer, new Dictionary<TypeParameter, IMistType> { [upper] = eight, [lower] = two });
        var changedUpper = new RefinedType(declaration, integer, new Dictionary<TypeParameter, IMistType> { [upper] = ten, [lower] = two });

        Assert.Equal(RefinedConstituentNames.Of(lowerFirst), RefinedConstituentNames.Of(upperFirst));
        Assert.NotEqual(RefinedConstituentNames.Of(upperFirst), RefinedConstituentNames.Of(changedUpper));
    }
}