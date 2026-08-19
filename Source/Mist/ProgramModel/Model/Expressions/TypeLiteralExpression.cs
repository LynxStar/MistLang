using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// A type spelled in VALUE position (<c>var t = int;</c>), producing the reflection object
/// for it. Distinct from <see cref="TypeReferenceExpression"/>, which is a type named as a
/// RECEIVER and never becomes a value.
/// </summary>
internal sealed class TypeLiteralExpression(
    SyntaxNode node,
    IMistType referencedType,
    IMistType type) : Expression(node)
{
    public IMistType ReferencedType { get; } = referencedType;

    public override IMistType Type { get; } = type;
}