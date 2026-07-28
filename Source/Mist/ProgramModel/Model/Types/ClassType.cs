using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A class type definition in the program model.
/// </summary>
internal sealed class ClassType(
    string name, string namespaceName, Scope scope, MistType? declaringType, TypeDeclarationNode? declarationNode)
    : ConcreteType(name, namespaceName, scope, declaringType, declarationNode),
    IClassType;