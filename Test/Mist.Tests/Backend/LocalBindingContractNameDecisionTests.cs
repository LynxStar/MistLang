using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Model;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class LocalBindingContractNameDecisionTests
{
    private readonly HandBuiltModel model = new();

    [Fact]
    public void HiddenDiscardsKeepDistinctStandaloneNamesForTheirSemanticIdentities()
    {
        var first = new LocalVariable("_", model.Type("Int32")) { IsDiscard = true };
        var second = new LocalVariable("_", first.Type) { IsDiscard = true };

        var firstName = LocalNames.Of(first);
        var secondName = LocalNames.Of(second);

        Assert.StartsWith("__discard_", firstName, StringComparison.Ordinal);
        Assert.StartsWith("__discard_", secondName, StringComparison.Ordinal);
        Assert.NotEqual(firstName, secondName);
        Assert.Equal(firstName, LocalNames.Of(first));
        Assert.Equal(secondName, LocalNames.Of(second));
    }

    [Theory]
    [InlineData("_", "_")]
    [InlineData("value", "value")]
    public void SourceLocalsWithoutTheDiscardMarkerKeepTheirStandaloneSpelling(string name, string expected)
    {
        var local = new LocalVariable(name, model.Type("Int32"));

        Assert.Equal(expected, LocalNames.Of(local));
    }
}