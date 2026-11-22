using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.RuntimeDependentValues;

/// <summary>
/// A switch arm that tests a closing differing from its constituent only in a dependent slot compares the slot when
/// the program runs (runtime-dependent-values Requirement 9.2), so the exhaustiveness rule reads it as a guard: it
/// covers the constituent only where both slots are one refinement -- the same capture, literal, refinement argument or
/// parameter -- and otherwise MIST0148 names the constituent, as <c>Ring&lt;n&gt;</c> over <c>Ring&lt;m&gt; | string</c>
/// would otherwise throw when n and m differ. Two captures of one holder are two refinements, since a path condition
/// between them can narrow one. Judged by the exhaustiveness analyzer over hand-built bodies.
/// </summary>
[Trait("Tier", "2")]
public sealed class SlotTestCoverageTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly MemberType ring;
    private readonly LocalVariable n;
    private readonly LocalVariable m;

    public SlotTestCoverageTests()
    {
        n = new LocalVariable("n", slots.Int);
        m = new LocalVariable("m", slots.Int);
        ring = HandBuiltSlots.Generic(HandBuiltModel.Class("Ring<>"), "Capacity", slots.Int);
    }

    [Fact]
    public void AUnionConstituentReachedOnlyByAnotherValuesTestIsMissing()
    {
        var missing = Assert.Single(Analyze(Union(m, n)));

        Assert.Equal("MIST0148", missing.Number.Value);
        Assert.Contains("Missing cases: Ring<m>", missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AUnionConstituentTestedThroughAnotherCaptureOfTheSameUnwrittenHolderIsMissing()
    {
        var missing = Assert.Single(Analyze(Union(n, n)));

        Assert.Contains("Missing cases: Ring<n>", missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AUnionConstituentTestedThroughAHolderTheBodyWritesIsMissing()
    {
        var written = HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(HandBuiltRefinements.Read(n), slots.Refinements.Literal(1L)));
        var missing = Assert.Single(Analyze([.. Union(n, n), written]));

        Assert.Contains("Missing cases: Ring<n>", missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APlainScrutineeCoveredOnlyByAnotherCapturesTestIsMissingTheRemainder()
    {
        Assert.Contains("Missing cases: _", Assert.Single(Analyze(Plain(m, n))).Message, StringComparison.Ordinal);
        Assert.Contains("Missing cases: _", Assert.Single(Analyze(Plain(n, n))).Message, StringComparison.Ordinal);
    }

    private IReadOnlyList<Diagnostic> Analyze(params Statement[] statements)
    {
        return slots.Refinements.Built.Analyze(new ExhaustivenessAnalyzer(), HandBuiltModel.Block(statements));
    }

    private Statement[] Union(LocalVariable held, LocalVariable tested)
    {
        var ofHeld = slots.Capture(held, slots.Int);
        var ofTested = slots.Capture(tested, slots.Int);
        var constituent = slots.Closed(ring, ofHeld.Value);
        var union = new UnionType([constituent, slots.Refinements.String]);
        var a = new LocalVariable("a", union);
        var arms = new[]
        {
            Arm(new TypeTestPattern(HandBuiltModel.Node(), slots.Closed(ring, ofTested.Value), null, constituent, null) { Captures = [ofTested] }, 1L),
            Arm(new TypeTestPattern(HandBuiltModel.Node(), slots.Refinements.String, null, slots.Refinements.String, null), 2L),
        };

        return [HandBuiltSlots.Declare(a, HandBuiltRefinements.Local(union, "source"), ofHeld), Switched(a, arms)];
    }

    private Statement[] Plain(LocalVariable held, LocalVariable tested)
    {
        var ofHeld = slots.Capture(held, slots.Int);
        var ofTested = slots.Capture(tested, slots.Int);
        var b = new LocalVariable("b", slots.Closed(ring, ofHeld.Value));
        var arm = Arm(new TypeTestPattern(HandBuiltModel.Node(), slots.Closed(ring, ofTested.Value), null, null, null) { Captures = [ofTested] }, 1L);

        return [HandBuiltSlots.Declare(b, HandBuiltRefinements.Local(b.Type, "source"), ofHeld), Switched(b, [arm])];
    }

    private SwitchArm Arm(Pattern pattern, long result)
    {
        return new SwitchArm(HandBuiltModel.Node(), pattern, null, slots.Refinements.Literal(result));
    }

    private ExpressionStatement Switched(LocalVariable scrutinee, IReadOnlyList<SwitchArm> arms)
    {
        return HandBuiltRefinements.Evaluate(new SwitchExpression(HandBuiltModel.Node(), HandBuiltRefinements.Read(scrutinee), arms, slots.Int));
    }
}