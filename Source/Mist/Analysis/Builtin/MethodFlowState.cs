using Mist.Common.Text;
using Mist.Semantics;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Analysis.Builtin;

/// <summary>
/// Maintains the flow-sensitive ownership state for a single method body during analysis.
/// Tracks variable states, move locations, active borrows, and the current scope depth.
/// Supports forking at branch points and merging at join points for control flow analysis.
/// </summary>
public sealed class MethodFlowState
{
    /// <summary>
    /// Maps each tracked variable to its current ownership state.
    /// </summary>
    public Dictionary<ISymbol, VariableOwnershipState> VariableStates { get; }

    /// <summary>
    /// Maps each tracked variable to its <see cref="BindingKind"/> (Owned or Unrestricted).
    /// Used to determine which access rules apply to the variable.
    /// </summary>
    public Dictionary<ISymbol, BindingKind> BindingKinds { get; }

    /// <summary>
    /// Maps each tracked droppable variable to its current <see cref="VariableDropState"/>.
    /// Populated and consumed by the <see cref="DropAnalyzer"/> flow-sensitive walk
    /// (independent of the ownership <see cref="VariableStates"/> tracking). Forked at
    /// branch points and merged conservatively at join points: a variable consumed
    /// (dropped, moved, or returned) in any branch is considered consumed at the join.
    /// </summary>
    public Dictionary<ISymbol, VariableDropState> DropStates { get; }

    /// <summary>
    /// Records the source location where each variable was moved, for diagnostic reporting.
    /// Only populated for variables in the <see cref="VariableOwnershipState.Moved"/> state.
    /// </summary>
    public Dictionary<ISymbol, SourceSpan> MoveLocations { get; }

    /// <summary>
    /// The list of currently active borrows. Entries are added when a variable is lent
    /// and removed when the borrow's scope exits.
    /// </summary>
    public List<BorrowEntry> ActiveBorrows { get; }

    /// <summary>
    /// The current lexical scope depth. Incremented on scope entry, decremented on scope exit.
    /// Used to determine which borrows to release when a scope ends.
    /// </summary>
    public int ScopeDepth { get; set; }

    /// <summary>
    /// Creates a new empty flow state at scope depth zero.
    /// </summary>
    public MethodFlowState()
    {
        VariableStates = new Dictionary<ISymbol, VariableOwnershipState>();
        BindingKinds = new Dictionary<ISymbol, BindingKind>();
        DropStates = new Dictionary<ISymbol, VariableDropState>();
        MoveLocations = new Dictionary<ISymbol, SourceSpan>();
        ActiveBorrows = new List<BorrowEntry>();
        ScopeDepth = 0;
    }

    /// <summary>
    /// Creates a flow state with the given initial values (used internally for cloning).
    /// </summary>
    private MethodFlowState(
        Dictionary<ISymbol, VariableOwnershipState> variableStates,
        Dictionary<ISymbol, BindingKind> bindingKinds,
        Dictionary<ISymbol, VariableDropState> dropStates,
        Dictionary<ISymbol, SourceSpan> moveLocations,
        List<BorrowEntry> activeBorrows,
        int scopeDepth)
    {
        VariableStates = variableStates;
        BindingKinds = bindingKinds;
        DropStates = dropStates;
        MoveLocations = moveLocations;
        ActiveBorrows = activeBorrows;
        ScopeDepth = scopeDepth;
    }

    /// <summary>
    /// Enters a new lexical scope by incrementing the scope depth.
    /// </summary>
    public void EnterScope()
    {
        ScopeDepth++;
    }

    /// <summary>
    /// Exits the current lexical scope. All borrows created at the current scope depth
    /// are released, and lender states are restored based on the borrow tier:
    /// <list type="bullet">
    ///   <item><description><see cref="OwnershipTier.Borrowed"/>: lender is restored to <see cref="VariableOwnershipState.Live"/>.</description></item>
    ///   <item><description><see cref="OwnershipTier.Frozen"/>: lender is restored to <see cref="VariableOwnershipState.Live"/> if no other frozen borrows remain.</description></item>
    ///   <item><description><see cref="OwnershipTier.ReadonlyRef"/>: no state change needed (lender retains full access).</description></item>
    /// </list>
    /// </summary>
    public void ExitScope()
    {
        // Collect borrows at the current depth to release.
        var toRelease = new List<BorrowEntry>();
        for (var i = ActiveBorrows.Count - 1; i >= 0; i--)
        {
            if (ActiveBorrows[i].ScopeDepth == ScopeDepth)
            {
                toRelease.Add(ActiveBorrows[i]);
                ActiveBorrows.RemoveAt(i);
            }
        }

        // Restore lender states for released borrows.
        foreach (var entry in toRelease)
        {
            RestoreLenderState(entry);
        }

        ScopeDepth--;
    }

