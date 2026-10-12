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

    [Fact]
    [Trait("Tier", "2")]
    public void ARepeatedMemberIsRefusedAtItsRepetition()
    {
        var creation = Box(Entry("Value", offset: 10), Entry("Value", offset: 24));

        var diagnostic = Assert.Single(model.Analyze(new InitializerAnalyzer(), creation));

        Assert.Equal("MIST_PA_INITIALIZER_MEMBER_REPEATED", diagnostic.Id.Value);
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