using Mist.Semantics.Contracts;

namespace Mist.Analysis.Builtin;

/// <summary>
/// Tracks the definite-assignment state during forward dataflow analysis.
/// Records which constructor fields and local variables have been definitely assigned
/// on the current control-flow path, plus whether the path has terminated (e.g., via return/throw).
/// </summary>
internal sealed class AssignmentState
{
    /// <summary>
    /// Instance field names (this.fieldName) that have been definitely assigned on the current path.
    /// </summary>
    public HashSet<string> AssignedFields { get; }

    /// <summary>
    /// Local variable symbols that have been definitely assigned on the current path.
    /// </summary>
    public HashSet<ISymbol> AssignedLocals { get; }

    /// <summary>
    /// Indicates whether the current path has terminated (e.g., hit a return, throw, or break),
    /// making downstream code unreachable.
    /// </summary>
    public bool Terminated { get; set; }

    /// <summary>
    /// Creates a new empty assignment state.
    /// </summary>
    public AssignmentState()
    {
        AssignedFields = new HashSet<string>(StringComparer.Ordinal);
        AssignedLocals = new HashSet<ISymbol>(ReferenceEqualityComparer.Instance);
        Terminated = false;
    }

    /// <summary>
    /// Creates an assignment state seeded with an initial set of assigned fields.
    /// </summary>
    /// <param name="initiallyAssignedFields">Field names that are pre-assigned (e.g., via initializer or synthetic default).</param>
    public AssignmentState(IEnumerable<string> initiallyAssignedFields)
    {
        AssignedFields = new HashSet<string>(initiallyAssignedFields, StringComparer.Ordinal);
        AssignedLocals = new HashSet<ISymbol>(ReferenceEqualityComparer.Instance);
        Terminated = false;
    }

    /// <summary>
    /// Creates a deep copy of this assignment state.
    /// </summary>
    public AssignmentState Clone()
    {
        var clone = new AssignmentState();
        clone.AssignedFields.UnionWith(AssignedFields);
        clone.AssignedLocals.UnionWith(AssignedLocals);
        clone.Terminated = Terminated;
        return clone;
    }

    /// <summary>
    /// Computes the intersection of this state with another at a branch merge point.
    /// After the merge, a location is considered assigned only if it was assigned on
    /// all non-terminated branches reaching the merge.
    /// <para>
    /// Rules:
    /// <list type="bullet">
    /// <item>If <paramref name="other"/> is terminated (unreachable), this branch's assignments are fully preserved.</item>
    /// <item>If <c>this</c> is terminated, adopt the other branch's assignments (the other branch is the only live path).</item>
    /// <item>If both are non-terminated, only fields/locals assigned in BOTH branches survive.</item>
    /// <item>If both are terminated, the result is also terminated (no live path exits the merge).</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="other">The assignment state from the other branch at the merge point.</param>
    public void IntersectWith(AssignmentState other)
    {
        if(other.Terminated && Terminated)
        {
            // Both branches terminated — no live path. Keep terminated.
            return;
        }

        if(other.Terminated)
        {
            // The other branch is dead; this branch is the only live path.
            // Keep this state as-is (all assignments from this branch survive).
            return;
        }

        if(Terminated)
        {
            // This branch is dead; the other branch is the only live path.
            // Adopt the other branch's assignments entirely.
            AssignedFields.Clear();
            AssignedFields.UnionWith(other.AssignedFields);
            AssignedLocals.Clear();
            AssignedLocals.UnionWith(other.AssignedLocals);
            Terminated = false;
            return;
        }

        // Both branches are live — only locations assigned on BOTH survive.
        AssignedFields.IntersectWith(other.AssignedFields);
        AssignedLocals.IntersectWith(other.AssignedLocals);
    }
}