    /// <summary>
    /// Creates a deep clone of this flow state for use at branch points.
    /// The cloned state is independent — mutations to one do not affect the other.
    /// </summary>
    /// <returns>A new <see cref="MethodFlowState"/> with copied data.</returns>
    public MethodFlowState Fork()
    {
        return new MethodFlowState(
            new Dictionary<ISymbol, VariableOwnershipState>(VariableStates),
            new Dictionary<ISymbol, BindingKind>(BindingKinds),
            new Dictionary<ISymbol, VariableDropState>(DropStates),
            new Dictionary<ISymbol, SourceSpan>(MoveLocations),
            new List<BorrowEntry>(ActiveBorrows),
            ScopeDepth);
    }

    /// <summary>
    /// Merges another flow state into this one at a join point (e.g., after if/else branches).
    /// Uses conservative merging:
    /// <list type="bullet">
    ///   <item><description>If a variable is <see cref="VariableOwnershipState.Moved"/> in either branch, it is <see cref="VariableOwnershipState.Moved"/> at the join.</description></item>
    ///   <item><description>Borrow entries from inner scopes (deeper than the current scope depth) are discarded before merging.</description></item>
    ///   <item><description>Move locations are preserved from whichever branch recorded the move.</description></item>
    /// </list>
    /// </summary>
    /// <param name="other">The flow state from the other branch to merge into this state.</param>
    public void Merge(MethodFlowState other)
    {
        // Merge variable states conservatively.
        foreach (var (symbol, otherState) in other.VariableStates)
        {
            if (!VariableStates.TryGetValue(symbol, out var thisState))
            {
                // Variable only exists in the other branch — take its state.
                VariableStates[symbol] = otherState;
                if (otherState == VariableOwnershipState.Moved &&
                    other.MoveLocations.TryGetValue(symbol, out var otherSpan))
                {
                    MoveLocations[symbol] = otherSpan;
                }
                continue;
            }

            // Conservative merge: Moved in any branch → Moved at join.
            if (thisState == VariableOwnershipState.Moved || otherState == VariableOwnershipState.Moved)
            {
                VariableStates[symbol] = VariableOwnershipState.Moved;

                // Preserve the move location from whichever branch has it.
                if (!MoveLocations.ContainsKey(symbol) &&
                    other.MoveLocations.TryGetValue(symbol, out var span))
                {
                    MoveLocations[symbol] = span;
                }
            }
            else if (thisState != otherState)
            {
                // If states differ but neither is Moved, take the more restrictive state.
                // LentRef > LentFrozen > LentReadonly > Live (more restrictive first).
                VariableStates[symbol] = MoreRestrictiveState(thisState, otherState);
            }
        }

        // Discard borrows from inner scopes (deeper than current scope depth) in the merged result.
        ActiveBorrows.RemoveAll(b => b.ScopeDepth > ScopeDepth);

        // Merge drop states conservatively: a variable consumed (Dropped/Moved/Returned)
        // in either branch is considered consumed at the join, so a use after the join is
        // an error. Live in both branches stays Live. When the two branches disagree on
        // WHICH consumed state applies, the specific state is not material to use-after
        // checks, so the existing state is retained (any non-Live state blocks use).
        foreach (var (symbol, otherDropState) in other.DropStates)
        {
            if (!DropStates.TryGetValue(symbol, out var thisDropState))
            {
                DropStates[symbol] = otherDropState;
                continue;
            }

            if (thisDropState == VariableDropState.Live && otherDropState != VariableDropState.Live)
            {
                DropStates[symbol] = otherDropState;
            }
        }

        // Merge binding kinds — binding kind is immutable after declaration,
        // so we just ensure variables from the other branch are included.
        foreach (var (symbol, kind) in other.BindingKinds)
        {
            BindingKinds.TryAdd(symbol, kind);
        }
    }

