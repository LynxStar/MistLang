// Tier 2 (Functional): ownership, lease, escape, discipline, tier and readonly rules judge hand-built fragments.

using Mist.ProgramAnalysis.Rules;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class OwnershipRefusalTests
{
    [Theory]
    [InlineData("access-while-borrowed", "MIST_PA_LEASE_VIOLATION")]
    [InlineData("affine-declared-owned", "MIST_PA_CONFLICTING_DISCIPLINE")]
    [InlineData("affine-field-in-relevant-container", "MIST_PA_FIELD_DISCIPLINE_CONTAINMENT")]
    [InlineData("affine-used-after-drop", "MIST_PA_USE_AFTER_MOVE")]
    [InlineData("borrow-while-frozen", "MIST_PA_LEASE_VIOLATION")]
    [InlineData("borrower-mutation-while-frozen", "MIST_PA_LEASE_VIOLATION")]
    [InlineData("field-assigned-in-readonly-method", "MIST_PA_FIELD_ASSIGNED_IN_READONLY_MEMBER")]
    [InlineData("field-assigned-in-readonly-type", "MIST_PA_FIELD_ASSIGNED_IN_READONLY_MEMBER")]
    [InlineData("field-assigned-on-readonly-parameter", "MIST_PA_FIELD_ASSIGNED_ON_READONLY_PARAMETER")]
    [InlineData("field-write-while-frozen", "MIST_PA_LEASE_VIOLATION")]
    [InlineData("frozen-to-borrowed-parameter", "MIST_PA_INCOMPATIBLE_LEND")]
    [InlineData("lambda-captures-borrowed-parameter", "MIST_PA_CAPTURED_BINDING")]
    [InlineData("lambda-captures-frozen-parameter", "MIST_PA_CAPTURED_BINDING")]
    [InlineData("lambda-captures-owned-binding", "MIST_PA_CAPTURED_BINDING")]
    [InlineData("lambda-captures-readonly-ref-parameter", "MIST_PA_CAPTURED_BINDING")]
    [InlineData("lambda-captures-ref-struct", "MIST_PA_CAPTURED_BINDING")]
    [InlineData("linear-dropped-twice", "MIST_PA_USE_AFTER_MOVE")]
    [InlineData("linear-field-in-affine-container", "MIST_PA_FIELD_DISCIPLINE_CONTAINMENT")]
    [InlineData("linear-field-in-unrestricted-container", "MIST_PA_FIELD_DISCIPLINE_CONTAINMENT")]
    [InlineData("linear-task-awaited-twice", "MIST_PA_USE_AFTER_MOVE")]
    [InlineData("linear-task-dropped-twice", "MIST_PA_USE_AFTER_MOVE")]
    [InlineData("linear-used-after-alias-move", "MIST_PA_USE_AFTER_MOVE")]
    [InlineData("linear-used-after-drop", "MIST_PA_USE_AFTER_MOVE")]
    [InlineData("local-function-captures-a-borrowed-binding", "MIST_PA_CAPTURED_BINDING")]
    [InlineData("local-function-captures-an-owned-binding", "MIST_PA_CAPTURED_BINDING")]
    [InlineData("move-while-frozen", "MIST_PA_LEASE_VIOLATION")]
    [InlineData("move-while-readonly-ref-outstanding", "MIST_PA_LEASE_VIOLATION")]
    [InlineData("moved-inside-loop", "MIST_PA_LOOP_MOVE")]
    [InlineData("mutation-while-frozen", "MIST_PA_LEASE_VIOLATION")]
    [InlineData("owned-abstract", "MIST_PA_OWNED_ABSTRACT")]
    [InlineData("owned-binding-of-linear-type", "MIST_PA_OWNED_SUBSTRUCTURAL_BINDING")]
    [InlineData("owned-field-borrowed-then-read", "MIST_PA_LEASE_VIOLATION")]
    [InlineData("owned-field-frozen-then-whole-move", "MIST_PA_COMPOSITE_STATE")]
    [InlineData("owned-field-frozen-then-write", "MIST_PA_LEASE_VIOLATION")]
    [InlineData("owned-field-move-into-owned-parameter-then-read", "MIST_PA_USE_AFTER_MOVE")]
    [InlineData("owned-field-move-then-method-reading-it", "MIST_PA_FIELD_STATE_CALL")]
    [InlineData("owned-field-move-then-method-writing-it", "MIST_PA_FIELD_STATE_CALL")]
    [InlineData("owned-field-move-then-read", "MIST_PA_USE_AFTER_MOVE")]
    [InlineData("owned-field-move-then-whole-move", "MIST_PA_COMPOSITE_STATE")]
    [InlineData("owned-field-move-then-whole-read", "MIST_PA_COMPOSITE_STATE")]
    [InlineData("owned-field-moved-in-one-branch-then-read", "MIST_PA_USE_AFTER_MOVE")]
    [InlineData("owned-field-moved-inside-loop", "MIST_PA_LOOP_MOVE")]
    [InlineData("owned-field-moved-while-holder-frozen", "MIST_PA_LEASE_VIOLATION")]
    [InlineData("owned-field-read-into-unrestricted-then-read", "MIST_PA_USE_AFTER_MOVE")]
    [InlineData("owned-field-readonly-ref-then-move", "MIST_PA_LEASE_VIOLATION")]
    [InlineData("owned-parameter-used-after-move", "MIST_PA_USE_AFTER_MOVE")]
    [InlineData("readonly-block-in-constructor-assigns-field", "MIST_PA_FIELD_ASSIGNED_IN_READONLY_BLOCK")]
    [InlineData("readonly-field-assigned-from-outside-the-type", "MIST_PA_READONLY_FIELD_ASSIGNED")]
    [InlineData("readonly-field-assigned-outside-constructor", "MIST_PA_READONLY_FIELD_ASSIGNED")]
    [InlineData("readonly-method-allocates-mutable", "MIST_PA_READONLY_CONTEXT_ALLOCATES_MUTABLE")]
    [InlineData("readonly-method-calls-mutable-method", "MIST_PA_READONLY_CONTEXT_CALLS_MUTABLE")]
    [InlineData("readonly-method-calls-mutable-operator", "MIST_PA_READONLY_CONTEXT_CALLS_MUTABLE")]
    [InlineData("readonly-method-coalesces-property", "MIST_PA_READONLY_CONTEXT_SETS_PROPERTY")]
    [InlineData("readonly-method-reads-mutable-indexer", "MIST_PA_READONLY_CONTEXT_CALLS_MUTABLE")]
    [InlineData("readonly-method-sets-indexer-on-parameter", "MIST_PA_READONLY_CONTEXT_SETS_PROPERTY")]
    [InlineData("readonly-method-sets-property", "MIST_PA_READONLY_CONTEXT_SETS_PROPERTY")]
    [InlineData("readonly-method-writes-through-index", "MIST_PA_READONLY_CONTEXT_WRITES_THROUGH_INDEX")]
    [InlineData("readonly-parameter-calls-mutable-method", "MIST_PA_READONLY_PARAMETER_CALLS_MUTABLE")]
    [InlineData("readonly-parameter-sets-property", "MIST_PA_READONLY_PARAMETER_SETS_PROPERTY")]
    [InlineData("readonly-ref-to-frozen-parameter", "MIST_PA_INCOMPATIBLE_LEND")]
    [InlineData("ref-on-class", "MIST_PA_REF_ON_CLASS")]
    [InlineData("ref-struct-as-type-argument", "MIST_PA_REF_STRUCT_TYPE_ARGUMENT")]
    [InlineData("ref-struct-field-in-class", "MIST_PA_REF_STRUCT_FIELD")]
    [InlineData("relevant-declared-owned", "MIST_PA_CONFLICTING_DISCIPLINE")]
    [InlineData("shared-extract", "MIST_PA_SHARED_EXTRACT")]
    [InlineData("shared-extract-by-assignment", "MIST_PA_SHARED_EXTRACT")]
    [InlineData("shared-extract-frozen", "MIST_PA_SHARED_EXTRACT")]
    [InlineData("shared-extract-into-owned-parameter", "MIST_PA_SHARED_EXTRACT")]
    [InlineData("shared-extract-unrestricted-nonclonable", "MIST_PA_SHARED_EXTRACT")]
    [InlineData("stack-span-returned-through-alias", "MIST_PA_STACK_SPAN_RETURNED")]
    [InlineData("stack-span-returned", "MIST_PA_STACK_SPAN_RETURNED")]
    [InlineData("task-field-in-unrestricted-container", "MIST_PA_FIELD_DISCIPLINE_CONTAINMENT")]
    [InlineData("this-tier-insufficient-frozen", "MIST_PA_THIS_TIER_INSUFFICIENT")]
    [InlineData("this-tier-insufficient-readonly-ref", "MIST_PA_THIS_TIER_INSUFFICIENT")]
    [InlineData("this-tier-mismatch-bare-field", "MIST_PA_THIS_TIER_MISMATCH")]
    [InlineData("this-tier-mismatch-frozen-receiver", "MIST_PA_THIS_TIER_MISMATCH")]
    [InlineData("this-tier-mismatch-owned-required", "MIST_PA_THIS_TIER_MISMATCH")]
    [InlineData("this-tier-mismatch-readonly-ref-parameter", "MIST_PA_THIS_TIER_MISMATCH")]
    [InlineData("this-tier-mismatch-through-explicit-this-call", "MIST_PA_THIS_TIER_MISMATCH")]
    [InlineData("this-tier-mismatch-unrestricted-parameter", "MIST_PA_THIS_TIER_MISMATCH")]
    [InlineData("unconstrained-type-parameter-field-in-linear-container", "MIST_PA_FIELD_TYPE_PARAMETER_UNCONSTRAINED")]
    [InlineData("linear-argument-closing-a-generic-alias", "MIST_PA_FIELD_DISCIPLINE_CONTAINMENT")]
    [InlineData("linear-argument-closing-an-unconstrained-field", "MIST_PA_FIELD_DISCIPLINE_CONTAINMENT")]
    [InlineData("linear-argument-reaching-an-unconstrained-field-through-a-wrapper", "MIST_PA_FIELD_DISCIPLINE_CONTAINMENT")]
    [InlineData("unrestricted-to-borrowed-parameter", "MIST_PA_INCOMPATIBLE_LEND")]
    [InlineData("unrestricted-to-frozen-parameter", "MIST_PA_INCOMPATIBLE_LEND")]
    [InlineData("use-after-consuming-call", "MIST_PA_USE_AFTER_MOVE")]
    [InlineData("use-after-move-by-assignment", "MIST_PA_USE_AFTER_MOVE")]
    [InlineData("use-after-move-in-else-branch", "MIST_PA_USE_AFTER_MOVE")]
    [InlineData("use-after-move-in-one-branch", "MIST_PA_USE_AFTER_MOVE")]
    [InlineData("use-after-move-into-array-literal", "MIST_PA_USE_AFTER_MOVE")]
    [InlineData("use-after-move-into-owned-binding", "MIST_PA_USE_AFTER_MOVE")]
    [InlineData("use-after-move-into-owned-constructor-parameter", "MIST_PA_USE_AFTER_MOVE")]
    [InlineData("use-after-move-into-owned-parameter", "MIST_PA_USE_AFTER_MOVE")]
    [InlineData("use-after-move-into-unrestricted-binding", "MIST_PA_USE_AFTER_MOVE")]
    [InlineData("use-after-move-through-interpolation", "MIST_PA_USE_AFTER_MOVE")]
    public void RefusedFragmentKeepsItsDiagnostic(string caseName, string expected)
    {
        var diagnostic = Assert.Single(new OwnershipFragments().Analyze(caseName));

        Assert.True(diagnostic.Id.Value == expected, $"{caseName}: expected {expected}, got {diagnostic.Id.Value}");
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
    public void RefusalPointsAtTheUseCaptureReturnWriteOrDeclaration(string site)
    {
        var diagnostic = Assert.Single(new OwnershipFragments().Analyze(site));

        Assert.Equal("hand-built.mist", diagnostic.CompilationUnitPath);
        Assert.Equal(38, diagnostic.Location.Start.Column);
    }

    [Theory]
    [InlineData("readonly-block-calls-mutable-method", "readonly block in method 'Tick'")]
    [InlineData("readonly-method-calls-mutable-method", "readonly method 'Peek'")]
    public void ARefusalNamesTheReadonlyScopeItWasMadeIn(string caseName, string scope)
    {
        var diagnostic = Assert.Single(new OwnershipFragments().Analyze(caseName));

        Assert.Equal("MIST_PA_READONLY_CONTEXT_CALLS_MUTABLE", diagnostic.Id.Value);
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
        var ids = new OwnershipFragments().Analyze(caseName).Select(diagnostic => diagnostic.Id.Value).Order().ToList();

        Assert.Equal(new[] { "MIST_PA_INCOMPATIBLE_LEND", "MIST_PA_THIS_TIER_INSUFFICIENT" }, ids);
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
            body => Assert.Equal("MIST_PA_USE_AFTER_MOVE", Assert.Single(f.Model.Analyze(new OwnershipAnalyzer(), body)).Id.Value));
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