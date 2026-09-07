using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

/// <summary>
/// Tier 3: which specializations a program actually asks for.
/// <para>
/// PER ARITY, NOT PER CONSTRUCTION. C# has no pack, so one variadic declaration becomes one type
/// per arity -- and Box&lt;int, string&gt; and Box&lt;bool, char&gt; want the SAME one, a generic of
/// two parameters, differing only in what they pass it. Specializing per construction would emit a
/// type per distinct argument list and throw away genericity C# can express perfectly well.
/// </para>
/// </summary>
public sealed class MonomorphSetTests : ProgramModelProbe
{
    public MonomorphSetTests()
        : base("MistMonomorph_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Box<params TArgs> { }
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

        return [.. MonomorphSet.Discover(model.TypeInterner)
            .Select(entry => $"{entry.Definition.Name}/{entry.Arity}")];
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
    /// A family with no pack needs no specialization: C# already expresses it.
    /// </summary>
    [Fact]
    public void OrdinaryGenericsAreNotSpecialized()
    {
        Assert.Empty(Discovered("public Fixed<int> a; public Fixed<string> b;"));
    }
}