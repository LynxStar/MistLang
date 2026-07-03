using Mist.Generated;
using Mist.ProgramModel.Contracts;

namespace Mist.ProgramModel.Model;

/// <summary>
/// An abstract (interface-like) type definition in the program model.
/// </summary>
internal sealed class AbstractType(
    string name, string namespaceName, Scope scope, MistType? declaringType, TypeDeclarationNode declarationNode)
    : MemberType(name, namespaceName, scope, declaringType, declarationNode),
    IAbstractType;