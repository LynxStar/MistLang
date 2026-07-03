using Mist.ProgramModel.Contracts.Types;

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

    /// <summary>Whether the property has a getter.</summary>
    bool HasGetter { get; }

    /// <summary>Whether the property has a setter.</summary>
    bool HasSetter { get; }

    /// <summary>Whether the property is static.</summary>
    bool IsStatic { get; }
}