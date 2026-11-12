using Mist.Common.Diagnostics;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramModel.Construction.Members;

/// <summary>
/// The intrinsic rules fields, properties and indexers share, for a member that carries
/// <c>intrinsic</c>; each builder checks its own kind's rules (auto accessors, no body).
/// </summary>
internal static class IntrinsicMemberValidation
{
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