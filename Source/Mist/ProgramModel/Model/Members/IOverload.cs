using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model.Members;

/// <summary>
/// A member a use site selects among by its arguments: a method or a constructor by a call, an indexer
/// by an index. Selection reads only the signature and where it was declared, so the three share one
/// set of applicability and betterness rules. An indexer declares no type parameters.
/// </summary>
internal interface IOverload
{
    List<Parameter> ParametersList { get; }

    List<TypeParameter> TypeParametersList { get; }

    MemberType? DeclaringType { get; }

    Storm.Runtime.SyntaxNode? DeclarationNode { get; }
}