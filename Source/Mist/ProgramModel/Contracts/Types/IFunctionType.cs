namespace Mist.ProgramModel.Contracts.Types;

/// <summary>
/// A function type composed of ordered parameter types and a result (e.g. <c>func&lt;int, bool&gt;</c>).
/// Purely structural: identity depends on the parameter types and the result, never on where it was
/// written or what it was called.
/// <para>
/// Source spells the result LAST, sharing one syntax across every arity, so <c>func&lt;bool&gt;</c> takes
/// nothing and returns a bool. The split into parameters and a result is made once, at resolution, so
/// nothing downstream re-derives which slot the result occupies.
/// </para>
/// </summary>
public interface IFunctionType : IMistType
{
    /// <summary>
    /// The resolved parameter types in positional order. Empty for a function taking nothing.
    /// </summary>
    IReadOnlyList<IMistType> ParameterTypes { get; }

    /// <summary>
    /// The resolved result type. A void result is the unit type, not an absence.
    /// </summary>
    IMistType ReturnType { get; }

    /// <summary>
    /// Whether the type was written <c>pure</c>: part of the identity, so the same shape with and
    /// without it are two types. Which converts to which is
    /// <see cref="Construction.Binding.ConversionRelation"/>'s.
    /// </summary>
    bool IsPure { get; }
}