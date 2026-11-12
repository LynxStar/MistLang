namespace Mist.ProgramModel.Contracts.Types;

/// <summary>
/// A base type narrowed by a predicate, such as <c>int >= 0</c>.
/// </summary>
public interface IRefinedType : IMistType
{
    IMistType BaseType { get; }
}