using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A property declared on a type.
/// </summary>
public interface IProperty
{
    /// <summary>The property name.</summary>
    string Name { get; }

    /// <summary>The resolved type of the property.</summary>
    IMistType Type { get; }

    /// <summary>The modifiers declared on this property.</summary>
    MemberModifiers Modifiers { get; }
}