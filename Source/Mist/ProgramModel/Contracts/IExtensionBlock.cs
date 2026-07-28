using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A single extension block declaration in the program model.
/// Each <c>extension(ReceiverType name) { ... }</c> block is represented
/// as its own <see cref="IExtensionBlock"/> with its own scope.
/// </summary>
public interface IExtensionBlock : IGenericDeclaration
{
    /// <summary>
    /// The resolved receiver type that this extension block targets.
    /// </summary>
    IMistType ReceiverType { get; }

    /// <summary>
    /// The receiver parameter name (e.g., "self").
    /// </summary>
    string ReceiverParameterName { get; }

    /// <summary>
    /// Methods declared in this extension block.
    /// </summary>
    IReadOnlyList<IMethod> Methods { get; }

    /// <summary>
    /// Properties declared in this extension block.
    /// </summary>
    IReadOnlyList<IProperty> Properties { get; }

    /// <summary>
    /// The namespace this extension block belongs to.
    /// </summary>
    string NamespaceName { get; }
}