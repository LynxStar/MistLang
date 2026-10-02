using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

// Tier 3: a union over a pack element has no C# spelling in a declaration emitted once per arity, so
// the backend refuses it where the specializations are discovered. An optional over an element whose
// pack is constrained to one kind is spelled per arity and is left alone.
[Trait("Tier", "3")]
public sealed class PackElementUnionRefusalTests : ProgramModelProbe
{
    public PackElementUnionRefusalTests()
        : base("MistPackElementUnion_")
    {
    }

    protected override string Fixtures { get; } = "public class Box<T> { public T? Value; public Box() { } }";

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("public class Wrap<Type[2]:TArgs> { public TArgs[1]? Maybe; public Wrap() { } }", "TArgs[1]")]
    [InlineData("public class Wrap<Type[2]:TArgs> { public TArgs[0] | string Either; public Wrap(TArgs[0] e) { Either = e; } }", "TArgs[0]")]
    [InlineData("public class Wrap<Type[2]:TArgs> { public Box<TArgs[1]> Held; public Wrap() { Held = new Box<TArgs[1]>(); } }", "TArgs[1]")]
    [InlineData("public class Wrap { public static int Use<Type[2]:T>(T[0]? maybe) { return 0; } }", "T[0]")]
    public void AUnionOverAnOpenPackElement_IsRefusedNamingTheElement(string declaration, string element)
    {
        var refusal = Assert.Single(Refusals(declaration));

        Assert.Contains($"it names '{element}'", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public class Wrap<Type[2]:TArgs> where TArgs : struct { public TArgs[1]? Maybe; public Wrap() { } }")]
    [InlineData("public class Wrap<Type[2]:TArgs> where TArgs : class { public TArgs[1]? Maybe; public Wrap() { } }")]
    [InlineData("public class Wrap<Type[2]:TArgs> { public TArgs[1] Plain; public Wrap(TArgs[1] p) { Plain = p; } }")]
    public void AnElementWhoseSpellingIsFixedPerArity_IsNot(string declaration)
    {
        Assert.Empty(Refusals(declaration));
    }

    private List<Diagnostic> Refusals(string declaration)
    {
        var model = BuildModel("", declaration, "", out var built);
        var diagnostics = new DiagnosticBag();

        Assert.Empty(built.Where(d => d.Severity == DiagnosticSeverity.Error));

        using var specialization = UnionSpecialization.Enter(model, diagnostics);

        return [.. diagnostics.ToImmutableArray().Where(d => d.Id.Value == "MIST_BE_UNION_OVER_PACK_ELEMENT")];
    }
}