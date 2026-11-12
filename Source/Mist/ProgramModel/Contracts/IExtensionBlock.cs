using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// One per <c>extension(ReceiverType name) { ... }</c> block, each with its own scope, even where blocks
/// share a receiver type.
/// </summary>
public interface IExtensionBlock : IGenericDeclaration
{
    IMistType ReceiverType { get; }

    string ReceiverParameterName { get; }

    IReadOnlyList<IMethod> Methods { get; }

    IReadOnlyList<IProperty> Properties { get; }

    string NamespaceName { get; }
}