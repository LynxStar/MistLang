using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class ExtensionSignatureDecisionTests
{
    [Fact]
    public void DifferentParametersCoexistWithinAndAcrossBlocks()
    {
        var f = new ExtensionSignatureFragments();
        var first = f.Block(f.Integer);
        var second = f.Block(f.Integer);
        _ = f.Method(first, "Pick", parameters: [new Parameter("number", f.Integer)]);
        _ = f.Method(first, "Pick", parameters: [new Parameter("text", f.String)]);
        _ = f.Method(second, "Pick", parameters: [new Parameter("number", f.String), new Parameter("text", f.String)]);

        Assert.Empty(ExtensionSignatureDecision.Repeats([first, second]));
    }

    [Fact]
    public void SameBlockRepeatsIgnoreParameterNamesAndReturnTypes()
    {
        var f = new ExtensionSignatureFragments();
        var block = f.Block(f.Integer);
        var first = f.Method(block, "Pick", parameters: [new Parameter("first", f.Integer)]);
        var renamed = f.Method(block, "Pick", parameters: [new Parameter("second", f.Integer)]);
        var changedReturn = f.Method(block, "Pick", f.String, new Parameter("third", f.Integer));

        var repeats = ExtensionSignatureDecision.Repeats([block]).ToList();
        Assert.Equal(2, repeats.Count);
        Assert.Same(renamed.DeclarationNode, repeats[0].Later);
        Assert.Same(changedReturn.DeclarationNode, repeats[1].Later);
        Assert.All(repeats, repeat => Assert.Same(first.DeclarationNode, repeat.Earlier));
    }

    [Fact]
    public void SameSignatureAcrossBlocksReportsLaterMemberOnce()
    {
        var f = new ExtensionSignatureFragments();
        var first = f.Block(f.Integer);
        var second = f.Block(f.Integer);
        var earlier = f.Method(first, "Pick", parameters: [new Parameter("a", f.Integer)]);
        var later = f.Method(second, "Pick", parameters: [new Parameter("b", f.Integer)]);

        var repeat = Assert.Single(ExtensionSignatureDecision.Repeats([first, second]));
        Assert.Same(earlier.DeclarationNode, repeat.Earlier);
        Assert.Same(later.DeclarationNode, repeat.Later);
        Assert.Equal("(int)", repeat.Signature);
    }

    [Fact]
    public void AlphaEquivalentBlockAndMethodParametersRepeat()
    {
        var f = new ExtensionSignatureFragments();
        var pair = f.Built.Type("Pair<,>");
        var first = f.Block();
        var t = ExtensionSignatureFragments.Parameter(first, "T");
        first.ResolvedReceiverType = ExtensionSignatureFragments.Construct(pair, t, t);
        var left = f.Method(first, "Pick");
        var u = ExtensionSignatureFragments.Parameter(left, "U");
        left.ParametersList.Add(new Parameter("a", t));
        left.ParametersList.Add(new Parameter("b", u));

        var second = f.Block();
        var a = ExtensionSignatureFragments.Parameter(second, "A");
        second.ResolvedReceiverType = ExtensionSignatureFragments.Construct(pair, a, a);
        var right = f.Method(second, "Pick");
        var b = ExtensionSignatureFragments.Parameter(right, "B");
        right.ParametersList.Add(new Parameter("first", a));
        right.ParametersList.Add(new Parameter("second", b));

        _ = Assert.Single(ExtensionSignatureDecision.Repeats([first, second]));
    }

    [Fact]
    public void BlockAndMethodParametersHaveIndependentIdentitiesAndArities()
    {
        var f = new ExtensionSignatureFragments();
        var box = f.Built.Type("Box<>");
        var first = f.Block();
        var t = ExtensionSignatureFragments.Parameter(first, "T");
        first.ResolvedReceiverType = ExtensionSignatureFragments.Construct(box, t);
        var blockArgument = f.Method(first, "Pick", parameters: [new Parameter("x", t)]);
        var methodArgument = f.Method(first, "Pick");
        var u = ExtensionSignatureFragments.Parameter(methodArgument, "T");
        methodArgument.ParametersList.Add(new Parameter("x", u));
        var genericBlockArgument = f.Method(first, "Pick");
        _ = ExtensionSignatureFragments.Parameter(genericBlockArgument, "V");
        genericBlockArgument.ParametersList.Add(new Parameter("x", t));
        var twoGeneric = f.Method(first, "Pick");
        _ = ExtensionSignatureFragments.Parameter(twoGeneric, "A");
        _ = ExtensionSignatureFragments.Parameter(twoGeneric, "B");
        twoGeneric.ParametersList.Add(new Parameter("x", t));

        Assert.Empty(ExtensionSignatureDecision.Repeats([first]));
        Assert.NotEqual(blockArgument.ParametersList[0].Type, methodArgument.ParametersList[0].Type);
    }

    [Fact]
    public void ReceiverPatternsAreStructuralRatherThanCallSiteOverlaps()
    {
        var f = new ExtensionSignatureFragments();
        var pair = f.Built.Type("Pair<,>");
        var repeated = f.Block();
        var t = ExtensionSignatureFragments.Parameter(repeated, "T");
        repeated.ResolvedReceiverType = ExtensionSignatureFragments.Construct(pair, t, t);
        _ = f.Method(repeated, "Pick");

        var independent = f.Block();
        var a = ExtensionSignatureFragments.Parameter(independent, "A");
        var b = ExtensionSignatureFragments.Parameter(independent, "B");
        independent.ResolvedReceiverType = ExtensionSignatureFragments.Construct(pair, a, b);
        _ = f.Method(independent, "Pick");

        var concrete = f.Block(ExtensionSignatureFragments.Construct(pair, f.Integer, f.Integer));
        _ = f.Method(concrete, "Pick");
        var universal = f.Block(UnknownType.Instance);
        _ = f.Method(universal, "Pick");

        Assert.Empty(ExtensionSignatureDecision.Repeats([repeated, independent, concrete, universal]));
    }

    [Fact]
    public void UnusedBlockParameterDoesNotChangeAReceiverSignature()
    {
        var f = new ExtensionSignatureFragments();
        var first = f.Block(f.Integer);
        _ = f.Method(first, "Pick");
        var second = f.Block(f.Integer);
        _ = ExtensionSignatureFragments.Parameter(second, "Unused");
        _ = f.Method(second, "Pick");

        _ = Assert.Single(ExtensionSignatureDecision.Repeats([first, second]));
    }

    [Fact]
    public void OrdinaryPropertiesRepeatWithinAndAcrossBlocks()
    {
        var f = new ExtensionSignatureFragments();
        var first = f.Block(f.Integer);
        var second = f.Block(f.Integer);
        var original = f.Property(first, "Name");
        var sameBlock = f.Property(first, "Name", f.String);
        var nextBlock = f.Property(second, "Name");

        var repeats = ExtensionSignatureDecision.Repeats([first, second]).ToList();
        Assert.Equal(2, repeats.Count);
        Assert.All(repeats, repeat => Assert.True(repeat.IsProperty));
        Assert.All(repeats, repeat => Assert.Same(original.DeclarationNode, repeat.Earlier));
        Assert.Same(sameBlock.DeclarationNode, repeats[0].Later);
        Assert.Same(nextBlock.DeclarationNode, repeats[1].Later);
    }

    [Fact]
    public void AlphaEquivalentPropertyReceiversRepeatAcrossBlocks()
    {
        var f = new ExtensionSignatureFragments();
        var box = f.Built.Type("Box<>");
        var first = f.Block();
        var t = ExtensionSignatureFragments.Parameter(first, "T");
        first.ResolvedReceiverType = ExtensionSignatureFragments.Construct(box, t);
        var original = f.Property(first, "Name");
        var second = f.Block();
        var u = ExtensionSignatureFragments.Parameter(second, "U");
        second.ResolvedReceiverType = ExtensionSignatureFragments.Construct(box, u);
        var repeated = f.Property(second, "Name", f.String);

        var repeat = Assert.Single(ExtensionSignatureDecision.Repeats([first, second]));
        Assert.True(repeat.IsProperty);
        Assert.Same(original.DeclarationNode, repeat.Earlier);
        Assert.Same(repeated.DeclarationNode, repeat.Later);
    }

    [Fact]
    public void NamespacesAmbientAndUnresolvedTypesDoNotCreateSpeculativeRepeats()
    {
        var f = new ExtensionSignatureFragments();
        var first = f.Block(f.Integer, "First");
        _ = f.Method(first, "Pick", parameters: [new Parameter("x", f.Integer)]);
        var otherNamespace = f.Block(f.Integer, "Second");
        _ = f.Method(otherNamespace, "Pick", parameters: [new Parameter("x", f.Integer)]);
        var ambient = f.Block(f.Integer, "First", source: false);
        _ = f.Method(ambient, "Pick", parameters: [new Parameter("x", f.Integer)]);
        var errorReceiver = f.Block(ErrorType.Instance, "First");
        _ = f.Method(errorReceiver, "Pick", parameters: [new Parameter("x", f.Integer)]);
        var errorParameter = f.Block(f.Integer, "First");
        _ = f.Method(errorParameter, "Pick", parameters: [new Parameter("x", ErrorType.Instance)]);

        Assert.Empty(ExtensionSignatureDecision.Repeats([first, otherNamespace, ambient, errorReceiver, errorParameter]));
    }
}

