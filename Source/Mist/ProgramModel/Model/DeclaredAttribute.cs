using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Model;

/// <summary>
/// An attribute WRITTEN IN MIST SOURCE, resolved to its type with its arguments bound.
/// <para>
/// SEPARATE FROM <see cref="AttributeData"/>, WHICH IS THE SAME CONCEPT READ BACK FROM COMPILED
/// METADATA, and the split is forced rather than chosen. A metadata argument is already a value, so
/// <see cref="AttributeData"/> spells one <c>object?</c>; a declared argument is an expression that
/// has never been evaluated, and evaluating it here would mean writing a constant folder that
/// duplicates what the emitter can already print. Holding it as an <see cref="Expression"/> instead
/// buys unary minus, enum member access and type literals for nothing. That choice is what makes the
/// two irreconcilable: <see cref="Expression"/> is internal and <see cref="AttributeData"/> is a
/// public contract, so no one type can carry both payloads.
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