    /// <summary>
    /// Returns the more restrictive of two variable ownership states.
    /// Ordering (most to least restrictive): Moved > LentRef > LentFrozen > LentReadonly > Live.
    /// </summary>
    private static VariableOwnershipState MoreRestrictiveState(
        VariableOwnershipState a,
        VariableOwnershipState b)
    {
        return (int)a > (int)b ? a : b;
    }

    /// <summary>
    /// Restores the lender's variable state after a borrow is released.
    /// For <see cref="OwnershipTier.Borrowed"/> borrows, the lender is restored to Live.
    /// For <see cref="OwnershipTier.Frozen"/> borrows, the lender is restored to Live
    /// only if no other frozen borrows from the same lender remain active.
    /// For <see cref="OwnershipTier.ReadonlyRef"/> borrows, the lender is restored to Live
    /// only if no other readonly ref borrows from the same lender remain active.
    /// </summary>
    private void RestoreLenderState(BorrowEntry entry)
    {
        switch (entry.BorrowTier)
        {
            case OwnershipTier.Borrowed:
                // Borrowed borrow released: restore lender to Live.
                VariableStates[entry.Lender] = VariableOwnershipState.Live;
                break;

            case OwnershipTier.Frozen:
                // Frozen borrow released: restore lender to Live only if no other
                // frozen borrows from the same lender remain.
                if (!HasRemainingFrozenBorrows(entry.Lender))
                {
                    // If there are still readonly ref borrows, transition to LentReadonly.
                    if (HasRemainingReadonlyRefBorrows(entry.Lender))
                    {
                        VariableStates[entry.Lender] = VariableOwnershipState.LentReadonly;
                    }
                    else
                    {
                        VariableStates[entry.Lender] = VariableOwnershipState.Live;
                    }
                }
                break;

            case OwnershipTier.ReadonlyRef:
                // Readonly ref borrow released: restore lender to Live only if no other
                // readonly ref or frozen borrows from the same lender remain.
                if (!HasRemainingReadonlyRefBorrows(entry.Lender))
                {
                    if (!HasRemainingFrozenBorrows(entry.Lender))
                    {
                        VariableStates[entry.Lender] = VariableOwnershipState.Live;
                    }
                    // If frozen borrows remain, stay in LentFrozen (already set).
                }
                break;
        }
    }

