using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class ContractImplicationTests
{
    [Fact]
    public void AUnionAlternativeMustAcceptTheProducedBaseType()
    {
        var integer = new ClassType("Int32", "Standard", null, null, null);
        var text = new ClassType("String", "Standard", null, null, null);
        var positive = new RefinedType(integer, null!, "Positive")
        {
            Predicate = new ComparisonPattern(null!, integer, ComparisonKind.GreaterThan, new LiteralExpression(null!, 0, integer)),
        };
        var promised = new UnionType([positive, text]);
        var unknown = new TypeParameter("T", 0, new Scope(null), null);

        Assert.True(ContractImplication.Proves(positive, promised));
        Assert.True(ContractImplication.Proves(text, promised));
        Assert.False(ContractImplication.Proves(integer, promised));
        Assert.False(ContractImplication.Proves(unknown, promised));
        Assert.False(ContractImplication.Proves(new UnionType([integer, text]), promised));
    }

    [Fact]
    public void AnUnknownGenericSiblingCannotHideBehindAnEqualRefinement()
    {
        var integer = new ClassType("Int32", "Standard", null, null, null);
        var text = new ClassType("String", "Standard", null, null, null);
        var positive = new RefinedType(integer, null!, "Positive")
        {
            Predicate = new ComparisonPattern(null!, integer, ComparisonKind.GreaterThan, new LiteralExpression(null!, 0, integer)),
        };
        var pair = new ClassType("Pair", "Test", null, null, null);
        var unknown = new TypeParameter("T", 0, new Scope(null), null);
        var produced = new ConstructedType(pair, [positive, unknown]);
        var promised = new ConstructedType(pair, [positive, text]);

        Assert.True(ContractImplication.Invariant(produced, produced));
        Assert.False(ContractImplication.Proves(produced, promised));
        Assert.False(ContractImplication.Proves(promised, produced));
    }

    [Fact]
    public void AnOpaqueSubstitutionRetainsDeclarationIdentity()
    {
        var text = new ClassType("String", "Standard", null, null, null);
        var declaration = new RefinedType(text, null!, "Letters") { Predicate = new RegexPattern(null!, text, "[a-z]+") };
        var first = new RefinedType(declaration, text);
        var second = new RefinedType(declaration, text);
        var separate = new RefinedType(text, null!, "Letters") { Predicate = declaration.Predicate };

        Assert.True(ContractImplication.Proves(first, second));
        Assert.False(ContractImplication.Proves(first, separate));
    }

    [Fact]
    public void ADeclarationClosedOverAnotherLength_IsNotTheSameRefinement()
    {
        var h = new HandBuiltRefinements();
        var size = new TypeParameter("N", 0, new Scope(null), null) { IsDependent = true, Domain = h.Built.Type("Int32") };
        var index = HandBuiltRefinements.RefinePattern(h.Int, new ConjunctionPattern(HandBuiltModel.Node(), h.Int,
            [h.Compare(h.Int, ComparisonKind.GreaterThanOrEqual, 0L),
                h.Compare(h.Int, ComparisonKind.LessThan, new TypeReferenceExpression(HandBuiltModel.Node(), size))]), "Index");

        RefinedType closed(long length)
        {
            return new RefinedType(index, h.Int, new Dictionary<TypeParameter, IMistType> { [size] = new LiftedType(h.Int, length) });
        }

        Assert.True(ContractImplication.Proves(closed(3), closed(3)));
        Assert.True(ContractImplication.Proves(closed(3), closed(5)));
        Assert.False(ContractImplication.Proves(closed(5), closed(3)));
    }
}