// Feature: owned-fields, Task 18.5 — Property 5: Siblings are independent (Req 16 AC 22)
//
// **Validates: Requirements 16.22**
//
// For all owned bindings with multiple owned fields where field A is in a non-Live state,
// operations on sibling field B are unaffected. Field B remains Live regardless of field A's
// state, and all operations (read, write, move, borrow, freeze) succeed on field B.
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
/// Property-based test: Siblings are independent (Property 5, Req 16 AC 22).
///
/// For all owned bindings with multiple owned fields where field A is in a non-Live state,
/// operations on sibling field B are unaffected. Specifically:
///   - Field B remains Live after field A transitions to any non-Live state
///   - All operations on field B (read, write/restore, move, borrow, freeze) succeed
///     independently of field A's state
///
/// The test generates combinations of:
///   - Field A's state: LentReadonly, LentFrozen, LentRef, Moved (all non-Live states)
///   - Operation on field B: Read, Write, Move, Borrow, Freeze
///
/// Pure in-memory — spawns 0 external processes.
///
/// <b>Validates: Requirements 16.22</b>
/// </summary>
public sealed class SiblingIndependencePropertyTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Operations that can be performed on a sibling field
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The operations that can be performed on field B while field A is in a non-Live state.
    /// </summary>
    public enum SiblingOperation
    {
        /// <summary>Read the field state (GetFieldState).</summary>
        Read,

        /// <summary>Write/restore a new value into the field (SetFieldState to Live).</summary>
        Write,

        /// <summary>Move the field out (MoveField).</summary>
        Move,

        /// <summary>Borrow the field (BorrowField → LentRef).</summary>
        Borrow,

        /// <summary>Freeze the field (FreezeField → LentFrozen).</summary>
        Freeze,
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Scenario model for FsCheck
    // ═══════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034 // nested types are the FsCheck-friendly way to surface a scenario

    /// <summary>
    /// A test scenario: field A's non-Live state + the operation to perform on sibling field B.
    /// </summary>
    public sealed record SiblingScenario(VariableOwnershipState FieldAState, SiblingOperation FieldBOperation)
    {
        public override string ToString()
        {
            return $"FieldA={FieldAState}, FieldB_Op={FieldBOperation}";
        }
    }

#pragma warning restore CA1034

    // ═══════════════════════════════════════════════════════════════════════
    // FsCheck generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates non-Live states for field A (the states that restrict access).
    /// </summary>
    private static Gen<VariableOwnershipState> GenNonLiveState()
    {
        return Gen.Elements(
            VariableOwnershipState.LentReadonly,
            VariableOwnershipState.LentFrozen,
            VariableOwnershipState.LentRef,
            VariableOwnershipState.Moved);
    }

    /// <summary>
    /// Generates the sibling operation to apply to field B.
    /// </summary>
    private static Gen<SiblingOperation> GenSiblingOperation()
    {
        return Gen.Elements(
            SiblingOperation.Read,
            SiblingOperation.Write,
            SiblingOperation.Move,
            SiblingOperation.Borrow,
            SiblingOperation.Freeze);
    }

    private static Gen<SiblingScenario> GenScenario()
    {
        return GenNonLiveState().SelectMany(fieldAState =>
            GenSiblingOperation().Select(op =>
                new SiblingScenario(fieldAState, op)));
    }

    public static Arbitrary<SiblingScenario> ArbitrarySiblingScenario()
    {
        return GenScenario().ToArbitrary();
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

    private static (ClassType Type, Symbol[] Fields) CreateTypeWithOwnedFields()
    {
        var scope = new Scope(parent: null);
        var fieldA = new Symbol(
            name: "fieldA",
            kind: SymbolKind.Field,
            origin: SymbolOrigin.UserCode,
            containingScope: scope,
            declaredType: null,
            declarationSpan: new SourceSpan(0, 6))
        {
            IsOwnedField = true
        };
        scope.AddMember(fieldA);

        var fieldB = new Symbol(
            name: "fieldB",
            kind: SymbolKind.Field,
            origin: SymbolOrigin.UserCode,
            containingScope: scope,
            declaredType: null,
            declarationSpan: new SourceSpan(10, 6))
        {
            IsOwnedField = true
        };
        scope.AddMember(fieldB);

        var classType = new ClassType("TestClass", scope, containingNamespace: "Test");
        return (classType, new[] { fieldA, fieldB });
    }

    private static Symbol CreateBorrowerSymbol(string name = "borrower")
    {
        var scope = new Scope(parent: null);
        return new Symbol(
            name: name,
            kind: SymbolKind.LocalVariable,
            origin: SymbolOrigin.UserCode,
            containingScope: scope,
            declaredType: null,
            declarationSpan: new SourceSpan(100, 8));
    }

    private static SourceSpan TestMoveLocation => new(50, 10);

    /// <summary>
    /// Puts field A into the specified non-Live state on the given flow state.
    /// </summary>
    private static void PutFieldAInState(
        MethodFlowState flowState, Symbol parent, Symbol fieldA, VariableOwnershipState targetState)
    {
        switch(targetState)
        {
            case VariableOwnershipState.LentReadonly:
                flowState.ReadonlyRefField(parent, fieldA);
                break;
            case VariableOwnershipState.LentFrozen:
                flowState.FreezeField(parent, fieldA);
                break;
            case VariableOwnershipState.LentRef:
                flowState.BorrowField(parent, fieldA);
                break;
            case VariableOwnershipState.Moved:
                flowState.MoveField(parent, fieldA, TestMoveLocation);
                break;
        }
    }

    /// <summary>
    /// Applies the specified operation to field B and returns the expected resulting state.
    /// </summary>
    private static VariableOwnershipState ApplyOperationAndGetExpectedState(
        MethodFlowState flowState, Symbol parent, Symbol fieldB, SiblingOperation operation)
    {
        switch(operation)
        {
            case SiblingOperation.Read:
                // Reading field B's state — just query, expected to be Live
                return flowState.GetFieldState(parent, fieldB) ?? VariableOwnershipState.Live;

            case SiblingOperation.Write:
                // Write/restore — set back to Live (simulating writing a new owned value)
                flowState.SetFieldState(parent, fieldB, VariableOwnershipState.Live);
                return flowState.GetFieldState(parent, fieldB) ?? VariableOwnershipState.Live;

            case SiblingOperation.Move:
                // Move field B out
                flowState.MoveField(parent, fieldB, TestMoveLocation);
                return flowState.GetFieldState(parent, fieldB) ?? VariableOwnershipState.Live;

            case SiblingOperation.Borrow:
                // Borrow field B
                var borrower = CreateBorrowerSymbol("fieldB_borrower");
                flowState.BorrowField(parent, fieldB, borrower);
                return flowState.GetFieldState(parent, fieldB) ?? VariableOwnershipState.Live;

            case SiblingOperation.Freeze:
                // Freeze field B
                var freezer = CreateBorrowerSymbol("fieldB_freezer");
                flowState.FreezeField(parent, fieldB, freezer);
                return flowState.GetFieldState(parent, fieldB) ?? VariableOwnershipState.Live;

            default:
                return VariableOwnershipState.Live;
        }
    }

    /// <summary>
    /// Returns the expected state of field B after an operation is applied,
    /// assuming the operation is not blocked by field A's state (sibling independence).
    /// </summary>
    private static VariableOwnershipState ExpectedFieldBStateAfterOperation(SiblingOperation operation)
    {
        return operation switch
        {
            SiblingOperation.Read => VariableOwnershipState.Live,
            SiblingOperation.Write => VariableOwnershipState.Live,
            SiblingOperation.Move => VariableOwnershipState.Moved,
            SiblingOperation.Borrow => VariableOwnershipState.LentRef,
            SiblingOperation.Freeze => VariableOwnershipState.LentFrozen,
            _ => VariableOwnershipState.Live,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5: Siblings are independent (Req 16 AC 22)
    //
    // For all owned bindings with multiple owned fields where field A is in
    // a non-Live state, operations on sibling field B are unaffected.
    //
    // **Validates: Requirements 16.22**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 200, Arbitrary = new[] { typeof(SiblingIndependencePropertyTests) })]
    public Property SiblingFieldB_Unaffected_ByFieldA_NonLiveState(SiblingScenario scenario)
    {
        // Set up flow state with parent binding and two owned fields
        var parent = CreateParentSymbol();
        var (type, fields) = CreateTypeWithOwnedFields();
        var fieldA = fields[0];
        var fieldB = fields[1];

        var flowState = new MethodFlowState();
        flowState.VariableStates[parent] = VariableOwnershipState.Live;
        flowState.BindingKinds[parent] = BindingKind.Owned;
        flowState.InitializeFieldStates(parent, type);

        // Put field A in the specified non-Live state
        PutFieldAInState(flowState, parent, fieldA, scenario.FieldAState);

        // Verify field A is in the expected state
        var actualFieldAState = flowState.GetFieldState(parent, fieldA);
        if(actualFieldAState != scenario.FieldAState)
        {
            return false.ToProperty().Label(
                $"Setup failed: fieldA expected {scenario.FieldAState} but got {actualFieldAState}");
        }

        // Verify field B is still Live before we operate on it
        var fieldBBeforeOp = flowState.GetFieldState(parent, fieldB);
        if(fieldBBeforeOp != VariableOwnershipState.Live)
        {
            return false.ToProperty().Label(
                $"Setup failed: fieldB expected Live before operation but got {fieldBBeforeOp}");
        }

        // Apply the operation on field B
        var actualFieldBState = ApplyOperationAndGetExpectedState(flowState, parent, fieldB, scenario.FieldBOperation);
        var expectedFieldBState = ExpectedFieldBStateAfterOperation(scenario.FieldBOperation);

        // The operation on field B should produce the expected outcome
        // independent of field A's state
        var operationSucceeded = actualFieldBState == expectedFieldBState;

        // Additionally verify field A's state was NOT affected by the operation on field B
        var fieldAAfterOp = flowState.GetFieldState(parent, fieldA);
        var fieldAUnchanged = fieldAAfterOp == scenario.FieldAState;

        return (operationSucceeded && fieldAUnchanged).ToProperty().Label(
            $"FieldA={scenario.FieldAState}, Op={scenario.FieldBOperation}: " +
            $"FieldB expected {expectedFieldBState} got {actualFieldBState}, " +
            $"FieldA expected {scenario.FieldAState} got {fieldAAfterOp}");
    }
}