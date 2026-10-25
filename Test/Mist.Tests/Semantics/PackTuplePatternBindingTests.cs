using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

// Tier 2: PatternBinder's tuple chain over hand-built syntax, with a scrutinee typed `(TArgs...)` as
// `params TArgs args` and a `TArgs...` field are.
[Trait("Tier", "2")]
public sealed class PackTuplePatternBindingTests
{
    private readonly BindingFragments f = new();
    private readonly TypeParameter open = new("TArgs", 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed };

    [Fact]
    public void AnOpenPack_MatchesEachElementAgainstThePacksElementAtThatIndex()
    {
        var bound = Bind(Spread(open), Typed("Int32", "a"), Typed("Int32", "b"));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.Same(open, bound.OpenPack);
        Assert.Equal(["TArgs[0]", "TArgs[1]"], Assert.IsAssignableFrom<ITupleType>(bound.TestedType).ElementTypes.Select(type => type.Name));
        Assert.Same(bound.TestedType, bound.NarrowedType);
        Assert.All(bound.Elements, element => Assert.Same(f.Built.Type("Int32"), Assert.IsType<TypeTestPattern>(element).NarrowedType));
        Assert.Same(f.Built.Type("Int32"), f.Scope.Locals["b"].Type);
    }

    [Fact]
    public void AnOpenPackAtWidthThree_BindsEachVarAtItsElementType()
    {
        var bound = Bind(Spread(open), Var("a"), Var("b"), Var("c"));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.Equal(3, bound.Elements.Count);
        Assert.Same(open.ElementAt(0), f.Scope.Locals["a"].Type);
        Assert.Same(open.ElementAt(2), f.Scope.Locals["c"].Type);
    }

    [Fact]
    public void AnElementTypeNoInstantiationNeedHold_IsNoRefusal()
    {
        var bound = Bind(Spread(open), Typed("Boolean", "b"), Typed("String", "s"));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.False(bound.IsFailed);
    }

    [Fact]
    public void DiscardsOverAnOpenPack_TestOnlyItsWidth()
    {
        var bound = Bind(Spread(open), Discard(), Discard());

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.Same(open, bound.OpenPack);
        Assert.All(bound.Elements, element => Assert.Null(Assert.IsType<CatchAllPattern>(element).Binding));
    }

    [Fact]
    public void AFixedSizePack_IsReadAtTheWidthItDeclares()
    {
        var fixedPack = new TypeParameter("TPair", 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed, PackLength = 2 };

        var bound = Bind(Spread(fixedPack), Typed("Int32", "a"), Var("b"));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.Null(bound.OpenPack);
        Assert.Null(bound.TestedType);
        Assert.Same(fixedPack.ElementAt(1), f.Scope.Locals["b"].Type);
    }

    [Theory]
    [InlineData("fixed-pack", "The pattern spells 3 elements but '(TPair...)' holds 2, so this pattern can never match.")]
    [InlineData("tuple", "The pattern spells 3 elements but '(Int32, String)' holds 2, so this pattern can never match.")]
    public void AClosedWidthKeepsItsArityRefusal(string scrutinee, string message)
    {
        var fixedPack = new TypeParameter("TPair", 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed, PackLength = 2 };
        var type = scrutinee == "tuple"
            ? f.Built.Interner.GetOrCreateTupleType([f.Built.Type("Int32"), f.Built.Type("String")])
            : Spread(fixedPack);

        var bound = Bind(type, Var("a"), Var("b"), Var("c"));

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_TUPLE_PATTERN_ARITY", diagnostic.Number.Value);
        Assert.Equal(message, diagnostic.Message);
        Assert.True(bound.IsFailed);
    }

    [Fact]
    public void APlainTuple_ReadsItsOwnElementsWithNoWidthToTest()
    {
        var bound = Bind(f.Built.Interner.GetOrCreateTupleType([f.Built.Type("Int32"), f.Built.Type("String")]), Typed("Boolean", "b"), Typed("String", "s"));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.Null(bound.OpenPack);
        Assert.Null(bound.TestedType);
    }

    private TuplePattern Bind(IMistType scrutinee, params PrimaryPatternNode[] elements)
    {
        var tuple = BindingFragments.At<TuplePositionalPatternPrimaryPatternNode>();
        tuple.First = Pattern(elements[0]);
        tuple.Rest = Pattern(elements[1]);
        tuple.Patterns = [.. elements.Skip(2).Select(Pattern)];
        return Assert.IsType<TuplePattern>(PatternBinder.Bind(Pattern(tuple), scrutinee, f.Binding, f.Builder));
    }

    private IMistType Spread(TypeParameter pack)
    {
        return f.Built.Interner.GetOrCreateTupleType([new SpreadType(pack)]);
    }

    private static PrimaryPatternNode Typed(string type, string name)
    {
        var primary = BindingFragments.At<TypePatternPrimaryPatternNode>();
        primary.Type = BindingFragments.TypeName(type);
        primary.Name = BindingFragments.Token(name);
        return primary;
    }

    private static PrimaryPatternNode Var(string name)
    {
        var primary = BindingFragments.At<VarPatternPrimaryPatternNode>();
        primary.Name = BindingFragments.Token(name);
        return primary;
    }

    private static PrimaryPatternNode Discard()
    {
        return BindingFragments.At<DiscardPatternPrimaryPatternNode>();
    }

    private static PatternNode Pattern(PrimaryPatternNode primary)
    {
        return new()
        {
            OrPattern = new OrPatternNode { Left = new AndPatternNode { Left = new DirectPatternUnaryPatternNode { Primary = primary } } },
        };
    }
}