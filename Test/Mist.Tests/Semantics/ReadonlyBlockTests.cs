using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

/// <summary>
/// Tier 3: <c>readonly { ... }</c> binds as a block that REMEMBERS it was readonly.
/// <para>
/// THE FLAG IS THE WHOLE TEST, because emission drops it -- C# has no readonly block, and classic
/// emits a plain one. So no output can tell a model that kept the fact from one that threw it away,
/// and the rule it exists for (what the block may mutate) is a judgement on a finished model that
/// nothing runs yet. Losing it would be invisible until someone came to write that rule and found
/// the model could not answer.
/// </para>
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

    /// <summary>
    /// The other half: an ordinary nested block must NOT come back marked, or the flag says nothing.
    /// </summary>
    [Fact]
    public void AnOrdinaryBlockIsNotMarked()
    {
        Assert.False(NestedBlockIn("{ var inner = 55; }").IsReadonly);
    }

    /// <summary>
    /// Bindings inside are ordinary bindings: the block scopes a promise about mutation, not about
    /// what its statements mean.
    /// </summary>
    [Fact]
    public void StatementsInsideBindNormally()
    {
        var block = NestedBlockIn("readonly { readonly var inner = 55; var used = inner; }");

        Assert.Equal(2, block.Statements.OfType<LocalDeclarationStatement>().Count());
        Assert.Empty(BuildRun("readonly { readonly var inner = 55; var used = inner; }")
            .Where(d => d.Severity == DiagnosticSeverity.Error));
    }
}