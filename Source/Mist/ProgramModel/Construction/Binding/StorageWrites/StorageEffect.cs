using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding.StorageWrites;

/// <summary>
/// A call from one member's body into another's classification: the callee, the direction it is reached
/// in, and what the site binds the callee's type parameters to, in terms of the caller's.
/// </summary>
internal readonly record struct StorageEdge(object Callee, StorageAccess Side, TypeSubstitution Substitution);

/// <summary>
/// What one body does to storage typed by type parameters, before any callee's classification is
/// known: the parameters whose storage it writes itself, or hands to a callee no body can be read for,
/// and the calls whose classifications it inherits.
/// </summary>
internal sealed class StorageEffect
{
    public HashSet<TypeParameter> Direct { get; } = new(ReferenceEqualityComparer.Instance);

    public List<StorageEdge> Edges { get; } = [];

    public void Absorb(StorageEffect other)
    {
        Direct.UnionWith(other.Direct);
        Edges.AddRange(other.Edges);
    }
}