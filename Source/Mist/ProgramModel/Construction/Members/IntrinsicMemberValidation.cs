using Mist.Common.Diagnostics;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramModel.Construction.Members;

/// <summary>
/// Validates the requirements common to every intrinsic data member — fields,
/// properties, and indexers. Two rules apply regardless of member kind:
/// the containing type must itself be declared <c>intrinsic</c>, and the member
/// must be <c>public</c>. Member-specific rules (auto accessors, no body) are
/// validated by the individual builders because they differ per member kind.
/// This is the single point of truth for the shared checks so they cannot drift.
/// </summary>
internal static class IntrinsicMemberValidation
{
    /// <summary>
    /// Reports diagnostics when an intrinsic member violates the shared rules.
    /// Call only when the member actually carries the <c>intrinsic</c> modifier.
    /// </summary>
    /// <param name="containerIsIntrinsic">Whether the enclosing type is intrinsic.</param>
    /// <param name="containerName">The enclosing type's name, for diagnostics.</param>
    /// <param name="isPublic">Whether the member is declared public.</param>
    /// <param name="locationNode">The declaration node to attach diagnostics to.</param>
    /// <param name="diagnostics">The diagnostic sink.</param>
    public static void Validate(
        bool containerIsIntrinsic,
        string containerName,
        bool isPublic,
        SyntaxNode locationNode,
        DiagnosticBag diagnostics)
    {
        if(!containerIsIntrinsic)
        {
            diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.IntrinsicRequiresIntrinsicType, locationNode, null, new NameValue(containerName));
        }

        if(!isPublic)
        {
            diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.IntrinsicMemberMustBePublic, locationNode, null);
        }
    }
}