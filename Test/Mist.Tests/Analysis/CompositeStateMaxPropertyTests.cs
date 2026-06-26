// Feature: owned-fields, Property 3: Composite state is max(child states) (Req 16 AC 9–14)
// **Validates: Requirements 16.9, 16.10, 16.11, 16.12, 16.13, 16.14**
//
// For all owned bindings with per-field state, CompositeState(p) == Max(State(f₁), ..., State(fₙ))
// using the design ordering: Live(0) < LentReadonly(1) < LentFrozen(2) < LentRef(3) < Moved(4).
//
// This test exercises MethodFlowState directly (no compilation needed).
// In-process only: 0 external process spawns (test-process-spawning policy).

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Text;
using Mist.Semantics;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based test: Composite state is max(child states).
///
/// Feature: owned-fields, Property 3
/// **Validates: Requirements 16.9, 16.10, 16.11, 16.12, 16.13, 16.14**
///
/// For all owned bindings with per-field state:
///   <c>CompositeState(p) == Max(State(f₁), State(f₂), ..., State(fₙ))</c>
///
/// The test generates random arrays of <see cref="VariableOwnershipState"/> values
/// (1..6 fields), directly sets them on a <see cref="MethodFlowState"/> instance,
/// and verifies that <see cref="MethodFlowState.CompositeState"/> returns the
/// most restrictive state using the ordering:
///   Live &lt; LentReadonly &lt; LentFrozen &lt; LentRef &lt; Moved.
///
/// Pure in-memory — spawns 0 external processes.
/// </summary>
public sealed class CompositeStateMaxPropertyTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Restrictiveness ordering — matches MethodFlowState.FieldStateRestrictiveness
    // ═══════════════════════════════════════════════════════════════════════

    private static int Restrictiveness(VariableOwnershipState state)
    {
        return state switch
        {
            VariableOwnershipState.Live => 0,
            VariableOwnershipState.LentReadonly => 1,
            VariableOwnershipState.LentFrozen => 2,
            VariableOwnershipState.LentRef => 3,
            VariableOwnershipState.Moved => 4,
            _ => 0,
        };
    }

    /// <summary>
    /// Computes the expected composite state as the maximum over the restrictiveness ordering.
    /// </summary>
    private static VariableOwnershipState ExpectedComposite(IReadOnlyList<VariableOwnershipState> fieldStates)
    {
        var max = VariableOwnershipState.Live;
        foreach(var s in fieldStates)
        {
            if(Restrictiveness(s) > Restrictiveness(max))
            {
                max = s;
            }
        }

        return max;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test infrastructure — stub symbols
    // ═══════════════════════════════════════════════════════════════════════

    private static Symbol CreateParentSymbol()
    {
        var scope = new Scope(parent: null);
        return new Symbol(
            name: "parent",
            kind: SymbolKind.LocalVariable,
            origin: SymbolOrigin.UserCode,
            containingScope: scope,
            declaredType: null,
            declarationSpan: new SourceSpan(0, 6));
    }

    private static Symbol[] CreateFieldSymbols(int count)
    {
        var scope = new Scope(parent: null);
        var fields = new Symbol[count];
        for(var i = 0; i < count; i++)
        {
            var field = new Symbol(
                name: $"field{i}",
                kind: SymbolKind.Field,
                origin: SymbolOrigin.UserCode,
                containingScope: scope,
                declaredType: null,
                declarationSpan: new SourceSpan(i * 10, 5));
            field.IsOwnedField = true;
            scope.AddMember(field);
            fields[i] = field;
        }

        return fields;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Scenario model for FsCheck
    // ═══════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034 // nested types are the FsCheck-friendly way to surface a scenario

    /// <summary>
    /// A test scenario: a list of per-field states to apply.
    /// </summary>
    public sealed record CompositeStateScenario(IReadOnlyList<VariableOwnershipState> FieldStates)
    {
        public override string ToString()
        {
            var states = string.Join(", ", FieldStates.Select(s => s.ToString()));
            return $"[{states}]";
        }
    }

#pragma warning restore CA1034

    // ═══════════════════════════════════════════════════════════════════════
    // FsCheck generators
    // ═══════════════════════════════════════════════════════════════════════

    private static Gen<VariableOwnershipState> GenFieldState()
    {
        return Gen.Elements(
            VariableOwnershipState.Live,
            VariableOwnershipState.LentReadonly,
            VariableOwnershipState.LentFrozen,
            VariableOwnershipState.LentRef,
            VariableOwnershipState.Moved);
    }

    private static Gen<CompositeStateScenario> GenScenario()
    {
        return Gen.Choose(1, 6).SelectMany(fieldCount =>
            Gen.ArrayOf(GenFieldState(), fieldCount)
                .Select(states => new CompositeStateScenario(states)));
    }

    public static Arbitrary<CompositeStateScenario> ArbitraryCompositeStateScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3: CompositeState(p) == Max(State(f₁), ..., State(fₙ))
    //
    // **Validates: Requirements 16.9, 16.10, 16.11, 16.12, 16.13, 16.14**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 200, Arbitrary = new[] { typeof(CompositeStateMaxPropertyTests) })]
    public Property CompositeState_EqualsMaxOfAllFieldStates(CompositeStateScenario scenario)
    {
        var parent = CreateParentSymbol();
        var fields = CreateFieldSymbols(scenario.FieldStates.Count);

        // Set up the flow state with the parent binding and per-field states
        var flowState = new MethodFlowState();
        flowState.VariableStates[parent] = VariableOwnershipState.Live;
        flowState.BindingKinds[parent] = BindingKind.Owned;
        flowState.InitializeFieldStates(parent, fields.AsEnumerable());

        // Apply the generated states to each field directly via SetFieldState
        for(var i = 0; i < scenario.FieldStates.Count; i++)
        {
            flowState.SetFieldState(parent, fields[i], scenario.FieldStates[i]);
        }

        // Verify the property
        var actual = flowState.CompositeState(parent);
        var expected = ExpectedComposite(scenario.FieldStates);

        return (actual == expected).ToProperty().Label(
            $"Expected CompositeState={expected} (max of {scenario}), but got {actual}");
    }
}