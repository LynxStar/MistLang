using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

/// <summary>
/// Tier 3: which domain a type parameter ranges over.
/// <para>
/// THE QUESTION USED TO HAVE NO ANSWER AT ALL. Domain was declared for exactly this and never
/// populated, so a consumer could ask only WHETHER a parameter was dependent, never over what --
/// which is why the emitter's refusal of dependent parameters had to be keyed on a syntactic flag.
/// Asserting the domain by name is the cheapest thing that fails if the resolution is dropped.
/// </para>
/// </summary>
public sealed class TypeParameterDomainTests : ProgramModelProbe
{
    public TypeParameterDomainTests()
        : base("MistDomains_")
    {
    }

    protected override string Fixtures { get; } = "";

    protected override string Preamble { get; } = "";

    private string? Domain(string declaration, string typeName, int ordinal)
    {
        var model = BuildModel(statement: "", extraTypes: declaration, extraPreamble: "", out var diagnostics);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var type = Assert.IsAssignableFrom<IHasTypeParameters>(model.TypesByFQN[$"Probe.{typeName}"]);
        return type.TypeParametersList[ordinal].Domain?.FullyQualifiedName;
    }

    [Fact]
    public void OrdinaryParameter_RangesOverType()
    {
        Assert.Equal("Standard.Type", Domain("public class Box<T> { }", "Box<>", ordinal: 0));
    }

    [Theory]
    [InlineData("int", "Standard.Int32")]
    [InlineData("long", "Standard.Int64")]
    [InlineData("byte", "Standard.Byte")]
    [InlineData("bool", "Standard.Boolean")]
    [InlineData("char", "Standard.Char")]
    public void DependentParameter_RangesOverItsAnnotatedValueType(string keyword, string expected)
    {
        Assert.Equal(
            expected,
            Domain($"public class Buffer<Size:{keyword}> {{ }}", "Buffer<>", ordinal: 0));
    }

    [Fact]
    public void MixedList_ResolvesEachSlotSeparately()
    {
        const string declaration = "public class Grid<T, Rows:int> { }";

        Assert.Equal("Standard.Type", Domain(declaration, "Grid<,>", ordinal: 0));
        Assert.Equal("Standard.Int32", Domain(declaration, "Grid<,>", ordinal: 1));
    }

    [Fact]
    public void PackParameter_RangesOverAnArrayOfType()
    {
        // The keystone spelled out: `params TArgs` is the dependent parameter `Type[]:TArgs`, so its
        // domain has to be the array, not Type and not a kind of its own.
        Assert.Equal(
            "Standard.Array<Type, unknown>",
            Domain("public class F<params TArgs, TResult> { }", "F<,>", ordinal: 0));
    }

    [Fact]
    public void RefinementAlias_IsAdmittedByWhatItRefines()
    {
        // The spelling the standard library actually uses: Array's length is declared against the
        // alias, so rejecting it would refuse Standard itself.
        Assert.Equal(
            "Probe.Small",
            Domain("public type Small = int >= 0; public class B<N:Small> { }", "B<>", ordinal: 0));
    }

    [Theory]
    [InlineData("string")]
    [InlineData("float")]
    [InlineData("double")]
    [InlineData("decimal")]
    public void DomainThatCannotCarryIdentity_IsRefused(string keyword)
    {
        _ = BuildModel(
            statement: "", extraTypes: $"public class Bad<V:{keyword}> {{ }}", extraPreamble: "",
            out var diagnostics);

        _ = Assert.Single(Errors(diagnostics, "MIST0407"));
    }

    [Fact]
    public void FixedSizePack_PinsTheArityInItsDomain()
    {
        Assert.Equal(
            "Standard.Array<Type, 3>",
            Domain("public class K<Type[3]:TArgs> { }", "K<>", ordinal: 0));
    }
}