    /// <summary>
    /// Checks whether any active frozen borrows remain for the given lender.
    /// </summary>
    private bool HasRemainingFrozenBorrows(ISymbol lender)
    {
        foreach (var borrow in ActiveBorrows)
        {
            if (ReferenceEquals(borrow.Lender, lender) && borrow.BorrowTier == OwnershipTier.Frozen)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Checks whether any active readonly ref borrows remain for the given lender.
    /// </summary>
    private bool HasRemainingReadonlyRefBorrows(ISymbol lender)
    {
        foreach (var borrow in ActiveBorrows)
        {
            if (ReferenceEquals(borrow.Lender, lender) && borrow.BorrowTier == OwnershipTier.ReadonlyRef)
            {
                return true;
            }
        }
        return false;
    }

    // ─── Per-field state tracking (owned-fields) ─────────────────────────

    /// <summary>
    /// Per-field ownership states, keyed by parent symbol → field symbol → state.
    /// Used by the owned-fields analysis to track individual field states independently.
    /// </summary>
    public Dictionary<ISymbol, Dictionary<ISymbol, VariableOwnershipState>> FieldStates { get; }
        = new();

    /// <summary>
    /// Gets the current ownership state of a specific field on a parent variable.
    /// Returns null if no specific state is recorded for the field.
    /// </summary>
    public VariableOwnershipState? GetFieldState(ISymbol parent, ISymbol? field)
    {
        if (field is null) return null;
        if (FieldStates.TryGetValue(parent, out var fieldMap) &&
            fieldMap.TryGetValue(field, out var state))
            return state;
        return null;
    }

    /// <summary>
    /// Restores a field from Moved state back to Live after a valid reassignment.
    /// </summary>
    public void RestoreField(ISymbol parent, ISymbol? field)
    {
        if (field is null) return;
        if (!FieldStates.TryGetValue(parent, out var fieldMap))
        {
            fieldMap = new Dictionary<ISymbol, VariableOwnershipState>();
            FieldStates[parent] = fieldMap;
        }
        fieldMap[field] = VariableOwnershipState.Live;
    }

    /// <summary>
    /// Initializes per-field state tracking for a parent binding. Only owned, non-static
    /// instance fields are tracked (Req 16 AC 1).
    /// </summary>
    public void InitializeFieldStates(ISymbol parent, IType type)
    {
        var scope = type switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            _ => null,
        };
        if (scope is null) return;

        var fieldMap = new Dictionary<ISymbol, VariableOwnershipState>();
        foreach (var member in scope.Members)
        {
            if (member.Kind == SymbolKind.Field &&
                member is Symbol sym &&
                sym.IsOwnedField && !sym.IsStatic)
            {
                fieldMap[member] = VariableOwnershipState.Live;
            }
        }
        if (fieldMap.Count > 0)
            FieldStates[parent] = fieldMap;
    }

    /// <summary>
    /// Borrows a field (exclusive mutable) — transitions it to LentRef and registers a borrow entry.
    /// </summary>
    public void BorrowField(ISymbol parent, ISymbol field, ISymbol borrower)
    {
        if (!FieldStates.TryGetValue(parent, out var fieldMap))
        {
            fieldMap = new Dictionary<ISymbol, VariableOwnershipState>();
            FieldStates[parent] = fieldMap;
        }
        fieldMap[field] = VariableOwnershipState.LentRef;
        ActiveBorrows.Add(new BorrowEntry(field, borrower, OwnershipTier.Borrowed, ScopeDepth));
    }

    /// <summary>
    /// Freezes a field — transitions it to LentFrozen and registers a borrow entry.
    /// </summary>
    public void FreezeField(ISymbol parent, ISymbol field, ISymbol borrower)
    {
        if (!FieldStates.TryGetValue(parent, out var fieldMap))
        {
            fieldMap = new Dictionary<ISymbol, VariableOwnershipState>();
            FieldStates[parent] = fieldMap;
        }
        fieldMap[field] = VariableOwnershipState.LentFrozen;
        ActiveBorrows.Add(new BorrowEntry(field, borrower, OwnershipTier.Frozen, ScopeDepth));
    }

    /// <summary>
    /// Readonly-refs a field — transitions it to LentReadonly and registers a borrow entry.
    /// </summary>
    public void ReadonlyRefField(ISymbol parent, ISymbol field, ISymbol borrower)
    {
        if (!FieldStates.TryGetValue(parent, out var fieldMap))
        {
            fieldMap = new Dictionary<ISymbol, VariableOwnershipState>();
            FieldStates[parent] = fieldMap;
        }
        fieldMap[field] = VariableOwnershipState.LentReadonly;
        ActiveBorrows.Add(new BorrowEntry(field, borrower, OwnershipTier.ReadonlyRef, ScopeDepth));
    }

    /// <summary>
    /// Computes the composite (most restrictive) ownership state across all tracked fields
    /// for a parent binding (Req 16 AC 9).
    /// </summary>
    public VariableOwnershipState CompositeState(ISymbol parent)
    {
        if (!FieldStates.TryGetValue(parent, out var fieldMap))
            return VariableOwnershipState.Live;

        var result = VariableOwnershipState.Live;
        foreach (var (_, fieldState) in fieldMap)
        {
            if ((int)fieldState > (int)result)
                result = fieldState;
        }
        return result;
    }

    /// <summary>
    /// Per-field move locations for diagnostic reporting.
    /// Keyed by (parent symbol, field symbol) → source span where the move occurred.
    /// </summary>
    public Dictionary<(ISymbol Parent, ISymbol Field), SourceSpan> FieldMoveLocations { get; }
        = new();

    /// <summary>
    /// Moves a field — transitions it to Moved and records the move location.
    /// </summary>
    public void MoveField(ISymbol parent, ISymbol field, SourceSpan moveLocation)
    {
        if (!FieldStates.TryGetValue(parent, out var fieldMap))
        {
            fieldMap = new Dictionary<ISymbol, VariableOwnershipState>();
            FieldStates[parent] = fieldMap;
        }
        fieldMap[field] = VariableOwnershipState.Moved;
        FieldMoveLocations[(parent, field)] = moveLocation;
    }
}
