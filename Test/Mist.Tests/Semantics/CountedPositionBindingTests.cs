using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

// A position C# counts back from the receiver's count, for ^ or a list pattern's elements, is read only through
// an indexer that takes an int or an Index. Selection alone cannot say so: it hands back a lone candidate
// without asking whether the argument fits, and Roslyn then refuses the emitted access.
[Trait("Tier", "2")]
public sealed class CountedPositionBindingTests
{
    [Theory]
    [InlineData("String")]
    [InlineData("Range")]
    public void AFromEndIndexIntoAnIndexerThatTakesNoPositionIsLeftToTheParameterCheck(string parameter)
    {
        var f = new BindingFragments();
        var shelf = Shelf(f, f.Built.Type(parameter));

        var read = Assert.IsType<IndexExpression>(FromEnd(f, shelf));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.Null(read.FromEnd);
        Assert.Same(f.Built.Type(parameter), read.ParameterType);
    }

    [Fact]
    public void AFromEndIndexIntoARefinedIntIndexerIsStillCounted()
    {
        var f = new BindingFragments();
        var shelf = Shelf(f, HandBuiltModel.Refined(f.Built.Type("Int32")));

        var read = Assert.IsType<IndexExpression>(FromEnd(f, shelf));

        Assert.NotNull(read.FromEnd);
        Assert.Null(read.ParameterType);
    }

    [Theory]
    [InlineData("String", false)]
    [InlineData("Range", false)]
    [InlineData("Index", true)]
    [InlineData("Refined", true)]
    public void AListPatternReadsItsElementsOnlyThroughAnIndexerThatTakesAPosition(string parameter, bool admitted)
    {
        var f = new BindingFragments();
        IMistType slot = parameter == "Refined" ? HandBuiltModel.Refined(f.Built.Type("Int32")) : f.Built.Type(parameter);
        var shelf = Shelf(f, slot);
        var element = new PatternElementListPatternElementNode { Pattern = Var("first") };
        var list = BindingFragments.At<ListPatternPrimaryPatternNode>();
        list.Elements = new ListPatternElementListNode { ListPatternElement = element, ListPatternElements = [new SliceElementListPatternElementNode()] };

        var bound = Assert.IsType<ListPattern>(PatternBinder.Bind(Pattern(list), shelf, f.Binding, f.Builder));

        Assert.Equal(admitted, !bound.IsFailed);
        Assert.Equal(admitted ? [] : ["MIST_PM_LIST_PATTERN_SCRUTINEE"], f.Diagnostics.ToImmutableArray().Select(diagnostic => diagnostic.Id.Value));
    }

    private static ClassType Shelf(BindingFragments f, IMistType parameter)
    {
        var shelf = HandBuiltModel.Class("Shelf");
        f.Register(shelf);
        f.Register(f.Built.Type("Range"));
        shelf.PropertiesList.Add(new Property("Count", f.Built.Type("Int32"), DeclarationFragments.Public)
        {
            DeclaringType = shelf,
            Getter = new PropertyAccessor { IsAuto = true },
        });
        shelf.IndexersList.Add(new Indexer(f.Built.Type("String"), [new Parameter("at", parameter)], DeclarationFragments.Public)
        {
            DeclaringType = shelf,
            Getter = new PropertyAccessor { IsAuto = true },
        });
        return shelf;
    }

    private static Expression FromEnd(BindingFragments f, ClassType shelf)
    {
        _ = f.Local("shelf", shelf);
        _ = f.Local("last", f.Built.Type("Index"));
        var index = BindingFragments.At<IndexSuffixPostfixSuffixNode>();
        index.Index = BindingFragments.Expression(BindingFragments.Identifier("last"));
        return f.Bind(BindingFragments.Identifier("shelf"), index);
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