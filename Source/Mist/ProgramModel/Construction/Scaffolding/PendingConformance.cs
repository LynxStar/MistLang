using Mist.Generated;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Construction.Scaffolding;

internal sealed record PendingConformance(WithDeclarationNode Declaration, Scope Scope, string NamespaceName);