using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

/// <summary>
/// Tier 3: which specializations a program actually asks for.
/// </summary>
public sealed class MonomorphSetTests : ProgramModelProbe
{
    public MonomorphSetTests()
        : base("MistMonomorph_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Box<params TArgs> { }
        public class Pair<Type[]:A, Type[]:B> { }
        public class Fixed<T> { }
        """;

    protected override string Preamble { get; } = "";

    private IReadOnlyList<string> Discovered(string fields)
    {
        var model = BuildModel(
            statement: "",
            extraTypes: $"public class Holder {{ {fields} }}",
            extraPreamble: "",
            out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        return [.. MonomorphSet.Discover(model).Types
            .Select(entry => $"{entry.Definition.Name}/{string.Join("_", entry.Widths)}")];
    }

    [Fact]
    public void EachArityIsDiscoveredOnce()
    {
        Assert.Equal(
            ["Box<>/1", "Box<>/2"],
            Discovered("public Box<int> a; public Box<int, string> b; public Box<bool, char> c;"));
    }

    [Fact]
    public void TheEmptyPackIsAnArityLikeAnyOther()
    {
        Assert.Equal(["Box<>/0"], Discovered("public Box a;"));
    }

    /// <summary>
    /// A declaration may carry more than one pack, and the widths are discovered TOGETHER: a use at
    /// (2, 1) asks for that one declaration, not for the four a cross product of the widths seen
    /// per slot would name.
    /// </summary>
    [Fact]
    public void PacksOfOneDeclarationAreDiscoveredAsACombination()
    {
        Assert.Equal(
            ["Pair<,>/1_2", "Pair<,>/2_1"],
            Discovered("public Pair<[int, int], [string]> a; public Pair<[bool], [int, char]> b;"));
    }

    /// <summary>
    /// A family with no pack needs no specialization: C# already expresses it.
    /// </summary>
    [Fact]
    public void OrdinaryGenericsAreNotSpecialized()
    {
        Assert.Empty(Discovered("public Fixed<int> a; public Fixed<string> b;"));
    }
}