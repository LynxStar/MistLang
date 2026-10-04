// Tier 1 (Unit): what a context key is, and the two declaration decisions that stand on it, over types
// built by hand.

using Mist.Generated;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class StructuredContextDecisionTests
{
    private readonly HandBuiltModel model = new();
    private readonly ClassType text;
    private readonly ClassType integer;
    private readonly ClassType holder = new("Holder<>", "Probe", new Scope(null), null, null);

    public StructuredContextDecisionTests()
    {
        text = model.Type("String");
        integer = model.Type("Int32");
    }

    [Fact]
    public void AnAliasAndARefinementKeyAsWhatTheyStandFor()
    {
        var alias = new TypeAlias("Name", "Probe", new Scope(null), null, new TypeAliasDeclarationNode()) { AliasedType = text };
        var refined = HandBuiltModel.Refined(integer, "Positive");

        Assert.True(ContextKeys.Same(alias, text));
        Assert.True(ContextKeys.Same(refined, integer));
        Assert.True(ContextKeys.Same(integer, refined));
        Assert.False(ContextKeys.Same(alias, integer));
    }

    [Fact]
    public void OptionalKeysRetainTheirConstituentsAfterNormalization()
    {
        var absent = model.Type("Void");
        var alias = new TypeAlias("Name", "Probe", new Scope(null), null, new TypeAliasDeclarationNode()) { AliasedType = text };
        var optional = new UnionType([text, absent]);
        var refined = HandBuiltModel.Refined(text, "Short");

        Assert.False(ContextKeys.Same(text, optional));
        Assert.False(ContextKeys.Same(optional, text));
        Assert.True(ContextKeys.Same(optional, new UnionType([absent, alias])));
        Assert.True(ContextKeys.Same(optional, new UnionType([refined, absent])));
        Assert.False(ContextKeys.Same(new TupleType([optional, integer]), new TupleType([text, integer])));
        Assert.False(ContextKeys.Same(new FunctionType([optional], text), new FunctionType([text], text)));
        Assert.False(ContextKeys.Same(new FunctionType([], optional), new FunctionType([], text)));
        Assert.False(ContextKeys.Same(new ConstructedType(holder, [optional]), new ConstructedType(holder, [text])));
    }

    [Fact]
    public void NothingIsFoundThroughABaseType()
    {
        var config = HandBuiltModel.Class("Config");
        var detailed = HandBuiltModel.Class("Detailed", config);

        Assert.False(ContextKeys.Same(detailed, config));
        Assert.False(ContextKeys.Same(config, detailed));
    }

    [Fact]
    public void AUnionIsNotAnyOfItsConstituents()
    {
        var either = model.Interner.GetOrCreateUnionType([integer, text]);

        Assert.False(ContextKeys.Same(either, integer));
        Assert.False(ContextKeys.Same(text, either));
        Assert.True(ContextKeys.Same(either, new UnionType([text, integer])));
    }

    [Fact]
    public void AConstructionKeysArgumentByArgument()
    {
        var refined = HandBuiltModel.Refined(integer, "Positive");

        Assert.True(ContextKeys.Same(new ConstructedType(holder, [integer]), new ConstructedType(holder, [integer])));
        Assert.True(ContextKeys.Same(new ConstructedType(holder, [refined]), new ConstructedType(holder, [integer])));
        Assert.False(ContextKeys.Same(new ConstructedType(holder, [integer]), new ConstructedType(holder, [text])));
        Assert.False(ContextKeys.Same(new ConstructedType(holder, [integer]), holder));
    }

    [Fact]
    public void APureFunctionTypeIsNotThePlainOne()
    {
        Assert.True(ContextKeys.Same(new FunctionType([integer], text), new FunctionType([integer], text)));
        Assert.False(ContextKeys.Same(new FunctionType([integer], text, isPure: true), new FunctionType([integer], text)));
        Assert.False(ContextKeys.Same(new FunctionType([text], text), new FunctionType([integer], text)));
    }

    [Fact]
    public void ANamedTupleKeysAsItsElements()
    {
        var pair = new TupleType([integer, text]);

        Assert.True(ContextKeys.Same(new NamedTupleType(pair, ["a", "b"]), pair));
        Assert.False(ContextKeys.Same(pair, new TupleType([text, integer])));
    }

    [Fact]
    public void ATypeParameterIsOnlyEverItself()
    {
        var first = new TypeParameter("T", 0, new Scope(null), null);
        var second = new TypeParameter("T", 0, new Scope(null), null);

        Assert.True(ContextKeys.Same(first, first));
        Assert.False(ContextKeys.Same(first, second));
        Assert.True(ContextKeys.IsOpen(first));
        Assert.True(ContextKeys.IsOpen(new ConstructedType(holder, [first])));
        Assert.False(ContextKeys.IsOpen(new ConstructedType(holder, [integer])));
    }

    [Fact]
    public void ATypeHoldingAnAnonymousTypeHasNoNameToKeyBy()
    {
        var anonymous = model.Interner.GetOrCreateAnonymousType(["A"], [integer]);

        Assert.False(ContextKeys.IsNameable(anonymous));
        Assert.False(ContextKeys.IsNameable(new ConstructedType(holder, [anonymous])));
        Assert.True(ContextKeys.IsNameable(new ConstructedType(holder, [integer])));
    }

    [Fact]
    public void EachClauseRepeatingAnEarlierKeyIsPairedWithTheFirstThatBoundIt()
    {
        var first = new ContextBinding(text, "a");
        var other = new ContextBinding(integer, "n");
        var second = new ContextBinding(text, "b");
        var third = new ContextBinding(text, "c");

        Assert.Equal([(first, second), (first, third)], ContextDecision.Repeats([first, other, second, third]));
        Assert.Empty(ContextDecision.Repeats([first, other]));
    }

    [Fact]
    public void ClausesThatDidNotResolveAreNotCalledRepeats()
    {
        Assert.Empty(ContextDecision.Repeats([new ContextBinding(ErrorType.Instance, "a"), new ContextBinding(ErrorType.Instance, "b")]));
    }

    [Fact]
    public void ASlotsClausesAreMetOnlyByTheSameKeysInAnyOrder()
    {
        Assert.Null(ContextDecision.Contract([text, integer], [integer, text]));
        Assert.Null(ContextDecision.Contract([], []));
        Assert.Equal("`with string` is missing", ContextDecision.Contract([text], []));
        Assert.Equal("`with int` is not declared by the slot", ContextDecision.Contract([], [integer]));
        Assert.Equal(
            "`with string` is missing; `with int` is not declared by the slot", ContextDecision.Contract([text], [integer]));
    }

    [Fact]
    public void AContractOverAClauseThatDidNotResolveIsLeftToTheBinder()
    {
        Assert.Null(ContextDecision.Contract([ErrorType.Instance], []));
    }
}