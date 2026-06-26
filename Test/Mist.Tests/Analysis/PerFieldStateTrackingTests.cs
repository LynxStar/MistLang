// Task 12.4 — Unit tests for per-field state tracking in MethodFlowState.
//
// Tests the per-field state tracking machinery directly on the MethodFlowState class
// without compilation — uses stub Symbol/ClassType instances to exercise field state
// transitions, composite state computation, sibling independence, parent-to-child
// restriction logic, and fork/merge correctness.
//
// In-process only: 0 external process spawns (test-process-spawning policy).

using Mist.Analysis.Builtin;
using Mist.Common.Source;
using Mist.Semantics;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests for per-field state tracking in <see cref="MethodFlowState"/> (Task 12.4).
/// Validates field state transitions, composite state computation, sibling independence,
/// parent-to-child restrictions, field restoration, and fork/merge behavior.
///
/// <b>Validates: Requirements 16.1, 16.2, 16.3, 16.4, 16.5, 16.6, 16.9, 16.11,
/// 16.15, 16.16, 16.19, 16.20, 16.21, 16.22</b>
/// </summary>
public sealed class PerFieldStateTrackingTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Test infrastructure — stub symbols and types
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a ClassType with the given owned field symbols in its member scope.
    /// </summary>
    private static (ClassType Type, Symbol[] Fields) CreateTypeWithOwnedFields(params string[] fieldNames)
    {
        var scope = new Scope(parent: null);
        var fields = new Symbol[fieldNames.Length];

        for(var i = 0; i < fieldNames.Length; i++)
        {
            var field = new Symbol(
                name: fieldNames[i],
                kind: SymbolKind.Field,
                origin: SymbolOrigin.UserCode,
                containingScope: scope,
                declaredType: null,
                declarationSpan: new SourceSpan(i * 10, 5))
            {
                IsOwnedField = true
            };
            scope.AddMember(field);
            fields[i] = field;
        }

        var classType = new ClassType("TestClass", scope, containingNamespace: "Test");
        return (classType, fields);
    }

    /// <summary>
    /// Creates a simple parent binding symbol (representing an owned var).
    /// </summary>
    private static Symbol CreateParentSymbol(string name = "parent")
    {
        var scope = new Scope(parent: null);
        return new Symbol(
            name: name,
            kind: SymbolKind.LocalVariable,
            origin: SymbolOrigin.UserCode,
            containingScope: scope,
            declaredType: null,
            declarationSpan: new SourceSpan(0, 6));
    }

    /// <summary>
    /// Creates a borrower symbol (representing a variable that borrows a field).
    /// </summary>
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

    // ═══════════════════════════════════════════════════════════════════════
    // Move one field → field is Moved, sibling is Live, parent is actually Live
    // (Req 16 AC 2, 15, 22)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MoveField_FieldBecomesMoved_SiblingRemainsLive_ParentRemainsLive()
    {
        var (type, fields) = CreateTypeWithOwnedFields("fieldA", "fieldB");
        var parent = CreateParentSymbol();

        var state = new MethodFlowState();
        state.VariableStates[parent] = VariableOwnershipState.Live;
        state.BindingKinds[parent] = BindingKind.Owned;
        state.InitializeFieldStates(parent, type);

        // Move fieldA
        state.MoveField(parent, fields[0], TestMoveLocation);

        // fieldA is Moved
        Assert.Equal(VariableOwnershipState.Moved, state.GetFieldState(parent, fields[0]));

        // fieldB (sibling) is still Live
        Assert.Equal(VariableOwnershipState.Live, state.GetFieldState(parent, fields[1]));

        // Parent binding's own state remains Live (Req 16 AC 15)
        Assert.Equal(VariableOwnershipState.Live, state.VariableStates[parent]);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Composite state after one field moved → Moved (Req 16 AC 9–14)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CompositeState_OneFieldMoved_ReturnsMoved()
    {
        var (type, fields) = CreateTypeWithOwnedFields("fieldA", "fieldB");
        var parent = CreateParentSymbol();

        var state = new MethodFlowState();
        state.VariableStates[parent] = VariableOwnershipState.Live;
        state.BindingKinds[parent] = BindingKind.Owned;
        state.InitializeFieldStates(parent, type);

        state.MoveField(parent, fields[0], TestMoveLocation);

        Assert.Equal(VariableOwnershipState.Moved, state.CompositeState(parent));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Composite state after one field frozen → LentFrozen (Req 16 AC 11)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CompositeState_OneFieldFrozen_ReturnsLentFrozen()
    {
        var (type, fields) = CreateTypeWithOwnedFields("fieldA", "fieldB");
        var parent = CreateParentSymbol();
        var borrower = CreateBorrowerSymbol();

        var state = new MethodFlowState();
        state.VariableStates[parent] = VariableOwnershipState.Live;
        state.BindingKinds[parent] = BindingKind.Owned;
        state.InitializeFieldStates(parent, type);

        state.FreezeField(parent, fields[0], borrower);

        Assert.Equal(VariableOwnershipState.LentFrozen, state.CompositeState(parent));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Restore moved field → field returns to Live, composite returns to Live
    // (Req 16 AC 6, 16)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void RestoreField_MovedFieldReturnsToLive_CompositeReturnsToLive()
    {
        var (type, fields) = CreateTypeWithOwnedFields("fieldA", "fieldB");
        var parent = CreateParentSymbol();

        var state = new MethodFlowState();
        state.VariableStates[parent] = VariableOwnershipState.Live;
        state.BindingKinds[parent] = BindingKind.Owned;
        state.InitializeFieldStates(parent, type);

        // Move fieldA out
        state.MoveField(parent, fields[0], TestMoveLocation);
        Assert.Equal(VariableOwnershipState.Moved, state.GetFieldState(parent, fields[0]));
        Assert.Equal(VariableOwnershipState.Moved, state.CompositeState(parent));

        // Restore fieldA (write a new owned value into it)
        state.RestoreField(parent, fields[0]);

        // Field is Live again
        Assert.Equal(VariableOwnershipState.Live, state.GetFieldState(parent, fields[0]));

        // Composite returns to Live (all fields are Live)
        Assert.Equal(VariableOwnershipState.Live, state.CompositeState(parent));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Sibling independence: field A borrowed does NOT block ops on field B
    // (Req 16 AC 22)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SiblingIndependence_FieldABorrowed_FieldBStillLive()
    {
        var (type, fields) = CreateTypeWithOwnedFields("fieldA", "fieldB");
        var parent = CreateParentSymbol();
        var borrower = CreateBorrowerSymbol();

        var state = new MethodFlowState();
        state.VariableStates[parent] = VariableOwnershipState.Live;
        state.BindingKinds[parent] = BindingKind.Owned;
        state.InitializeFieldStates(parent, type);

        // Borrow fieldA
        state.BorrowField(parent, fields[0], borrower);

        // fieldA is LentRef (borrowed)
        Assert.Equal(VariableOwnershipState.LentRef, state.GetFieldState(parent, fields[0]));

        // fieldB is still Live — operations on it are independent of fieldA's state
        Assert.Equal(VariableOwnershipState.Live, state.GetFieldState(parent, fields[1]));
    }

    [Fact]
    public void SiblingIndependence_FieldABorrowed_FieldBCanBeMoved()
    {
        var (type, fields) = CreateTypeWithOwnedFields("fieldA", "fieldB");
        var parent = CreateParentSymbol();
        var borrower = CreateBorrowerSymbol();

        var state = new MethodFlowState();
        state.VariableStates[parent] = VariableOwnershipState.Live;
        state.BindingKinds[parent] = BindingKind.Owned;
        state.InitializeFieldStates(parent, type);

        // Borrow fieldA
        state.BorrowField(parent, fields[0], borrower);

        // Move fieldB — unaffected by fieldA's borrowed state
        state.MoveField(parent, fields[1], TestMoveLocation);

        Assert.Equal(VariableOwnershipState.Moved, state.GetFieldState(parent, fields[1]));
    }

    [Fact]
    public void SiblingIndependence_FieldABorrowed_FieldBCanBeFrozen()
    {
        var (type, fields) = CreateTypeWithOwnedFields("fieldA", "fieldB");
        var parent = CreateParentSymbol();
        var borrowerA = CreateBorrowerSymbol("borrowerA");
        var borrowerB = CreateBorrowerSymbol("borrowerB");

        var state = new MethodFlowState();
        state.VariableStates[parent] = VariableOwnershipState.Live;
        state.BindingKinds[parent] = BindingKind.Owned;
        state.InitializeFieldStates(parent, type);

        // Borrow fieldA
        state.BorrowField(parent, fields[0], borrowerA);

        // Freeze fieldB — sibling is independent
        state.FreezeField(parent, fields[1], borrowerB);

        Assert.Equal(VariableOwnershipState.LentFrozen, state.GetFieldState(parent, fields[1]));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Parent-to-child restriction: parent externally frozen → child write blocked
    // (Req 16 AC 19–21)
    //
    // NOTE: The parent-to-child restriction is enforced by the flow-sensitive
    // analysis at the call site (OwnershipAnalyzer checks parent's VariableState
    // before permitting field mutations). Here we verify the state infrastructure:
    // when the parent is externally frozen (VariableStates[parent] = LentFrozen),
    // the per-field state still allows queries but the calling code should check
    // the parent's state before permitting writes.
    //
    // We verify the MethodFlowState can track the scenario where the parent is
    // externally lent (frozen) and the child fields remain in their own states.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ParentToChildRestriction_ParentExternallyFrozen_ChildFieldStatesPreserved()
    {
        var (type, fields) = CreateTypeWithOwnedFields("fieldA", "fieldB");
        var parent = CreateParentSymbol();
        var externalBorrower = CreateBorrowerSymbol("externalBorrower");

        var state = new MethodFlowState();
        state.VariableStates[parent] = VariableOwnershipState.Live;
        state.BindingKinds[parent] = BindingKind.Owned;
        state.InitializeFieldStates(parent, type);

        // Parent is externally frozen (lent as frozen to something else)
        state.VariableStates[parent] = VariableOwnershipState.LentFrozen;
        state.ActiveBorrows.Add(new BorrowEntry(parent, externalBorrower, OwnershipTier.Frozen, state.ScopeDepth));

        // Child fields remain in their own tracked state (Live)
        Assert.Equal(VariableOwnershipState.Live, state.GetFieldState(parent, fields[0]));
        Assert.Equal(VariableOwnershipState.Live, state.GetFieldState(parent, fields[1]));

        // The parent's externally-imposed state (LentFrozen) is what blocks child writes.
        // The calling code must check VariableStates[parent] before allowing mutations.
        Assert.Equal(VariableOwnershipState.LentFrozen, state.VariableStates[parent]);
    }

    [Fact]
    public void ParentToChildRestriction_ParentExternallyBorrowed_ParentStateLentRef()
    {
        var (type, fields) = CreateTypeWithOwnedFields("fieldA", "fieldB");
        var parent = CreateParentSymbol();
        var externalBorrower = CreateBorrowerSymbol("externalBorrower");

        var state = new MethodFlowState();
        state.VariableStates[parent] = VariableOwnershipState.Live;
        state.BindingKinds[parent] = BindingKind.Owned;
        state.InitializeFieldStates(parent, type);

        // Parent is externally borrowed (exclusive mutable)
        state.VariableStates[parent] = VariableOwnershipState.LentRef;
        state.ActiveBorrows.Add(new BorrowEntry(parent, externalBorrower, OwnershipTier.Borrowed, state.ScopeDepth));

        // Parent state is LentRef → all child operations are blocked (no access at all)
        Assert.Equal(VariableOwnershipState.LentRef, state.VariableStates[parent]);

        // Per-field state infrastructure still tracks fields independently
        Assert.Equal(VariableOwnershipState.Live, state.GetFieldState(parent, fields[0]));
    }

    [Fact]
    public void ParentToChildRestriction_ParentExternallyReadonlyRef_ParentStateLentReadonly()
    {
        var (type, fields) = CreateTypeWithOwnedFields("fieldA");
        var parent = CreateParentSymbol();
        var externalBorrower = CreateBorrowerSymbol("externalBorrower");

        var state = new MethodFlowState();
        state.VariableStates[parent] = VariableOwnershipState.Live;
        state.BindingKinds[parent] = BindingKind.Owned;
        state.InitializeFieldStates(parent, type);

        // Parent is externally lent as readonly ref
        state.VariableStates[parent] = VariableOwnershipState.LentReadonly;
        state.ActiveBorrows.Add(new BorrowEntry(parent, externalBorrower, OwnershipTier.ReadonlyRef, state.ScopeDepth));

        // Parent state is LentReadonly → child writes/borrows/moves blocked but reads OK
        Assert.Equal(VariableOwnershipState.LentReadonly, state.VariableStates[parent]);
        Assert.Equal(VariableOwnershipState.Live, state.GetFieldState(parent, fields[0]));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Fork/merge preserves per-field state correctly (Req 16 AC 1)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Fork_CreatesIndependentCopy_FieldStateChangesDoNotAffectOriginal()
    {
        var (type, fields) = CreateTypeWithOwnedFields("fieldA", "fieldB");
        var parent = CreateParentSymbol();

        var state = new MethodFlowState();
        state.VariableStates[parent] = VariableOwnershipState.Live;
        state.BindingKinds[parent] = BindingKind.Owned;
        state.InitializeFieldStates(parent, type);

        // Fork
        var forked = state.Fork();

        // Move fieldA in the forked copy
        forked.MoveField(parent, fields[0], TestMoveLocation);

        // Original is unaffected
        Assert.Equal(VariableOwnershipState.Live, state.GetFieldState(parent, fields[0]));
        Assert.Equal(VariableOwnershipState.Live, state.CompositeState(parent));

        // Forked reflects the move
        Assert.Equal(VariableOwnershipState.Moved, forked.GetFieldState(parent, fields[0]));
        Assert.Equal(VariableOwnershipState.Moved, forked.CompositeState(parent));
    }

    [Fact]
    public void Merge_TakesMoreRestrictiveState_PerField()
    {
        var (type, fields) = CreateTypeWithOwnedFields("fieldA", "fieldB");
        var parent = CreateParentSymbol();

        var state = new MethodFlowState();
        state.VariableStates[parent] = VariableOwnershipState.Live;
        state.BindingKinds[parent] = BindingKind.Owned;
        state.InitializeFieldStates(parent, type);

        // Fork — one branch moves fieldA, the other leaves it Live
        var branch = state.Fork();
        branch.MoveField(parent, fields[0], TestMoveLocation);

        // In the main branch, fieldA is Live; in the other branch, fieldA is Moved.
        Assert.Equal(VariableOwnershipState.Live, state.GetFieldState(parent, fields[0]));
        Assert.Equal(VariableOwnershipState.Moved, branch.GetFieldState(parent, fields[0]));

        // Merge: takes the more restrictive state (Moved > Live)
        state.Merge(branch);

        Assert.Equal(VariableOwnershipState.Moved, state.GetFieldState(parent, fields[0]));
        Assert.Equal(VariableOwnershipState.Live, state.GetFieldState(parent, fields[1]));
    }

    [Fact]
    public void Merge_BothBranchesDifferentFieldStates_TakesMoreRestrictiveForEach()
    {
        var (type, fields) = CreateTypeWithOwnedFields("fieldA", "fieldB", "fieldC");
        var parent = CreateParentSymbol();
        var borrower = CreateBorrowerSymbol();

        var state = new MethodFlowState();
        state.VariableStates[parent] = VariableOwnershipState.Live;
        state.BindingKinds[parent] = BindingKind.Owned;
        state.InitializeFieldStates(parent, type);

        var branch = state.Fork();

        // Main branch: freeze fieldA, fieldB Live, fieldC Live
        state.FreezeField(parent, fields[0], borrower);

        // Other branch: fieldA Live, move fieldB, fieldC Live
        branch.MoveField(parent, fields[1], TestMoveLocation);

        // Merge
        state.Merge(branch);

        // fieldA: LentFrozen (from main) vs Live (from branch) → LentFrozen (more restrictive)
        Assert.Equal(VariableOwnershipState.LentFrozen, state.GetFieldState(parent, fields[0]));

        // fieldB: Live (from main) vs Moved (from branch) → Moved (more restrictive)
        Assert.Equal(VariableOwnershipState.Moved, state.GetFieldState(parent, fields[1]));

        // fieldC: Live in both → Live
        Assert.Equal(VariableOwnershipState.Live, state.GetFieldState(parent, fields[2]));
    }

    [Fact]
    public void Fork_PreservesFieldMoveLocations()
    {
        var (type, fields) = CreateTypeWithOwnedFields("fieldA");
        var parent = CreateParentSymbol();
        var moveSpan = new SourceSpan(42, 7);

        var state = new MethodFlowState();
        state.VariableStates[parent] = VariableOwnershipState.Live;
        state.BindingKinds[parent] = BindingKind.Owned;
        state.InitializeFieldStates(parent, type);

        state.MoveField(parent, fields[0], moveSpan);

        var forked = state.Fork();

        // Forked copy has the move location
        Assert.True(forked.FieldMoveLocations.ContainsKey((parent, fields[0])));
        Assert.Equal(moveSpan, forked.FieldMoveLocations[(parent, fields[0])]);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Additional composite state scenarios
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CompositeState_AllFieldsLive_ReturnsLive()
    {
        var (type, _) = CreateTypeWithOwnedFields("fieldA", "fieldB", "fieldC");
        var parent = CreateParentSymbol();

        var state = new MethodFlowState();
        state.VariableStates[parent] = VariableOwnershipState.Live;
        state.BindingKinds[parent] = BindingKind.Owned;
        state.InitializeFieldStates(parent, type);

        Assert.Equal(VariableOwnershipState.Live, state.CompositeState(parent));
    }

    [Fact]
    public void CompositeState_OneFieldBorrowed_ReturnsLentRef()
    {
        var (type, fields) = CreateTypeWithOwnedFields("fieldA", "fieldB");
        var parent = CreateParentSymbol();
        var borrower = CreateBorrowerSymbol();

        var state = new MethodFlowState();
        state.VariableStates[parent] = VariableOwnershipState.Live;
        state.BindingKinds[parent] = BindingKind.Owned;
        state.InitializeFieldStates(parent, type);

        state.BorrowField(parent, fields[0], borrower);

        Assert.Equal(VariableOwnershipState.LentRef, state.CompositeState(parent));
    }

    [Fact]
    public void CompositeState_OneFieldReadonlyRef_ReturnsLentReadonly()
    {
        var (type, fields) = CreateTypeWithOwnedFields("fieldA", "fieldB");
        var parent = CreateParentSymbol();
        var borrower = CreateBorrowerSymbol();

        var state = new MethodFlowState();
        state.VariableStates[parent] = VariableOwnershipState.Live;
        state.BindingKinds[parent] = BindingKind.Owned;
        state.InitializeFieldStates(parent, type);

        state.ReadonlyRefField(parent, fields[0], borrower);

        Assert.Equal(VariableOwnershipState.LentReadonly, state.CompositeState(parent));
    }

    [Fact]
    public void CompositeState_MixedFieldStates_ReturnsMostRestrictive()
    {
        var (type, fields) = CreateTypeWithOwnedFields("fieldA", "fieldB", "fieldC");
        var parent = CreateParentSymbol();
        var borrowerA = CreateBorrowerSymbol("bA");
        var borrowerB = CreateBorrowerSymbol("bB");

        var state = new MethodFlowState();
        state.VariableStates[parent] = VariableOwnershipState.Live;
        state.BindingKinds[parent] = BindingKind.Owned;
        state.InitializeFieldStates(parent, type);

        // fieldA: ReadonlyRef, fieldB: Frozen, fieldC: Live
        state.ReadonlyRefField(parent, fields[0], borrowerA);
        state.FreezeField(parent, fields[1], borrowerB);

        // Ordering per design (Req 16 AC 9): Live < LentReadonly < LentFrozen < LentRef = Moved
        // Enum values: Live=0, LentReadonly=1, LentFrozen=2, LentRef=3, Moved=4
        // MoreRestrictiveState picks the higher int value → correctly selects LentFrozen(2)
        // over LentReadonly(1) as more restrictive. LentFrozen is the composite.
        Assert.Equal(VariableOwnershipState.LentFrozen, state.CompositeState(parent));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // InitializeFieldStates — only owned fields tracked
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void InitializeFieldStates_OnlyOwnedFieldsTracked()
    {
        var scope = new Scope(parent: null);

        // One owned field
        var ownedField = new Symbol("data", SymbolKind.Field, SymbolOrigin.UserCode,
            scope, declaredType: null, declarationSpan: new SourceSpan(0, 4))
        {
            IsOwnedField = true
        };
        scope.AddMember(ownedField);

        // One non-owned field
        var regularField = new Symbol("count", SymbolKind.Field, SymbolOrigin.UserCode,
            scope, declaredType: null, declarationSpan: new SourceSpan(10, 5))
        {
            IsOwnedField = false
        };
        scope.AddMember(regularField);

        var classType = new ClassType("Mixed", scope, containingNamespace: "Test");
        var parent = CreateParentSymbol();

        var state = new MethodFlowState();
        state.InitializeFieldStates(parent, classType);

        // Owned field is tracked
        Assert.Equal(VariableOwnershipState.Live, state.GetFieldState(parent, ownedField));

        // Non-owned field is NOT tracked
        Assert.Null(state.GetFieldState(parent, regularField));
    }

    [Fact]
    public void InitializeFieldStates_StaticFieldsNotTracked()
    {
        var scope = new Scope(parent: null);

        var staticField = new Symbol("Instance", SymbolKind.Field, SymbolOrigin.UserCode,
            scope, declaredType: null, declarationSpan: new SourceSpan(0, 8))
        {
            IsOwnedField = true,
            IsStatic = true
        };
        scope.AddMember(staticField);

        var classType = new ClassType("Singleton", scope, containingNamespace: "Test");
        var parent = CreateParentSymbol();

        var state = new MethodFlowState();
        state.InitializeFieldStates(parent, classType);

        // Static owned fields are skipped
        Assert.Null(state.GetFieldState(parent, staticField));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Scope-based field borrow restoration
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ExitScope_RestoresBorrowedFieldToLive()
    {
        var (type, fields) = CreateTypeWithOwnedFields("fieldA");
        var parent = CreateParentSymbol();
        var borrower = CreateBorrowerSymbol();

        var state = new MethodFlowState();
        state.VariableStates[parent] = VariableOwnershipState.Live;
        state.BindingKinds[parent] = BindingKind.Owned;
        state.InitializeFieldStates(parent, type);

        state.EnterScope();
        state.BorrowField(parent, fields[0], borrower);

        Assert.Equal(VariableOwnershipState.LentRef, state.GetFieldState(parent, fields[0]));

        state.ExitScope();

        // After scope exit, the borrow is released and the field returns to Live
        Assert.Equal(VariableOwnershipState.Live, state.GetFieldState(parent, fields[0]));
    }

    [Fact]
    public void ExitScope_RestoresFrozenFieldToLive()
    {
        var (type, fields) = CreateTypeWithOwnedFields("fieldA");
        var parent = CreateParentSymbol();
        var borrower = CreateBorrowerSymbol();

        var state = new MethodFlowState();
        state.VariableStates[parent] = VariableOwnershipState.Live;
        state.BindingKinds[parent] = BindingKind.Owned;
        state.InitializeFieldStates(parent, type);

        state.EnterScope();
        state.FreezeField(parent, fields[0], borrower);

        Assert.Equal(VariableOwnershipState.LentFrozen, state.GetFieldState(parent, fields[0]));

        state.ExitScope();

        Assert.Equal(VariableOwnershipState.Live, state.GetFieldState(parent, fields[0]));
    }

    [Fact]
    public void ExitScope_RestoresReadonlyRefFieldToLive()
    {
        var (type, fields) = CreateTypeWithOwnedFields("fieldA");
        var parent = CreateParentSymbol();
        var borrower = CreateBorrowerSymbol();

        var state = new MethodFlowState();
        state.VariableStates[parent] = VariableOwnershipState.Live;
        state.BindingKinds[parent] = BindingKind.Owned;
        state.InitializeFieldStates(parent, type);

        state.EnterScope();
        state.ReadonlyRefField(parent, fields[0], borrower);

        Assert.Equal(VariableOwnershipState.LentReadonly, state.GetFieldState(parent, fields[0]));

        state.ExitScope();

        Assert.Equal(VariableOwnershipState.Live, state.GetFieldState(parent, fields[0]));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // RestoreField only restores Moved fields (not lent fields)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void RestoreField_OnlyRestoresMovedState_NotLentState()
    {
        var (type, fields) = CreateTypeWithOwnedFields("fieldA");
        var parent = CreateParentSymbol();
        var borrower = CreateBorrowerSymbol();

        var state = new MethodFlowState();
        state.VariableStates[parent] = VariableOwnershipState.Live;
        state.BindingKinds[parent] = BindingKind.Owned;
        state.InitializeFieldStates(parent, type);

        // Freeze the field
        state.FreezeField(parent, fields[0], borrower);
        Assert.Equal(VariableOwnershipState.LentFrozen, state.GetFieldState(parent, fields[0]));

        // RestoreField should NOT change LentFrozen to Live (it only handles Moved)
        state.RestoreField(parent, fields[0]);
        Assert.Equal(VariableOwnershipState.LentFrozen, state.GetFieldState(parent, fields[0]));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetFieldState returns null for untracked parent/field combinations
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetFieldState_UntrackedParent_ReturnsNull()
    {
        var parent = CreateParentSymbol();
        var field = CreateBorrowerSymbol("someField"); // just a symbol

        var state = new MethodFlowState();

        Assert.Null(state.GetFieldState(parent, field));
    }

    [Fact]
    public void CompositeState_UntrackedParent_ReturnsLive()
    {
        var parent = CreateParentSymbol();

        var state = new MethodFlowState();

        Assert.Equal(VariableOwnershipState.Live, state.CompositeState(parent));
    }
}