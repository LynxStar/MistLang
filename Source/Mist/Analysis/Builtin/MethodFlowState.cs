using Mist.Common.Text;
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
        MoveLocations = new Dictionary<ISymbol, SourceSpan>();
        ActiveBorrows = new List<BorrowEntry>();
        ScopeDepth = 0;
    }

    /// <summary>
    /// Creates a flow state with the given initial values (used internally for cloning).
    /// </summary>
    private MethodFlowState(
        Dictionary<ISymbol, VariableOwnershipState> variableStates,
        Dictionary<ISymbol, SourceSpan> moveLocations,
        List<BorrowEntry> activeBorrows,
        int scopeDepth)
    {
        VariableStates = variableStates;
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
    /// For <see cref="OwnershipTier.ReadonlyRef"/> borrows, no restoration is needed
    /// (lender already retains full access).
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
                    VariableStates[entry.Lender] = VariableOwnershipState.Live;
                }
                break;

            case OwnershipTier.ReadonlyRef:
                // Readonly borrows don't restrict the lender; no restoration needed.
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
}
