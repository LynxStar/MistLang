using Mist.Generated;

namespace Mist.ProgramModel.Construction.Scaffolding;

internal sealed record PendingUsing(UsingDeclarationNode Directive, string NamespaceName);