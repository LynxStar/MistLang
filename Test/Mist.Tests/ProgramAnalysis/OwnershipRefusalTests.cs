// Tier 2 (Functional): ownership, lease, escape, discipline, tier and readonly rules judge hand-built fragments.

using Mist.ProgramAnalysis.Rules;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class OwnershipRefusalTests
{
    [Theory]
    [InlineData("access-while-borrowed", "MIST0229")]
    [InlineData("affine-declared-owned", "MIST0239")]
    [InlineData("affine-field-in-relevant-container", "MIST0240")]
    [InlineData("affine-used-after-drop", "MIST0227")]
    [InlineData("borrow-while-frozen", "MIST0229")]
    [InlineData("borrower-mutation-while-frozen", "MIST0229")]
    [InlineData("field-assigned-in-readonly-method", "MIST0251")]
    [InlineData("field-assigned-in-readonly-type", "MIST0251")]
    [InlineData("field-assigned-on-readonly-parameter", "MIST0252")]
    [InlineData("field-write-while-frozen", "MIST0229")]
    [InlineData("frozen-to-borrowed-parameter", "MIST0230")]
    [InlineData("handed-in-a-loop-then-returned-owned", "MIST0263")]
    [InlineData("handed-on-one-branch-then-returned-owned", "MIST0263")]
    [InlineData("handed-owned-field-then-field-returned-owned", "MIST0263")]
    [InlineData("handed-owned-field-then-holder-returned-owned", "MIST0263")]
    [InlineData("handed-owned-parameter-then-moved-into-an-owned-binding", "MIST0264")]
    [InlineData("handed-then-consumed-as-an-owned-receiver", "MIST0264")]
    [InlineData("handed-then-moved-into-an-owned-binding", "MIST0264")]
    [InlineData("handed-then-moved-into-an-owned-field", "MIST0264")]
    [InlineData("handed-then-moved-into-an-owned-parameter", "MIST0264")]
    [InlineData("handed-then-mutated-then-returned-owned", "MIST0263")]
    [InlineData("handed-then-result-aliased-then-returned-owned", "MIST0263")]
    [InlineData("handed-through-a-wrapper-then-returned-owned", "MIST0263")]
    [InlineData("handed-to-a-constructor-then-returned-owned", "MIST0263")]
    [InlineData("handed-to-a-function-value-then-returned-owned", "MIST0263")]
    [InlineData("handed-to-a-local-function-then-returned-owned", "MIST0263")]
    [InlineData("handed-to-a-static-method-then-returned-owned", "MIST0263")]
    [InlineData("handed-to-an-instance-method-then-returned-owned", "MIST0263")]
    [InlineData("handed-whole-then-owned-field-returned-owned", "MIST0263")]
    [InlineData("lambda-captures-borrowed-parameter", "MIST0269")]
    [InlineData("lambda-captures-frozen-parameter", "MIST0269")]
    [InlineData("lambda-captures-owned-binding", "MIST0269")]
    [InlineData("lambda-captures-readonly-ref-parameter", "MIST0269")]
    [InlineData("lambda-captures-ref-struct", "MIST0269")]
    [InlineData("linear-dropped-twice", "MIST0227")]
    [InlineData("linear-field-in-affine-container", "MIST0240")]
    [InlineData("linear-field-in-unrestricted-container", "MIST0240")]
    [InlineData("linear-task-awaited-twice", "MIST0227")]
    [InlineData("linear-task-dropped-twice", "MIST0227")]
    [InlineData("linear-used-after-alias-move", "MIST0227")]
    [InlineData("linear-used-after-drop", "MIST0227")]
    [InlineData("local-function-captures-a-borrowed-binding", "MIST0269")]
    [InlineData("local-function-captures-an-owned-binding", "MIST0269")]
    [InlineData("move-while-frozen", "MIST0229")]
    [InlineData("move-while-readonly-ref-outstanding", "MIST0229")]
    [InlineData("moved-inside-loop", "MIST0228")]
    [InlineData("mutation-while-frozen", "MIST0229")]
    [InlineData("owned-abstract", "MIST0236")]
    [InlineData("owned-binding-of-linear-type", "MIST0238")]
    [InlineData("owned-field-borrowed-then-read", "MIST0229")]
    [InlineData("owned-field-frozen-then-whole-move", "MIST0234")]
    [InlineData("owned-field-frozen-then-write", "MIST0229")]
    [InlineData("owned-field-move-into-owned-parameter-then-read", "MIST0227")]
    [InlineData("owned-field-move-then-method-reading-it", "MIST0235")]
    [InlineData("owned-field-move-then-method-writing-it", "MIST0235")]
    [InlineData("owned-field-move-then-read", "MIST0227")]
    [InlineData("owned-field-move-then-whole-move", "MIST0234")]
    [InlineData("owned-field-move-then-whole-read", "MIST0234")]
    [InlineData("owned-field-moved-in-one-branch-then-read", "MIST0227")]
    [InlineData("owned-field-moved-inside-loop", "MIST0228")]
    [InlineData("owned-field-moved-while-holder-frozen", "MIST0229")]
    [InlineData("owned-field-read-into-unrestricted-then-read", "MIST0227")]
    [InlineData("owned-field-readonly-ref-then-move", "MIST0229")]
    [InlineData("owned-parameter-used-after-move", "MIST0227")]
    [InlineData("readonly-block-in-constructor-assigns-field", "MIST0255")]
    [InlineData("readonly-field-assigned-from-outside-the-type", "MIST0262")]
    [InlineData("readonly-field-assigned-outside-constructor", "MIST0262")]
    [InlineData("readonly-method-allocates-mutable", "MIST0260")]
    [InlineData("readonly-method-calls-mutable-method", "MIST0257")]
    [InlineData("readonly-method-calls-mutable-operator", "MIST0257")]
    [InlineData("readonly-method-coalesces-property", "MIST0259")]
    [InlineData("readonly-method-reads-mutable-indexer", "MIST0257")]
    [InlineData("readonly-method-sets-indexer-on-parameter", "MIST0259")]
    [InlineData("readonly-method-sets-property", "MIST0259")]
    [InlineData("readonly-method-writes-through-index", "MIST0256")]
    [InlineData("readonly-parameter-calls-mutable-method", "MIST0253")]
    [InlineData("readonly-parameter-sets-property", "MIST0254")]
    [InlineData("readonly-ref-to-frozen-parameter", "MIST0230")]
    [InlineData("ref-on-class", "MIST0242")]
    [InlineData("ref-struct-as-type-argument", "MIST0244")]
    [InlineData("ref-struct-field-in-class", "MIST0243")]
    [InlineData("relevant-declared-owned", "MIST0239")]
    [InlineData("shared-extract", "MIST0233")]
    [InlineData("shared-extract-by-assignment", "MIST0233")]
    [InlineData("shared-extract-frozen", "MIST0233")]
    [InlineData("shared-extract-into-owned-parameter", "MIST0233")]
    [InlineData("shared-extract-unrestricted-nonclonable", "MIST0233")]
    [InlineData("stack-span-returned-through-alias", "MIST0265")]
    [InlineData("stack-span-returned", "MIST0265")]
    [InlineData("task-field-in-unrestricted-container", "MIST0240")]
    [InlineData("this-tier-insufficient-frozen", "MIST0232")]
    [InlineData("this-tier-insufficient-readonly-ref", "MIST0232")]
    [InlineData("this-tier-mismatch-bare-field", "MIST0231")]
    [InlineData("this-tier-mismatch-frozen-receiver", "MIST0231")]
    [InlineData("this-tier-mismatch-owned-required", "MIST0231")]
    [InlineData("this-tier-mismatch-readonly-ref-parameter", "MIST0231")]
    [InlineData("this-tier-mismatch-through-explicit-this-call", "MIST0231")]
    [InlineData("this-tier-mismatch-unrestricted-parameter", "MIST0231")]
    [InlineData("unconstrained-type-parameter-field-in-linear-container", "MIST0241")]
    [InlineData("linear-argument-closing-a-generic-alias", "MIST0240")]
    [InlineData("linear-argument-closing-an-unconstrained-field", "MIST0240")]
    [InlineData("linear-argument-reaching-an-unconstrained-field-through-a-wrapper", "MIST0240")]
    [InlineData("unrestricted-to-borrowed-parameter", "MIST0230")]
    [InlineData("unrestricted-to-frozen-parameter", "MIST0230")]
    [InlineData("use-after-consuming-call", "MIST0227")]
    [InlineData("use-after-move-by-assignment", "MIST0227")]
    [InlineData("use-after-move-in-else-branch", "MIST0227")]
    [InlineData("use-after-move-in-one-branch", "MIST0227")]
    [InlineData("use-after-move-into-array-literal", "MIST0227")]
    [InlineData("use-after-move-into-owned-binding", "MIST0227")]
    [InlineData("use-after-move-into-owned-constructor-parameter", "MIST0227")]
    [InlineData("use-after-move-into-owned-parameter", "MIST0227")]
    [InlineData("use-after-move-into-unrestricted-binding", "MIST0227")]
    [InlineData("use-after-move-through-interpolation", "MIST0227")]
    public void RefusedFragmentKeepsItsDiagnostic(string caseName, string expected)
    {
        var diagnostic = Assert.Single(new OwnershipFragments().Analyze(caseName));

        Assert.True(diagnostic.Number.Value == expected, $"{caseName}: expected {expected}, got {diagnostic.Number.Value}");
    }

    [Theory]
    [InlineData("use-after-move-into-owned-binding")]
    [InlineData("access-while-borrowed")]
    [InlineData("owned-field-move-then-read")]
    [InlineData("owned-field-frozen-then-write")]
    [InlineData("owned-field-move-then-whole-move")]
    [InlineData("shared-extract-frozen")]
    [InlineData("linear-argument-closing-an-unconstrained-field")]
    [InlineData("this-tier-mismatch-unrestricted-parameter")]
    [InlineData("lambda-captures-owned-binding")]
    [InlineData("stack-span-returned")]
    [InlineData("field-assigned-in-readonly-method")]
    [InlineData("ref-struct-field-in-class")]
    [InlineData("handed-to-a-static-method-then-returned-owned")]
    [InlineData("handed-then-moved-into-an-owned-binding")]
    [InlineData("handed-owned-field-then-field-returned-owned")]
    public void RefusalPointsAtTheUseCaptureReturnWriteOrDeclaration(string site)
    {
        var diagnostic = Assert.Single(new OwnershipFragments().Analyze(site));

        Assert.Equal("hand-built.mist", diagnostic.Location.Path);
        Assert.Equal(38, diagnostic.Position().Start.Column);
    }

    [Theory]
    [InlineData("handed-to-a-static-method-then-returned-owned", "'c' was handed to parameter 'c' of 'Keep' without a move")]
    [InlineData("handed-to-a-constructor-then-returned-owned", "'c' was handed to parameter 'c' of 'Sink' without a move")]
    [InlineData("handed-to-a-function-value-then-returned-owned", "'c' was handed to a function value without a move")]
    [InlineData("handed-owned-field-then-holder-returned-owned", "its owned field 'Left' was handed to parameter 'c' of 'Keep' without a move")]
    [InlineData("handed-then-moved-into-an-owned-parameter", "Cannot move this value into owned parameter 'c': 'c' was handed to parameter 'c' of 'Keep'")]
    public void AHandoffRefusalNamesTheRecipient(string caseName, string named)
    {
        Assert.Contains(named, Assert.Single(new OwnershipFragments().Analyze(caseName)).Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Owned-fields Requirement 18.9 bites at the owned claim, not at the handoff: a frozen, borrowed or
    /// readonly ref parameter gives the value back, a fresh value replaces what was handed away on
    /// whichever path handed it, a value that cannot change or a copied field leaves no second holder to
    /// speak of, and a plain use after the handoff claims nothing.
    /// </summary>
    [Theory]
    [InlineData("handed-to-a-frozen-parameter-then-returned-owned")]
    [InlineData("handed-to-a-borrowed-parameter-then-returned-owned")]
    [InlineData("handed-to-a-readonly-ref-parameter-then-returned-owned")]
    [InlineData("handed-then-replaced-then-returned-owned")]
    [InlineData("handed-on-one-branch-and-replaced-there-then-returned-owned")]
    [InlineData("handed-in-a-loop-and-replaced-then-returned-owned")]
    [InlineData("handed-then-used-plainly")]
    [InlineData("handed-value-field-then-returned-owned")]
    [InlineData("handed-string-then-moved-into-an-owned-binding")]
    [InlineData("handed-owned-field-then-sibling-returned-owned")]
    [InlineData("handed-owned-field-then-restored-then-holder-returned-owned")]
    public void AHandoffTheContractAdmits_IsAdmitted(string caseName)
    {
        Assert.Empty(new OwnershipFragments().Analyze(caseName));
    }

    [Theory]
    [InlineData("readonly-block-calls-mutable-method", "readonly block in method 'Tick'")]
    [InlineData("readonly-method-calls-mutable-method", "readonly method 'Peek'")]
    public void ARefusalNamesTheReadonlyScopeItWasMadeIn(string caseName, string scope)
    {
        var diagnostic = Assert.Single(new OwnershipFragments().Analyze(caseName));

        Assert.Equal("MIST0257", diagnostic.Number.Value);
        Assert.Contains($"from {scope}:", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AReadonlyMethodMaySetALocalIndexerOnWhatItOwns()
    {
        Assert.Empty(new OwnershipFragments().Analyze("readonly-method-sets-indexer-on-fresh-local"));
    }

    [Theory]
    [InlineData("owned-field-move-then-sibling-read")]
    [InlineData("owned-field-move-then-restore-then-whole-move")]
    [InlineData("owned-field-move-then-method-reading-sibling")]
    [InlineData("owned-field-frozen-then-sibling-write")]
    [InlineData("owned-field-frozen-then-whole-freeze")]
    [InlineData("owned-field-frozen-lease-ends-then-write")]
    [InlineData("owned-field-borrowed-then-sibling-read")]
    [InlineData("owned-field-moved-in-loop-and-restored")]
    [InlineData("owned-field-cloned-into-owned-then-read")]
    [InlineData("owned-string-field-read-then-read")]
    [InlineData("this-lend-in-constructor")]
    [InlineData("this-lend-with-borrowed")]
    [InlineData("this-lend-struct")]
    [InlineData("this-lend-undeclared")]
    [InlineData("shared-extract-unrestricted-string")]
    [InlineData("shared-extract-cloned")]
    [InlineData("plain-argument-closing-an-unconstrained-field")]
    [InlineData("linear-argument-closing-an-affine-constrained-field")]
    public void AnOwnedFieldShapeTheContractAdmits_IsAdmitted(string caseName)
    {
        Assert.Empty(new OwnershipFragments().Analyze(caseName));
    }

    /// <summary>
    /// A `this` lent past the tier its method declares is refused where it is lent and again where the
    /// tier is declared, as a call through `this` already is: the site names the parameter, the
    /// declaration names what the body needs.
    /// </summary>
    [Theory]
    [InlineData("this-lend-frozen-to-borrowed")]
    [InlineData("this-lend-readonly-ref-to-frozen")]
    public void ThisLentPastItsDeclaredTier_IsRefusedAtTheLendAndTheDeclaration(string caseName)
    {
        var ids = new OwnershipFragments().Analyze(caseName).Select(diagnostic => diagnostic.Number.Value).Order().ToList();

        Assert.Equal(new[] { "MIST0230", "MIST0232" }, ids);
    }

    /// <summary>
    /// The shapes whose release now follows the executed path stay admitted: a linear local dropped on
    /// one branch, dropped and given a fresh value -- in a loop too -- or assigned a value after its own
    /// was consumed. The lowering is only right if the rule lets these through and refuses a use after
    /// the move, which the rows above hold.
    /// </summary>
    [Fact]
    public void ALinearLocalConsumedAndReintroducedIsAdmitted()
    {
        var f = new LifetimeFragments();
        var t = f.Local("t");
        var a = f.Local("a");
        var bodies = new[]
        {
            HandBuiltModel.Block(LifetimeFragments.Declare(t, f.New(1)), f.If(HandBuiltModel.Block(LifetimeFragments.Run(LifetimeFragments.Drop(t))))),
            HandBuiltModel.Block(
                LifetimeFragments.Declare(t, f.New(1)),
                LifetimeFragments.Run(LifetimeFragments.Drop(t)),
                LifetimeFragments.Run(LifetimeFragments.Assign(t, f.New(2))),
                LifetimeFragments.Run(LifetimeFragments.Drop(t))),
            HandBuiltModel.Block(
                LifetimeFragments.Declare(t, f.New(0)),
                f.While(HandBuiltModel.Block(LifetimeFragments.Run(LifetimeFragments.Drop(t)), LifetimeFragments.Run(LifetimeFragments.Assign(t, f.New(1)))))),
            HandBuiltModel.Block(
                LifetimeFragments.Declare(a, f.New(1)),
                LifetimeFragments.Declare(t, f.New(2)),
                LifetimeFragments.Run(LifetimeFragments.Drop(t)),
                LifetimeFragments.Run(LifetimeFragments.Assign(t, LifetimeFragments.Ref(a))),
                LifetimeFragments.Run(LifetimeFragments.Drop(t))),
        };

        Assert.All(bodies, body => Assert.Empty(f.Model.Analyze(new OwnershipAnalyzer(), body)));
    }

    /// <summary>
    /// A with releases the linear or affine local it was given as it ends, so a use after it is a use of
    /// a dropped value; inside the block the local is still there to use or hand on.
    /// </summary>
    [Fact]
    public void ALocalGivenToAWithIsDroppedWhenTheBlockEnds()
    {
        var f = new LifetimeFragments();
        var t = f.Local("t");
        var once = f.Local("once", f.Once);
        var linear = HandBuiltModel.Block(
            LifetimeFragments.Declare(t, f.New(1)),
            LifetimeFragments.With(LifetimeFragments.Ref(t), HandBuiltModel.Block()),
            LifetimeFragments.Run(f.Call(f.Take, LifetimeFragments.Ref(t))));
        var affine = HandBuiltModel.Block(
            LifetimeFragments.Declare(once, f.New(2, f.Once)),
            LifetimeFragments.With(LifetimeFragments.Ref(once), HandBuiltModel.Block()),
            LifetimeFragments.Run(LifetimeFragments.Drop(once)));

        Assert.All(
            [linear, affine],
            body => Assert.Equal("MIST0227", Assert.Single(f.Model.Analyze(new OwnershipAnalyzer(), body)).Number.Value));
    }

    [Fact]
    public void ALocalGivenToAWithOrLentToItIsAdmittedWhereItIsStillHeld()
    {
        var f = new LifetimeFragments();
        var t = f.Local("t");
        var u = f.Local("u");
        var v = f.Local("v", ownership: ProgramModel.Model.Enums.OwnershipKind.Frozen);
        var bodies = new[]
        {
            HandBuiltModel.Block(
                LifetimeFragments.Declare(t, f.New(1)),
                LifetimeFragments.With(LifetimeFragments.Ref(t), HandBuiltModel.Block(LifetimeFragments.Run(f.Call(f.Take, LifetimeFragments.Ref(t)))))),
            HandBuiltModel.Block(
                LifetimeFragments.Declare(u, f.New(2)),
                LifetimeFragments.Declare(v, LifetimeFragments.Ref(u)),
                LifetimeFragments.With(LifetimeFragments.Ref(v), HandBuiltModel.Block()),
                LifetimeFragments.Run(LifetimeFragments.Drop(u))),
        };

        Assert.All(bodies, body => Assert.Empty(f.Model.Analyze(new OwnershipAnalyzer(), body)));
    }
}