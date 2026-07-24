using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Members;

/// <summary>
/// Abstraction over entities that host method and property declarations.
/// Implemented by <see cref="MemberType"/> (classes, structs, abstracts) and
/// <see cref="ExtensionBlock"/> to allow <see cref="MethodBuilder"/> and
/// <see cref="PropertyBuilder"/> to work against both.
/// </summary>
internal interface IMethodHost
{
    /// <summary>The host's own scope (methods create child scopes from this).</summary>
    Scope Scope { get; }

    /// <summary>The namespace the host belongs to.</summary>
    string NamespaceName { get; }

    /// <summary>The host's name for use in diagnostic messages.</summary>
    string Name { get; }

    /// <summary>Mutable list of methods resolved on this host.</summary>
    List<Method> MethodsList { get; }

    /// <summary>Mutable list of properties resolved on this host.</summary>
    List<Property> PropertiesList { get; }

    /// <summary>
    /// Whether this host is an abstract type definition (affects accessor validation).
    /// Extension blocks always return false.
    /// </summary>
    bool IsAbstract { get; }

    /// <summary>
    /// Whether this host is an intrinsic type (affects intrinsic member validation).
    /// Extension blocks always return false.
    /// </summary>
    bool IsIntrinsic { get; }
}