using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

// Tier 2: PatternBinder's list chain over hand-built syntax, asserting the input each element is matched against.
[Trait("Tier", "2")]
public sealed class ListPatternInputBindingTests
{
    [Fact]
    public void AnElementRecordsTheIndexersType_AndASliceTheSliceType()
    {
        var f = new BindingFragments();
        var text = Sequence(f, "String", f.Built.Type("Char"));

        var bound = Assert.IsType<ListPattern>(PatternBinder.Bind(
            List(Element(BareType("Int32")), Slice(Var("rest"))), text, f.Binding, f.Builder));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.Equal(2, bound.Elements.Count);
        Assert.Same(f.Built.Type("Char"), bound.Elements[0].ScrutineeType);
        Assert.Same(f.Built.Type("Int32"), bound.Elements[0].Subpattern!.NarrowedType);
        Assert.Same(text, bound.Elements[1].ScrutineeType);
    }

    [Fact]
    public void ACustomShapeRecordsItsSelectedIndexer_AndABareSliceRecordsNothing()
    {
        var f = new BindingFragments();
        var shelf = Sequence(f, "Shelf", f.Built.Type("String"));

        var bound = Assert.IsType<ListPattern>(PatternBinder.Bind(
            List(Element(Var("first")), Slice(null), Element(Var("last"))), shelf, f.Binding, f.Builder));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.Equal([f.Built.Type("String"), null, f.Built.Type("String")], bound.Elements.Select(element => element.ScrutineeType));
        Assert.Null(bound.Elements[1].Subpattern);
    }

    private static ClassType Sequence(BindingFragments f, string name, IMistType element)
    {
        var integer = f.Built.Type("Int32");
        var sequence = name == "String" ? f.Built.Type(name) : HandBuiltModel.Class(name);
        sequence.IndexersList.Add(new Indexer(element, [new Parameter("at", integer)], DeclarationFragments.Public)
        {
            DeclaringType = sequence,
            Getter = new PropertyAccessor { IsAuto = true },
        });
        sequence.PropertiesList.Add(new Property("Length", integer, DeclarationFragments.Public)
        {
            DeclaringType = sequence,
            Getter = new PropertyAccessor { IsAuto = true },
        });
        return sequence;
    }

    private static PatternNode List(params ListPatternElementNode[] elements)
    {
        var list = BindingFragments.At<ListPatternPrimaryPatternNode>();
        list.Elements = new ListPatternElementListNode { ListPatternElement = elements[0], ListPatternElements = [.. elements.Skip(1)] };
        return Pattern(list);
    }

    private static ListPatternElementNode Element(PatternNode pattern)
    {
        return new PatternElementListPatternElementNode { Pattern = pattern };
    }

    private static ListPatternElementNode Slice(PatternNode? subpattern)
    {
        return new SliceElementListPatternElementNode { Subpattern = subpattern };
    }

    private static PatternNode BareType(string name)
    {
        var primary = BindingFragments.At<BareTypePatternPrimaryPatternNode>();
        primary.Type = BindingFragments.TypeName(name);
        return Pattern(primary);
    }

    private static PatternNode Var(string name)
    {
        var primary = BindingFragments.At<VarPatternPrimaryPatternNode>();
        primary.Name = BindingFragments.Token(name);
        return Pattern(primary);
    }

    private static PatternNode Pattern(PrimaryPatternNode primary)
    {
        return new()
        {
            OrPattern = new OrPatternNode { Left = new AndPatternNode { Left = new DirectPatternUnaryPatternNode { Primary = primary } } },
        };
    }
}