using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Scopes;

/// <summary>
/// Callers that declare a type name accept this rather than <see cref="Scope"/>, so passing a scope
/// that cannot hold one is a compile error rather than a silently dropped declaration.
/// </summary>
internal interface ITypeNameScope
{
    void DeclareType(string name, IMistType type);
}