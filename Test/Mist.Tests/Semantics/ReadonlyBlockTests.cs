using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

/// <summary>
/// Tier 3: asserted on <see cref="Block.IsReadonly"/>, which emission drops.
/// </summary>
public sealed class ReadonlyBlockTests : ProgramModelProbe
{
    public ReadonlyBlockTests()
        : base("MistReadonlyBlock_")
    {
    }

    protected override string Fixtures { get; } = "";

    protected override string Preamble { get; } = "";

    private Block NestedBlockIn(string statement)
    {
        var body = BodyIn(statement);
        return Assert.Single(body.Statements.OfType<Block>());
    }

    [Fact]
    public void AReadonlyBlockBindsAndIsMarked()
    {
        var block = NestedBlockIn("readonly { var inner = 55; }");

        Assert.True(block.IsReadonly);
        Assert.Empty(block.Statements.OfType<UnsupportedStatement>());
    }

    [Fact]
    public void AnOrdinaryBlockIsNotMarked()
    {
        Assert.False(NestedBlockIn("{ var inner = 55; }").IsReadonly);
    }

    [Fact]
    public void StatementsInsideBindNormally()
    {
        var block = NestedBlockIn("readonly { readonly var inner = 55; var used = inner; }");

        Assert.Equal(2, block.Statements.OfType<LocalDeclarationStatement>().Count());
        Assert.Empty(BuildRun("readonly { readonly var inner = 55; var used = inner; }")
            .Where(d => d.Severity == DiagnosticSeverity.Error));
    }
}