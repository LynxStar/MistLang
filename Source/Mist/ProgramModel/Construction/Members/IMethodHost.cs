using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Members;

/// <summary>
/// Lets the member builders work against both a <see cref="MemberType"/> and an
/// <see cref="ExtensionBlock"/>.
/// </summary>
internal interface IMethodHost
{
    Scope Scope { get; }

    string NamespaceName { get; }

    string Name { get; }

    List<Method> MethodsList { get; }

    List<Property> PropertiesList { get; }

    bool IsAbstract { get; }

    bool IsIntrinsic { get; }
}