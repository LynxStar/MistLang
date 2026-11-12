using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

internal sealed class ClassType(
    string name, string namespaceName, Scope? parentScope, MistType? declaringType, TypeDeclarationNode? declarationNode)
    : ConcreteType(name, namespaceName, parentScope, declaringType, declarationNode),
    IClassType;