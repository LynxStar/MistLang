using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Types;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

/// <summary>
/// A name declared twice in one type parameter list, refused where the list is populated: dependent or
/// not, two parameters of one name are one C# identifier declared twice, and every use of the name is
/// ambiguous between them.
/// </summary>
[Trait("Tier", "1")]
public sealed class TypeParameterNameTests
{
    [Theory]
    [InlineData("dependent-twice", 1)]
    [InlineData("ordinary-then-dependent", 1)]
    [InlineData("ordinary-twice", 1)]
    [InlineData("three-of-one-name", 2)]
    [InlineData("distinct", 0)]
    public void ARepeatedNameIsRefusedAtEachRepeat(string shape, int findings)
    {
        var nodes = shape switch
        {
            "dependent-twice" => new[] { Dependent("N", 10), Dependent("N", 20) },
            "ordinary-then-dependent" => [Ordinary("N", 10), Dependent("N", 20)],
            "ordinary-twice" => [Ordinary("T", 10), Ordinary("T", 20)],
            "three-of-one-name" => [Dependent("N", 10), Ordinary("N", 20), Dependent("N", 30)],
            _ => [Dependent("N", 10), Ordinary("T", 20)],
        };
        var diagnostics = new DiagnosticBag();
        var box = new ClassType("Box<>", "Probe", new Scope(null), null, null);

        TypeParameterPopulator.Populate(
            box, box.Scope, box, new TypeParameterListNode { TypeParameter = nodes[0], TypeParameters = [.. nodes.Skip(1)] }, diagnostics);

        var reported = diagnostics.ToImmutableArray();
        Assert.Equal(findings, reported.Length);
        Assert.All(reported, diagnostic => Assert.Equal("MIST0408", diagnostic.Number.Value));
        Assert.Equal(nodes.Length, box.TypeParametersList.Count);
    }

    [Fact]
    public void TheRepeatIsReportedAtTheLaterDeclaration()
    {
        var diagnostics = new DiagnosticBag();
        var box = new ClassType("Box<>", "Probe", new Scope(null), null, null);

        TypeParameterPopulator.Populate(
            box, box.Scope, box, new TypeParameterListNode { TypeParameter = Dependent("N", 10), TypeParameters = [Dependent("N", 40)] }, diagnostics);

        Assert.Equal(41, Assert.Single(diagnostics.ToImmutableArray()).Position().Start.Column);
    }

    private static TypeParameterNode Dependent(string name, int offset)
    {
        var node = BindingFragments.At<DependentTypeParameterTypeParameterNode>(offset);
        node.Name = BindingFragments.Token(name);
        node.ValueType = new IntTypePrimitiveTypeNode();
        return node;
    }

    private static TypeParameterNode Ordinary(string name, int offset)
    {
        var node = BindingFragments.At<RegularTypeParameterTypeParameterNode>(offset);
        node.Name = BindingFragments.Token(name);
        return node;
    }
}