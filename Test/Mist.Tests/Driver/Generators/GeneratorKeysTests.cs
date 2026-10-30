using Mist.Driver.Generators;

namespace Mist.Tests.Driver.Generators;

[Trait("Tier", "1")]
public sealed class GeneratorKeysTests
{
    [Fact]
    public void SectionsAndStringBoundariesAreUnambiguous()
    {
        Assert.NotEqual(Run(["ab", "c"], []), Run(["a", "bc"], []));
        Assert.NotEqual(Run(["a", "b"], []), Run([], [new ContentHash("a", "b")]));
        Assert.NotEqual(Run(["a", "b"], []), Run(["b", "a"], []));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void EveryRunIngredientChangesTheKey(int ingredient)
    {
        var baseline = GeneratorKeys.Run("tool", "compiled", "consumer", "/manifest", ["arg"], [new ContentHash("input", "hash")]);
        var changed = GeneratorKeys.Run(ingredient == 0 ? "other" : "tool", ingredient == 1 ? "other" : "compiled",
            ingredient == 2 ? "other" : "consumer", ingredient == 3 ? "/other" : "/manifest",
            [ingredient == 4 ? "other" : "arg"], [new ContentHash("input", ingredient == 5 ? "other" : "hash")]);
        Assert.NotEqual(baseline, changed);
        Assert.NotEqual(baseline, GeneratorKeys.Run("tool", "compiled", "consumer", "/manifest", ["arg"], [new ContentHash("other", "hash")]));
    }

    [Fact]
    public void InputOrderAndCompileIngredientsEnterTheirOwnKeys()
    {
        ContentHash[] sources = [new("manifest", "m"), new("source", "s")];
        ContentHash[] references = [new("reference", "r")];
        var baseline = GeneratorKeys.Compile("tool", sources, references, "host");
        Assert.NotEqual(baseline, GeneratorKeys.Compile("other", sources, references, "host"));
        Assert.NotEqual(baseline, GeneratorKeys.Compile("tool", sources.Reverse(), references, "host"));
        Assert.NotEqual(baseline, GeneratorKeys.Compile("tool", [new("manifest", "changed"), sources[1]], references, "host"));
        Assert.NotEqual(baseline, GeneratorKeys.Compile("tool", [sources[0], new("source", "changed")], references, "host"));
        Assert.NotEqual(baseline, GeneratorKeys.Compile("tool", sources, [new("reference", "changed")], "host"));
        Assert.NotEqual(baseline, GeneratorKeys.Compile("tool", sources, references, "other-host"));
        Assert.NotEqual(Run([], sources), Run([], sources.Reverse()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ToolchainIncludesEverySection(int section)
    {
        ContentHash[] files = [new("path", "hash")];
        var baseline = GeneratorKeys.Toolchain(files, files, files, "runtime");
        ContentHash[] changed = [new("path", "other")];
        Assert.NotEqual(baseline, GeneratorKeys.Toolchain(section == 0 ? changed : files,
            section == 1 ? changed : files, section == 2 ? changed : files, section == 3 ? "other" : "runtime"));
        Assert.Equal(baseline, GeneratorKeys.Toolchain(files, files, files, "runtime"));
    }

    private static string Run(IEnumerable<string> arguments, IEnumerable<ContentHash> inputs)
    {
        return GeneratorKeys.Run("tool", "generator", "consumer", "/manifest", arguments, inputs);
    }
}