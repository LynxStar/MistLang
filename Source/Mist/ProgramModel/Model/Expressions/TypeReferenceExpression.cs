using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// An expression that resolved to a type (e.g., <c>Console</c> in
/// <c>Console.WriteLine(...)</c>). Used as the receiver of a static member access.
/// The <see cref="Type"/> is the referenced type itself, except for a dependent parameter, which in
/// expression position reads its value and so has its domain's type.
/// </summary>
internal sealed class TypeReferenceExpression(
    SyntaxNode node,
    IMistType referencedType) : Expression(node)
{
    /// <summary>
    /// The type being referenced.
    /// </summary>
    public IMistType ReferencedType { get; } = referencedType;

    public override IMistType Type { get; } =
        referencedType is TypeParameter { IsDependent: true, IsPack: false, Domain: { } domain } ? domain : referencedType;
}