using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Scopes;

/// <summary>
/// A scope that keeps its own table of the type names declared in it -- a nested type or a type
/// parameter.
/// <para>
/// Callers that declare one accept this rather than <see cref="Scope"/>, so passing a scope that
/// cannot hold a type name is a compile error rather than a silently dropped declaration.
/// </para>
/// </summary>
internal interface ITypeNameScope
{
    void DeclareType(string name, IMistType type);
}