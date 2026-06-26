using Mist.Common.Text;
using Mist.Semantics;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

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
    /// Per-field ownership state for instances held in owned bindings.
    /// Outer key: the parent binding symbol. Inner key: field symbol. Value: current state.
    /// Only tracked for owned bindings whose type has owned fields (Req 16 AC 1).
    /// </summary>
    public Dictionary<ISymbol, Dictionary<ISymbol, VariableOwnershipState>> FieldStates { get; }

    /// <summary>
    /// Records the source location where each field was moved, for diagnostic reporting.
    /// Key: (parentSymbol, fieldSymbol). Only populated for fields in Moved state.
    /// </summary>
    public Dictionary<(ISymbol Parent, ISymbol Field), SourceSpan> FieldMoveLocations { get; }

    /// <summary>
    /// Active field-level borrows. Entries are added when a field is lent and removed
    /// when the borrow's scope exits, restoring the field state to Live.
    /// </summary>
    public List<FieldBorrowEntry> ActiveFieldBorrows { get; }

    /// <summary>
    /// Inferred ownership tiers for var-declared variables (from OwnershipInferencePass).
    /// Used by the this-tier call-site check to determine the effective tier of a receiver.
    /// Not forked/merged — computed once before the flow walk.
    /// </summary>
    public Dictionary<ISymbol, OwnershipTier> InferredTiers { get; }

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
        FieldStates = new Dictionary<ISymbol, Dictionary<ISymbol, VariableOwnershipState>>(ReferenceEqualityComparer.Instance);
        FieldMoveLocations = new Dictionary<(ISymbol Parent, ISymbol Field), SourceSpan>();
        ActiveFieldBorrows = new List<FieldBorrowEntry>();
        InferredTiers = new Dictionary<ISymbol, OwnershipTier>(ReferenceEqualityComparer.Instance);
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
        Dictionary<ISymbol, Dictionary<ISymbol, VariableOwnershipState>> fieldStates,
        Dictionary<(ISymbol Parent, ISymbol Field), SourceSpan> fieldMoveLocations,
        List<FieldBorrowEntry> activeFieldBorrows,
        int scopeDepth)
    {
        VariableStates = variableStates;
        BindingKinds = bindingKinds;
        DropStates = dropStates;
        MoveLocations = moveLocations;
        ActiveBorrows = activeBorrows;
        FieldStates = fieldStates;
        FieldMoveLocations = fieldMoveLocations;
        ActiveFieldBorrows = activeFieldBorrows;
        InferredTiers = new Dictionary<ISymbol, OwnershipTier>(ReferenceEqualityComparer.Instance);
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
        for(var i = ActiveBorrows.Count - 1; i >= 0; i--)
        {
            if(ActiveBorrows[i].ScopeDepth == ScopeDepth)
            {
                toRelease.Add(ActiveBorrows[i]);
                ActiveBorrows.RemoveAt(i);
            }
        }

        // Restore lender states for released borrows.
        foreach(var entry in toRelease)
        {
            RestoreLenderState(entry);
        }

        // Release field borrows at the current scope depth (Req 16 AC 7–8).
        for(var i = ActiveFieldBorrows.Count - 1; i >= 0; i--)
        {
            if(ActiveFieldBorrows[i].ScopeDepth == ScopeDepth)
            {
                var fieldBorrow = ActiveFieldBorrows[i];
                ActiveFieldBorrows.RemoveAt(i);

                // Restore field to Live if no other borrows on this field remain.
                if(!HasRemainingFieldBorrows(fieldBorrow.Parent, fieldBorrow.Field))
                {
                    SetFieldState(fieldBorrow.Parent, fieldBorrow.Field, VariableOwnershipState.Live);
                }
            }
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
        // Deep-clone FieldStates: each inner dictionary must be independently mutable.
        var clonedFieldStates = new Dictionary<ISymbol, Dictionary<ISymbol, VariableOwnershipState>>(
            FieldStates.Count, ReferenceEqualityComparer.Instance);
        foreach(var (parent, fields) in FieldStates)
        {
            clonedFieldStates[parent] = new Dictionary<ISymbol, VariableOwnershipState>(fields);
        }

        return new MethodFlowState(
            new Dictionary<ISymbol, VariableOwnershipState>(VariableStates),
            new Dictionary<ISymbol, BindingKind>(BindingKinds),
            new Dictionary<ISymbol, VariableDropState>(DropStates),
            new Dictionary<ISymbol, SourceSpan>(MoveLocations),
            new List<BorrowEntry>(ActiveBorrows),
            clonedFieldStates,
            new Dictionary<(ISymbol Parent, ISymbol Field), SourceSpan>(FieldMoveLocations),
            new List<FieldBorrowEntry>(ActiveFieldBorrows),
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
        foreach(var (symbol, otherState) in other.VariableStates)
        {
            if(!VariableStates.TryGetValue(symbol, out var thisState))
            {
                // Variable only exists in the other branch — take its state.
                VariableStates[symbol] = otherState;
                if(otherState == VariableOwnershipState.Moved &&
                    other.MoveLocations.TryGetValue(symbol, out var otherSpan))
                {
                    MoveLocations[symbol] = otherSpan;
                }

                continue;
            }

            // Conservative merge: Moved in any branch → Moved at join.
            if(thisState == VariableOwnershipState.Moved || otherState == VariableOwnershipState.Moved)
            {
                VariableStates[symbol] = VariableOwnershipState.Moved;

                // Preserve the move location from whichever branch has it.
                if(!MoveLocations.ContainsKey(symbol) &&
                    other.MoveLocations.TryGetValue(symbol, out var span))
                {
                    MoveLocations[symbol] = span;
                }
            }
            else if(thisState != otherState)
            {
                // If states differ but neither is Moved, take the more restrictive state.
                // LentRef > LentFrozen > LentReadonly > Live (more restrictive first).
                VariableStates[symbol] = MoreRestrictiveState(thisState, otherState);
            }
        }

        // Discard borrows from inner scopes (deeper than current scope depth) in the merged result.
        _ = ActiveBorrows.RemoveAll(b => b.ScopeDepth > ScopeDepth);

        // Merge drop states conservatively: a variable consumed (Dropped/Moved/Returned)
        // in either branch is considered consumed at the join, so a use after the join is
        // an error. Live in both branches stays Live. When the two branches disagree on
        // WHICH consumed state applies, the specific state is not material to use-after
        // checks, so the existing state is retained (any non-Live state blocks use).
        foreach(var (symbol, otherDropState) in other.DropStates)
        {
            if(!DropStates.TryGetValue(symbol, out var thisDropState))
            {
                DropStates[symbol] = otherDropState;
                continue;
            }

            if(thisDropState == VariableDropState.Live && otherDropState != VariableDropState.Live)
            {
                DropStates[symbol] = otherDropState;
            }
        }

        // Merge binding kinds — binding kind is immutable after declaration,
        // so we just ensure variables from the other branch are included.
        foreach(var (symbol, kind) in other.BindingKinds)
        {
            _ = BindingKinds.TryAdd(symbol, kind);
        }

        // Merge per-field states: for each parent, for each field, take the more restrictive state.
        foreach(var (parent, otherFields) in other.FieldStates)
        {
            if(!FieldStates.TryGetValue(parent, out var thisFields))
            {
                FieldStates[parent] = new Dictionary<ISymbol, VariableOwnershipState>(otherFields);
                continue;
            }

            foreach(var (field, otherFieldState) in otherFields)
            {
                if(!thisFields.TryGetValue(field, out var thisFieldState))
                {
                    thisFields[field] = otherFieldState;
                }
                else
                {
                    thisFields[field] = MoreRestrictiveState(thisFieldState, otherFieldState);
                }
            }
        }

        // Merge field move locations.
        foreach(var (key, span) in other.FieldMoveLocations)
        {
            _ = FieldMoveLocations.TryAdd(key, span);
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
        switch(entry.BorrowTier)
        {
            case OwnershipTier.Borrowed:
                // Borrowed borrow released: restore lender to Live.
                VariableStates[entry.Lender] = VariableOwnershipState.Live;
                break;

            case OwnershipTier.Frozen:
                // Frozen borrow released: restore lender to Live only if no other
                // frozen borrows from the same lender remain.
                if(!HasRemainingFrozenBorrows(entry.Lender))
                {
                    // If there are still readonly ref borrows, transition to LentReadonly.
                    if(HasRemainingReadonlyRefBorrows(entry.Lender))
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
                if(!HasRemainingReadonlyRefBorrows(entry.Lender))
                {
                    if(!HasRemainingFrozenBorrows(entry.Lender))
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
        foreach(var borrow in ActiveBorrows)
        {
            if(ReferenceEquals(borrow.Lender, lender) && borrow.BorrowTier == OwnershipTier.Frozen)
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
        foreach(var borrow in ActiveBorrows)
        {
            if(ReferenceEquals(borrow.Lender, lender) && borrow.BorrowTier == OwnershipTier.ReadonlyRef)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether any active field borrows remain for the given parent and field.
    /// </summary>
    private bool HasRemainingFieldBorrows(ISymbol parent, ISymbol field)
    {
        foreach(var borrow in ActiveFieldBorrows)
        {
            if(ReferenceEquals(borrow.Parent, parent) && ReferenceEquals(borrow.Field, field))
            {
                return true;
            }
        }

        return false;
    }

    // ── Per-field state operations (Req 16) ──────────────────────────────

    /// <summary>
    /// Returns the current state of a specific owned field within a parent binding.
    /// Returns null if the parent or field is not tracked.
    /// </summary>
    public VariableOwnershipState? GetFieldState(ISymbol parent, ISymbol field)
    {
        if(FieldStates.TryGetValue(parent, out var fields) &&
            fields.TryGetValue(field, out var state))
        {
            return state;
        }

        return null;
    }

    /// <summary>
    /// Restores a field from Moved to Live after a new owned value is written to it.
    /// This is the field restoration mechanism (Req 16 AC 6, 16).
    /// </summary>
    public void RestoreField(ISymbol parent, ISymbol field)
    {
        if(FieldStates.TryGetValue(parent, out var fields) &&
            fields.TryGetValue(field, out var state) &&
            state == VariableOwnershipState.Moved)
        {
            fields[field] = VariableOwnershipState.Live;
            _ = FieldMoveLocations.Remove((parent, field));
        }
    }

    /// <summary>
    /// Sets the state of a specific owned field within a parent binding.
    /// </summary>
    public void SetFieldState(ISymbol parent, ISymbol field, VariableOwnershipState newState)
    {
        if(FieldStates.TryGetValue(parent, out var fields))
        {
            fields[field] = newState;
        }
    }

    /// <summary>
    /// Computes the composite state of a parent binding: the most restrictive state
    /// among all its owned fields. Uses the design ordering for per-field composite:
    /// Live &lt; LentReadonly &lt; LentFrozen &lt; LentRef = Moved.
    /// </summary>
    public VariableOwnershipState CompositeState(ISymbol parent)
    {
        if(!FieldStates.TryGetValue(parent, out var fields))
        {
            return VariableOwnershipState.Live;
        }

        var max = VariableOwnershipState.Live;
        foreach(var state in fields.Values)
        {
            if(FieldStateRestrictiveness(state) > FieldStateRestrictiveness(max))
            {
                max = state;
            }
        }

        return max;
    }

    /// <summary>
    /// Maps a field state to its restrictiveness rank for composite state computation.
    /// Design ordering: Live(0) &lt; LentReadonly(1) &lt; LentFrozen(2) &lt; LentRef(3) &lt; Moved(4).
    /// Moved ranks above LentRef so that if any field is Moved, the composite state is Moved
    /// (Req 16 AC 11), while LentRef is composite only when no field is Moved (Req 16 AC 12).
    /// Both block the same whole-object operations in the truth table; the distinction ensures
    /// the reported composite state accurately reflects whether the parent is incomplete (Moved)
    /// or merely locked (LentRef).
    /// </summary>
    private static int FieldStateRestrictiveness(VariableOwnershipState state)
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
    /// Initializes per-field state for an owned binding whose type has owned fields.
    /// All owned fields start at Live.
    /// </summary>
    public void InitializeFieldStates(ISymbol parent, IEnumerable<ISymbol> ownedFields)
    {
        var fields = new Dictionary<ISymbol, VariableOwnershipState>(ReferenceEqualityComparer.Instance);
        foreach(var field in ownedFields)
        {
            fields[field] = VariableOwnershipState.Live;
        }

        FieldStates[parent] = fields;
    }

    /// <summary>
    /// Initializes per-field state by extracting owned fields from the given type's member scope.
    /// All owned fields start at Live. Static fields are excluded.
    /// </summary>
    public void InitializeFieldStates(ISymbol parent, IType type)
    {
        var scope = GetMemberScope(type);
        if(scope is null)
        {
            return;
        }

        var fields = new Dictionary<ISymbol, VariableOwnershipState>(ReferenceEqualityComparer.Instance);
        foreach(var member in scope.Members)
        {
            if(member.Kind == SymbolKind.Field && member is Symbol sym && sym.IsOwnedField && !sym.IsStatic)
            {
                fields[member] = VariableOwnershipState.Live;
            }
        }

        if(fields.Count > 0)
        {
            FieldStates[parent] = fields;
        }
    }

    /// <summary>
    /// Moves a field out — transitions it to Moved and records the move location.
    /// </summary>
    public void MoveField(ISymbol parent, ISymbol field, SourceSpan moveLocation)
    {
        if(FieldStates.TryGetValue(parent, out var fields))
        {
            fields[field] = VariableOwnershipState.Moved;
            FieldMoveLocations[(parent, field)] = moveLocation;
        }
    }

    /// <summary>
    /// Borrows a field — transitions it to LentRef and registers a field borrow entry.
    /// </summary>
    public void BorrowField(ISymbol parent, ISymbol field, ISymbol? borrower = null)
    {
        if(FieldStates.TryGetValue(parent, out var fields))
        {
            fields[field] = VariableOwnershipState.LentRef;
            if(borrower is not null)
            {
                ActiveFieldBorrows.Add(new FieldBorrowEntry(parent, field, borrower, OwnershipTier.Borrowed, ScopeDepth));
            }
        }
    }

    /// <summary>
    /// Freezes a field — transitions it to LentFrozen and registers a field borrow entry.
    /// </summary>
    public void FreezeField(ISymbol parent, ISymbol field, ISymbol? borrower = null)
    {
        if(FieldStates.TryGetValue(parent, out var fields))
        {
            fields[field] = VariableOwnershipState.LentFrozen;
            if(borrower is not null)
            {
                ActiveFieldBorrows.Add(new FieldBorrowEntry(parent, field, borrower, OwnershipTier.Frozen, ScopeDepth));
            }
        }
    }

    /// <summary>
    /// Takes a readonly ref of a field — transitions it to LentReadonly and registers a field borrow entry.
    /// </summary>
    public void ReadonlyRefField(ISymbol parent, ISymbol field, ISymbol? borrower = null)
    {
        if(FieldStates.TryGetValue(parent, out var fields))
        {
            fields[field] = VariableOwnershipState.LentReadonly;
            if(borrower is not null)
            {
                ActiveFieldBorrows.Add(new FieldBorrowEntry(parent, field, borrower, OwnershipTier.ReadonlyRef, ScopeDepth));
            }
        }
    }

    /// <summary>
    /// Returns the member scope of a type, resolving through ConstructedType if needed.
    /// </summary>
    private static IScope? GetMemberScope(IType type)
    {
        var resolved = type is Semantics.Model.Types.ConstructedType ct ? ct.GenericDefinition : type;
        return resolved switch
        {
            Semantics.Model.Types.ClassType classType => classType.MemberScope,
            Semantics.Model.Types.StructType structType => structType.MemberScope,
            _ => null,
        };
    }
}