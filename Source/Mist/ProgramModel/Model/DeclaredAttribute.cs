using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Model;

/// <summary>
/// An attribute written in Mist source, resolved to its type with its arguments bound.
/// <para>
/// Separate from <see cref="AttributeData"/>, the same concept read back from compiled metadata,
/// because the payloads cannot share a type. A metadata argument is already a value; a declared one
/// stays an unevaluated <see cref="Expression"/>, which buys unary minus, enum member access and type
/// literals without a constant folder, and <see cref="Expression"/> is internal where
/// <see cref="AttributeData"/> is a public contract.
/// </para>
/// <para>
/// Never both on one declaration: a member is projected from an assembly or declared in source, and
/// the empty list is the one that says which.
/// </para>
/// </summary>
internal sealed class DeclaredAttribute(
    IMistType attributeType,
    IReadOnlyList<Expression> positionalArguments,
    IReadOnlyList<(string Name, Expression Value)> namedArguments)
{
    public IMistType AttributeType { get; } = attributeType;

    public IReadOnlyList<Expression> PositionalArguments { get; } = positionalArguments;

    public IReadOnlyList<(string Name, Expression Value)> NamedArguments { get; } = namedArguments;
}