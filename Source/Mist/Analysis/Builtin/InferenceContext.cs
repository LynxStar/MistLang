using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Analysis.Builtin;

/// <summary>
/// Mutable context used during inference to accumulate type parameter bindings.
/// Tracks which type parameters have been bound to concrete types and records
/// conflicts when the same type parameter receives different bindings from
/// different argument positions.
/// </summary>
internal sealed class InferenceContext
{
    private readonly Dictionary<string, IType?> _bindings;
    private readonly List<(string TypeParameter, IType First, IType Second)> _conflicts = new();

    /// <summary>
    /// Initializes the context with one unbound entry per type parameter.
    /// </summary>
    /// <param name="typeParameters">
    /// The method's type parameters (from Symbol.TypeParameters).
    /// Only <see cref="TypeParameterType"/> entries are registered as bindable.
    /// </param>
    public InferenceContext(IReadOnlyList<IType> typeParameters)
    {
        _bindings = new Dictionary<string, IType?>();
        foreach (var tp in typeParameters)
        {
            if (tp is TypeParameterType tpt)
                _bindings[tpt.Name] = null;
        }
    }

    /// <summary>
    /// Attempts to bind a type parameter to a concrete type.
    /// Returns true if binding succeeded (new binding or consistent with existing).
    /// Returns false if a conflict was detected (the type parameter was already bound
    /// to a different concrete type).
    /// </summary>
    /// <param name="typeParameterName">The name of the type parameter to bind.</param>
    /// <param name="concreteType">The concrete type to bind it to.</param>
    /// <returns>True if binding succeeded; false if a conflict was recorded.</returns>
    public bool TryBind(string typeParameterName, IType concreteType)
    {
        if (!_bindings.TryGetValue(typeParameterName, out var existing))
            return false;

        if (existing is null)
        {
            _bindings[typeParameterName] = concreteType;
            return true;
        }

        // Already bound — check consistency by reference or display name.
        if (ReferenceEquals(existing, concreteType))
            return true;

        if (existing.DisplayName == concreteType.DisplayName)
            return true;

        // Conflict: same type parameter bound to two different concrete types.
        _conflicts.Add((typeParameterName, existing, concreteType));
        return false;
    }

    /// <summary>
    /// Returns the bound type for a type parameter, or null if unbound.
    /// </summary>
    /// <param name="typeParameterName">The name of the type parameter.</param>
    /// <returns>The bound concrete type, or null if not yet bound.</returns>
    public IType? GetBinding(string typeParameterName)
    {
        return _bindings.TryGetValue(typeParameterName, out var bound) ? bound : null;
    }

    /// <summary>
    /// Whether any conflicts were detected during binding.
    /// </summary>
    public bool HasConflicts => _conflicts.Count > 0;

    /// <summary>
    /// The list of conflicts detected during binding. Each entry contains the type
    /// parameter name and the two conflicting concrete types.
    /// </summary>
    public IReadOnlyList<(string TypeParameter, IType First, IType Second)> Conflicts => _conflicts;

    /// <summary>
    /// Returns the names of type parameters that remain unbound after inference.
    /// </summary>
    public IReadOnlyList<string> GetUnboundParameters()
    {
        var unbound = new List<string>();
        foreach (var (name, type) in _bindings)
        {
            if (type is null)
                unbound.Add(name);
        }

        return unbound;
    }
}
