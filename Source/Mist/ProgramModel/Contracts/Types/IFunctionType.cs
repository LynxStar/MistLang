namespace Mist.ProgramModel.Contracts.Types;

/// <summary>
/// Purely structural: identity depends on the parameter types and the result, never on where it was
/// written or what it was called.
/// <para>
/// Source spells the result LAST, sharing one syntax across every arity, so a func with one type argument
/// takes nothing and returns it. The split into parameters and a result is made once, at resolution, so
/// nothing downstream re-derives which slot the result occupies.
/// </para>
/// </summary>
public interface IFunctionType : IMistType
{
    IReadOnlyList<IMistType> ParameterTypes { get; }

    /// <summary>
    /// A void result is the unit type, not an absence.
    /// </summary>
    IMistType ReturnType { get; }

    /// <summary>
    /// Written <c>pure</c>. Part of the identity, so the same shape with and without it are two types;
    /// which converts to which is <see cref="Construction.Binding.ConversionRelation"/>'s.
    /// </summary>
    bool IsPure { get; }
}