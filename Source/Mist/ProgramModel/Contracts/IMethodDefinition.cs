using Mist.ProgramModel.Contracts.TypeReferences;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A method or constructor in the program model, with all types fully resolved.
/// </summary>
public interface IMethodDefinition
{
    /// <summary>
    /// The method name (e.g., "DoWork"). For constructors, this is "new".
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The resolved return type. Null for constructors and void-returning methods.
    /// </summary>
    ITypeReference? ReturnType { get; }

    /// <summary>
    /// The parameters of this method, in declaration order.
    /// </summary>
    IReadOnlyList<IParameterDefinition> Parameters { get; }

    /// <summary>
    /// Generic type parameters declared on this method (method-level generics).
    /// </summary>
    IReadOnlyList<ITypeParameterDefinition> TypeParameters { get; }

    /// <summary>
    /// Whether this method is declared static.
    /// </summary>
    bool IsStatic { get; }

    /// <summary>
    /// Whether this method is declared async.
    /// </summary>
    bool IsAsync { get; }

    /// <summary>
    /// Whether this method's body is provided by the backend (intrinsic).
    /// </summary>
    bool IsIntrinsic { get; }

    /// <summary>
    /// The type that declares this method.
    /// </summary>
    ITypeDefinition DeclaringType { get; }

    /// <summary>
    /// The source location where this method was declared.
    /// </summary>
    SourceOrigin? Origin { get; }
}