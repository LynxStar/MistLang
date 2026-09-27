// Tier 2 (Functional): ownership, lease, escape, discipline, tier and readonly rules judge hand-built fragments.

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
    [InlineData("owned-parameter-used-after-move", "MIST_PA_USE_AFTER_MOVE")]
    [InlineData("readonly-block-in-constructor-assigns-field", "MIST_PA_FIELD_ASSIGNED_IN_READONLY_BLOCK")]
    [InlineData("readonly-field-assigned-from-outside-the-type", "MIST_PA_READONLY_FIELD_ASSIGNED")]
    [InlineData("readonly-field-assigned-outside-constructor", "MIST_PA_READONLY_FIELD_ASSIGNED")]
    [InlineData("readonly-method-allocates-mutable", "MIST_PA_READONLY_CONTEXT_ALLOCATES_MUTABLE")]
    [InlineData("readonly-method-calls-mutable-method", "MIST_PA_READONLY_CONTEXT_CALLS_MUTABLE")]
    [InlineData("readonly-method-calls-mutable-operator", "MIST_PA_READONLY_CONTEXT_CALLS_MUTABLE")]
    [InlineData("readonly-method-coalesces-property", "MIST_PA_READONLY_CONTEXT_SETS_PROPERTY")]
    [InlineData("readonly-method-reads-mutable-indexer", "MIST_PA_READONLY_CONTEXT_CALLS_MUTABLE")]
    [InlineData("readonly-method-sets-indexer-on-local", "MIST_PA_READONLY_CONTEXT_SETS_PROPERTY")]
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
}