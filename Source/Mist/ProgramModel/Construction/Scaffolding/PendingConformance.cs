using Mist.Generated;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Construction.Scaffolding;

/// <summary>
/// A stashed with-declaration waiting for Phase 2c resolution.
/// Captures the AST node and the scope/namespace context from Phase 1.
/// </summary>
internal sealed record PendingConformance(WithDeclarationNode Declaration, Scope Scope, string NamespaceName);