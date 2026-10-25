// Tier 3 (Component): the binder's purity placement refusals over the real model builder with stub ambient
// types, since a placement is decided from the declaration's kind and its declaring type together, which
// only the builder's own walk supplies.

using Mist.Common.Diagnostics;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "3")]
public sealed class PurityBindingRefusalTests : ProgramModelProbe
{
    public PurityBindingRefusalTests()
        : base("MistPurityBinding_")
    {
    }

    protected override string Fixtures { get; } = "";

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("local-on-a-constructor", "public class Counter { public int Count; public local Counter() { } }", "", "MIST0320")]
    [InlineData("local-on-a-static-member", "public class Ops { public static local int Twice(int x) { return x * 2; } }", "", "MIST0320")]
    [InlineData("pure-and-local-on-one-member", "public class Ops { public pure local int Twice(int x) { return x * 2; } }", "", "MIST0318")]
    [InlineData("pure-on-a-field", "public class Ops { public pure int Count; }", "", "MIST0321")]
    [InlineData("pure-on-a-non-function-type", "public class Counter { public int Count; public Counter() { } }", "pure Counter counter = new Counter();", "MIST0322")]
    public void BinderRefusesNamedCase(string caseName, string types, string statement, string expected)
    {
        var error = Assert.Single(BuildRun(statement, types), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        Assert.True(error.Number.Value == expected, $"{caseName}: expected {expected}, got {error.Number.Value}");
    }
}