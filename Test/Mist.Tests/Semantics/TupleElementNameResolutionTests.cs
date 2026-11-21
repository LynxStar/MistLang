using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class TupleElementNameResolutionTests
{
    private const int FirstOffset = 10;

    private const int Stride = 7;

    private readonly BindingFragments f = new();

    [Theory]
    [InlineData("Item1", 2, 0)]
    [InlineData("Item2", 2, 0)]
    [InlineData("Item2", 2, 1)]
    [InlineData("Item10", 10, 9)]
    public void AnElementNamedAsOneOfItsTuplesPositionalAccessorsIsRefusedAndLosesOnlyThatName(string reserved, int arity, int position)
    {
        var names = Enumerable.Range(0, arity).Select(i => i == position ? reserved : $"part{i}").ToArray();

        var resolution = Resolve(names);

        var finding = Assert.Single(f.Diagnostics.Admitted);
        Assert.Equal("MIST0305", finding.Number.Value);
        Assert.Equal(DiagnosticSeverity.Error, finding.Severity);
        Assert.Equal(new SlotValue[] { new NameValue(reserved) }, finding.Arguments);
        Assert.Equal(FirstOffset + Stride * position, Assert.IsType<SourceRange>(finding.Location).Offset);
        Assert.True(resolution.IsFailed);
        Assert.Equal(names.Select((name, i) => i == position ? null : name), DeclaredNames(resolution.Type));
        Assert.Same(Interned(arity), Structural(resolution.Type));
    }

    [Fact]
    public void EveryReservedElementIsReportedAtItsOwnElementAndTheTupleKeepsNoDeclaredName()
    {
        var resolution = Resolve("Item1", "Item2");

        var findings = f.Diagnostics.Admitted.OrderBy(finding => Assert.IsType<SourceRange>(finding.Location).Offset).ToList();
        Assert.Equal(["MIST0305", "MIST0305"], findings.Select(finding => finding.Number.Value));
        Assert.Equal([FirstOffset, FirstOffset + Stride], findings.Select(finding => Assert.IsType<SourceRange>(finding.Location).Offset));
        Assert.Equal(new SlotValue[] { new NameValue("Item1") }, findings[0].Arguments);
        Assert.Equal(new SlotValue[] { new NameValue("Item2") }, findings[1].Arguments);
        Assert.True(resolution.IsFailed);
        Assert.Equal(new string?[] { null, null }, DeclaredNames(resolution.Type));
        Assert.Same(Interned(2), Structural(resolution.Type));
    }

    [Theory]
    [InlineData("Item")]
    [InlineData("Items")]
    [InlineData("Item1a")]
    [InlineData("Itemx1")]
    [InlineData("item1")]
    [InlineData("ITEM1")]
    public void ANameThatOnlyResemblesAPositionalAccessorIsKeptWithoutAFinding(string name)
    {
        var resolution = Resolve(name, "other");

        Assert.Empty(f.Diagnostics.Admitted);
        Assert.False(resolution.IsFailed);
        Assert.Equal(new string?[] { name, "other" }, DeclaredNames(resolution.Type));
        Assert.Same(Interned(2), Structural(resolution.Type));
    }

    private TypeResolution Resolve(params string[] names)
    {
        var elements = names.Select(Element).ToList();
        var tuple = new TupleSingleTypeRefSingleTypeRefNode
        {
            First = elements[0],
            Rest = elements[1],
            TupleTypeElements = [.. elements.Skip(2)],
        };

        return f.Builder.Resolver.ResolveWithFailure(tuple, f.Scope);
    }

    private static TupleTypeElementNode Element(string name, int position)
    {
        var element = BindingFragments.At<TupleTypeElementNode>(FirstOffset + Stride * position);
        element.Type = BindingFragments.TypeName(ElementTypeName(position));
        element.Name = BindingFragments.Token(name);
        return element;
    }

    private static string ElementTypeName(int position)
    {
        return position % 2 == 0 ? "Int32" : "String";
    }

    private TupleType Interned(int arity)
    {
        return f.Built.Interner.GetOrCreateTupleType(
            [.. Enumerable.Range(0, arity).Select(position => (IMistType)f.Built.Type(ElementTypeName(position)))]);
    }

    private static TupleType Structural(IMistType type)
    {
        return type is NamedTupleType named ? named.Underlying : Assert.IsType<TupleType>(type);
    }

    private static IReadOnlyList<string?> DeclaredNames(IMistType type)
    {
        return type is NamedTupleType named
            ? named.ElementNames
            : [.. Structural(type).ElementTypes.Select(_ => (string?)null)];
    }
}