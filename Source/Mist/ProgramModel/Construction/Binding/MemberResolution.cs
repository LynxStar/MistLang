using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// What a name means on a type, for every caller that already knows which type to ask -- a
/// qualified <c>receiver.X</c> and a member named in an initializer block. Both go through the same
/// lookup an unqualified name uses, so none of the three can disagree about what exists, what is
/// inherited, or what is reachable.
/// <para>
/// The answer is returned unreduced, since a method group has no single type until a call decides
/// between its overloads, and the callers differ on which kinds they accept at all.
/// </para>
/// </summary>
internal static class MemberResolution
{
    /// <param name="type">
    /// The type as written at the use site. Members are declared on the generic DEFINITION, so that
    /// is what the lookup runs against, and the construction is handed back because it supplies the
    /// arguments any answer has to be substituted against.
    /// </param>
    public static (LookupResult Found, ConstructedType? Constructed) Resolve(
        IMistType type,
        string memberName,
        MemberVisibility visibility,
        SyntaxNode node,
        BuilderContext context)
    {
        var constructed = type as ConstructedType;

        if((constructed?.Definition ?? type) is not MistType declaring)
        {
            return (new NotFoundResult(), null);
        }

        var found = TypeMemberLookup.Find(declaring, LookupRequest.Member(memberName, visibility, node));

        Report(found, declaring, memberName, node, context);

        return (found, constructed);
    }

    /// <summary>
    /// Reports a name that resolved to nothing, or to more than one kind of thing.
    /// <para>
    /// Suppressed when the type is itself unresolved. A type that already failed has already been
    /// reported on, and it has no members to speak of, so every access through it would otherwise
    /// add a second error saying so -- one mistake, a cascade of complaints.
    /// </para>
    /// </summary>
    private static void Report(
        LookupResult found,
        MistType declaring,
        string memberName,
        SyntaxNode node,
        BuilderContext context)
    {
        if(declaring is ErrorType or UnknownType or NotProjectedType)
        {
            return;
        }

        switch(found)
        {
            case NotFoundResult:
                context.Diagnostics.ReportAtNode(
                    ProgramModelDiagnostics.UnresolvedMember, node, null, declaring.Name, memberName);
                break;

            case AmbiguousResult ambiguous:
                context.Diagnostics.ReportAtNode(
                    ProgramModelDiagnostics.AmbiguousMember, node, null,
                    declaring.Name, memberName, ambiguous.Candidates.Count);
                break;
        }
    }
}