[Trait("Tier", "2")]
public sealed class ExtensionSignatureAnalysisTests
{
    [Fact]
    public void AnalysisReportsLaterLocationAndEarlierLocationInMessage()
    {
        var f = new ExtensionSignatureFragments();
        var block = f.Block(f.Integer);
        var earlier = f.Method(block, "Pick", parameters: [new Parameter("a", f.Integer)]);
        var later = f.Method(block, "Pick", parameters: [new Parameter("b", f.Integer)]);

        var finding = Assert.Single(f.Built.Analyze(new DeclarationShapeAnalyzer(), extensions: [block]));
        Assert.Equal("MIST0134", finding.Number.Value);
        Assert.Equal(later.DeclarationNode!.Span.Start + 1, finding.Position().Start.Column);
        Assert.Contains($"hand-built.mist:1:{earlier.DeclarationNode!.Span.Start + 1}", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalysisReportsRepeatedPropertyAtTheLaterDeclaration()
    {
        var f = new ExtensionSignatureFragments();
        var block = f.Block(f.Integer);
        var earlier = f.Property(block, "Name");
        var later = f.Property(block, "Name", f.String);

        var finding = Assert.Single(f.Built.Analyze(new DeclarationShapeAnalyzer(), extensions: [block]));
        Assert.Equal("MIST0135", finding.Number.Value);
        Assert.Equal(later.DeclarationNode!.Span.Start + 1, finding.Position().Start.Column);
        Assert.Contains($"hand-built.mist:1:{earlier.DeclarationNode!.Span.Start + 1}", finding.Message, StringComparison.Ordinal);
    }
}