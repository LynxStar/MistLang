using Mist.Common.Diagnostics;
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

    [Fact]
    public void SpreadPreservesOrder()
    {
        Assert.NotEqual(Slots("A<int, string>"), Slots("A<string, int>"));
    }
}