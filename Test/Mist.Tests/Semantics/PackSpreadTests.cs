using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

/// <summary>
/// Tier 3: a pack spread into a type-argument list expands in place.
/// <para>
/// SPREADING IS WHAT MAKES A VARIADIC ALIAS MEAN ANYTHING. `type A&lt;params TArgs&gt; =
/// F&lt;TArgs..., void&gt;` is the standard library's own shape -- Action is written this way over
/// Func -- and without expansion the spread resolves to the pack ITSELF, so A&lt;int, string&gt;
/// becomes F with a slot holding a list holding a type parameter, rather than F over int and
/// string. That resolves and emits and is wrong, which is why it is asserted against the direct
/// spelling rather than against a shape.
/// </para>
/// </summary>
public sealed class PackSpreadTests : ProgramModelProbe
{
    public PackSpreadTests()
        : base("MistPackSpread_")
    {
    }

    protected override string Fixtures { get; } = """
        public class F<params TArgs, TResult> { }
        public type A<params TArgs> = F<TArgs..., void>;
        """;

    protected override string Preamble { get; } = "";

    private IReadOnlyList<string> Slots(string fieldType)
    {
        var model = BuildModel(
            statement: "",
            extraTypes: $"public class Holder {{ public {fieldType} f; }}",
            extraPreamble: "",
            out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var holder = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Holder"]);
        var constructed = Assert.IsType<ConstructedType>(holder.FieldsList[0].Type);

        return [.. constructed.TypeArguments.Select(a => a.Name)];
    }

    [Fact]
    public void SpreadIntoATypeArgumentList_ExpandsInPlace()
    {
        Assert.Equal(Slots("F<int, string, void>"), Slots("A<int, string>"));
    }

    [Fact]
    public void EmptyPackSpread_ExpandsToNothing()
    {
        // Written bare: the grammar has no empty argument list, so absorbing nothing is spelled
        // by writing no arguments at all.
        Assert.Equal(Slots("F<void>"), Slots("A"));
    }

    /// <summary>
    /// A spread in MEMBER position is one value, not several, so the member holds the tuple its
    /// pack expands to. Substitution then fills it in: the elements arrive where the spread stood.
    /// </summary>
    [Fact]
    public void SpreadMember_HoldsTheTupleItsPackExpandsTo()
    {
        var model = BuildModel(
            statement: "",
            extraTypes: "public class Bundle<params TArgs> { public TArgs... Item; }"
                + " public class Holder { public Bundle<int, string, bool> b; }",
            extraPreamble: "",
            out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var holder = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Holder"]);
        var bundle = Assert.IsType<ConstructedType>(holder.FieldsList[0].Type);
        var definition = Assert.IsType<ClassType>(bundle.Definition);

        var closed = TypeSubstitution.ForReceiver(bundle)
            .Apply(definition.FieldsList[0].Type, model.TypeInterner);

        Assert.Equal(
            ["Int32", "String", "Boolean"],
            Assert.IsType<TupleType>(closed).ElementTypes.Select(e => e.Name));
    }

    private ImmutableArray<Diagnostic> Stems(string body)
    {
        _ = BuildModel(
            statement: "",
            extraTypes: $"public class S<params TArgs> {{ {body} }}",
            extraPreamble: "",
            out var diagnostics);

        return diagnostics;
    }

    /// <summary>
    /// A spread reserves a name FAMILY, not a name. How many projections it has is unknown where it
    /// is declared, so the whole prefixed namespace has to be held rather than the names that
    /// happen to exist for one instantiation.
    /// </summary>
    [Theory]
    [InlineData("public TArgs... Item; public int Item1;")]
    [InlineData("public int Item1; public TArgs... Item;")]
    [InlineData("public TArgs... Item; public TArgs... ItemExtra;")]
    public void NameASpreadCouldClaim_IsRefused(string body)
    {
        _ = Assert.Single(Errors(Stems(body), "MIST_VAR_0007"));
    }

    /// <summary>
    /// The reservation stops at what the family can actually name: a member merely starting with
    /// the stem is not one of its projections.
    /// </summary>
    [Fact]
    public void NameTheFamilyCannotClaim_IsAllowed()
    {
        Assert.Empty(Errors(Stems("public TArgs... Item; public int ItemName; public int Other;"), "MIST_VAR_0007"));
    }

    [Fact]
    public void SpreadPreservesOrder()
    {
        Assert.NotEqual(Slots("A<int, string>"), Slots("A<string, int>"));
    }
}