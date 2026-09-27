// Tier 2: whole contract implication cannot use unrelated union or generic members as refinement proofs.

using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.ProgramAnalysis;

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
}