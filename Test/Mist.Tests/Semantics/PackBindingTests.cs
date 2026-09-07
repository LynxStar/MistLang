using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

/// <summary>
/// Tier 3: how a flat type-argument list is divided among slots when one of them absorbs.
/// <para>
/// THE COUNT NO LONGER DETERMINES THE DECLARATION, which is the whole difficulty. A family is
/// registered under its SLOT count, so `Func&lt;,&gt;` is the only Func there is however many
/// arguments a use site writes -- and the arity that happens to match is not privileged: binding
/// applies to `Func&lt;int, int&gt;` exactly as it does to `Func&lt;int, int, int&gt;`, or the
/// two-argument case would silently bind a bare type into a slot that holds a list.
/// </para>
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

    /// <summary>
    /// The bound slots of the field's type, each rendered as either a plain type name or the
    /// bracketed pack it holds.
    /// </summary>
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
        // The case that would pass by accident under the old one-for-one rule, with TArgs bound to
        // a bare int rather than to a list holding one.
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
    /// Refused, though not yet as MIST_VAR_0005. Lookup finds a variadic family by trying the slot
    /// counts its arguments could fill, so a list too short to fill any of them names no
    /// declaration at all and is refused as an unresolved type. Saying "too few arguments" instead
    /// needs lookup to find the family by STEM, which nothing indexes yet.
    /// </summary>
    [Fact]
    public void TooFewForTheFixedSlots_IsRefused()
    {
        _ = BuildModel(
            statement: "",
            extraTypes: "public class Holder { public Between<int> f; }",
            extraPreamble: "",
            out var diagnostics);

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_UNRESOLVED_TYPE"));
    }
}