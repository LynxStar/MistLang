using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

/// <summary>
/// Tier 3: how a flat type-argument list is divided among slots when one of them absorbs. A family is
/// registered under its SLOT count, so one declaration serves every arity a use site writes.
/// </summary>
public sealed class PackBindingTests : ProgramModelProbe
{
    public PackBindingTests()
        : base("MistPackBinding_")
    {
    }

    protected override string Fixtures { get; } = """
        public class F<params TArgs, TResult> { }
        public class Between<TFirst, params TMid, TLast> { }
        public class TwoPacks<Type[]:A, Type[]:B> { }
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
    public void EmptyPack_IsWellFormed()
    {
        Assert.Equal(["[]", "Int32"], Slots("F<int>"));
    }

    [Fact]
    public void ArityMatchingTheSlotCount_StillBinds()
    {
        // TArgs is bound to a list holding int, not to a bare int, though the counts match.
        Assert.Equal(["[Int32]", "String"], Slots("F<int, string>"));
    }

    [Fact]
    public void SurplusArguments_GoToThePack()
    {
        Assert.Equal(["[Int32, String]", "Boolean"], Slots("F<int, string, bool>"));
    }

    [Fact]
    public void MiddlePack_BindsInwardFromBothEnds()
    {
        Assert.Equal(
            ["Int32", "[String, Boolean]", "Double"],
            Slots("Between<int, string, bool, double>"));
    }

    [Fact]
    public void PackOrderAndLength_BothCount()
    {
        Assert.NotEqual(Slots("F<int, string, bool>"), Slots("F<string, int, bool>"));
        Assert.NotEqual(Slots("F<int, bool>"), Slots("F<int, string, bool>"));
    }

    /// <summary>
    /// The two spellings are ONE type, not two that agree. Which is the whole reason a bracketed
    /// argument is adopted rather than wrapped: wrapping would nest a pack inside a pack and make
    /// these differ, while resolving and emitting perfectly well.
    /// </summary>
    [Fact]
    public void BracketedAndFlatConstruction_AreTheSameType()
    {
        Assert.Equal(Slots("F<int, string, bool>"), Slots("F<[int, string], bool>"));
    }

    [Fact]
    public void BracketsSupplyAnEmptyPackExplicitly()
    {
        Assert.Equal(["[]", "Boolean"], Slots("F<[], bool>"));
    }

    /// <summary>
    /// Brackets are what let a list carry more than one pack: with each supplied directly, the
    /// argument count matches the slot count and nothing has to work out where one pack ends.
    /// </summary>
    [Fact]
    public void SeveralBracketedPacks_AreEachSuppliedDirectly()
    {
        Assert.Equal(["[Int32, Int32]", "[String]"], Slots("TwoPacks<[int, int], [string]>"));
    }

    /// <summary>
    /// A pack makes the count flexible upward only, so the slots around it are a floor. Reported as
    /// the shortfall it is rather than as an unresolved name, which needs lookup to probe past the
    /// arities the arguments could fill -- bounded by the widest variadic family the program
    /// actually declares, since any fixed ceiling is wrong for some program.
    /// </summary>
    [Fact]
    public void TooFewForTheFixedSlots_IsRefusedAsAShortfall()
    {
        _ = BuildModel(
            statement: "",
            extraTypes: "public class Holder { public Between<int> f; }",
            extraPreamble: "",
            out var diagnostics);

        var error = Assert.Single(Errors(diagnostics, "MIST0411"));
        Assert.Contains("needs at least 2 type arguments", error.Message);
    }

    [Fact]
    public void JustEnoughForTheFixedSlots_BindsAnEmptyPack()
    {
        Assert.Equal(["Int32", "[]", "Boolean"], Slots("Between<int, bool>"));
    }
}