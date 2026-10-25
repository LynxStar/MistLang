using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class InitializerBlockTests
{
    private readonly HandBuiltModel model = new();

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("Value,Value", "1")]
    [InlineData("Value,Count,Value,Value", "2,3")]
    [InlineData("Value,Count", "")]
    [InlineData("!Value,Value", "")]
    [InlineData("?Value,?Value", "")]
    [InlineData("+,+", "")]
    public void AMemberIsRepeatedFromItsSecondAssignmentInOneBlock(string entries, string repeated)
    {
        var block = entries.Split(',').Select(Entry).ToList();

        var found = InitializerAnalyzer.Repeated(block).Select(entry => block.IndexOf(entry));

        Assert.Equal(repeated, string.Join(",", found));
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("Value,+,Count,+", "1,3")]
    [InlineData("+,Value,+,!Count", "1,3")]
    [InlineData("+,+", "")]
    [InlineData("Value,Count", "")]
    public void AnEntryOfTheOtherKindThanTheBlocksFirstIsMixed(string entries, string mixed)
    {
        var block = entries.Split(',').Select(Entry).ToList();

        var found = InitializerAnalyzer.Mixed(block).Select(entry => block.IndexOf(entry));

        Assert.Equal(mixed, string.Join(",", found));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void AMixedEntryIsRefusedAtItself()
    {
        var creation = Box(Entry("Count", offset: 10), Entry("+", offset: 24));

        var diagnostic = Assert.Single(model.Analyze(new InitializerAnalyzer(), creation));

        Assert.Equal("MIST_PA_INITIALIZER_KINDS_MIXED", diagnostic.Number.Value);
        Assert.Contains("this entry adds a collection element, but the block's first entry assigns 'Count'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(25, diagnostic.Location.Start.Column);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ARepeatedMemberIsRefusedAtItsRepetition()
    {
        var creation = Box(Entry("Value", offset: 10), Entry("Value", offset: 24));

        var diagnostic = Assert.Single(model.Analyze(new InitializerAnalyzer(), creation));

        Assert.Equal("MIST_PA_INITIALIZER_MEMBER_REPEATED", diagnostic.Number.Value);
        Assert.Equal("Member 'Value' is assigned more than once in this initializer block.", diagnostic.Message);
        Assert.Equal(25, diagnostic.Location.Start.Column);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ANestedConstructionsBlockAnswersOnlyForItself()
    {
        var child = Box(Entry("Value", offset: 10));
        var creation = Box(new ObjectInitializerAssignment(HandBuiltModel.Node(3), "Child", child, child.Type), Entry("Value", offset: 24));

        Assert.Empty(model.Analyze(new InitializerAnalyzer(), creation));
    }

    private ObjectInitializerAssignment Entry(string written, int offset)
    {
        var value = HandBuiltModel.Literal(Integer, 1L, offset);

        return written[0] switch
        {
            '+' => new ObjectInitializerAssignment(HandBuiltModel.Node(offset), null, value, null, new Method("Add", Integer, [new Parameter("item", Integer)], default)),
            '!' => new ObjectInitializerAssignment(HandBuiltModel.Node(offset), written[1..], value, null) { IsTargetFailed = true },
            '?' => new ObjectInitializerAssignment(HandBuiltModel.Node(offset), written[1..], value, null),
            _ => new ObjectInitializerAssignment(HandBuiltModel.Node(offset), written, value, Integer),
        };
    }

    private IMistType Integer => model.Type("Int32");

    private static NewExpression Box(params ObjectInitializerAssignment[] entries)
    {
        return new NewExpression(HandBuiltModel.Node(3), HandBuiltModel.Class("Box"), [], null, entries, []);
    }
}