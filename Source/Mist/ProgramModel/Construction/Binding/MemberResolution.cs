using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// What a name means on a type, for every caller that already knows which type to ask -- a
/// qualified <c>receiver.X</c>, a member named in an initializer block, and a property pattern's
/// subpattern. All go through the same lookup an unqualified name uses, so none of them can disagree
/// about what exists, what is inherited, or what is reachable.
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
    /// <param name="callSite">
    /// The scope the access was WRITTEN in, supplied only by callers for whom an extension member is
    /// a legitimate answer. Its absence is the whole of the rule that an initializer target and a
    /// property pattern do not widen to extensions: they call through
    /// <see cref="ResolveStorage"/>, which passes nothing, so the miss path below never runs for them.
    /// </param>
    public static (LookupResult Found, ConstructedType? Constructed) Resolve(
        IMistType type,
        string memberName,
        MemberVisibility visibility,
        SyntaxNode node,
        BuilderContext context,
        Scope? callSite = null,
        bool allowExtensionProperties = false)
    {
        // A refinement is a nominal SUBTYPE of what it refines, so its members are that type's. The
        // predicate says which VALUES the type admits, never which members they answer to.
        var receiver = type is RefinedType refined ? refined.BaseType : type;
        var constructed = receiver as ConstructedType;

        if((constructed?.Definition ?? receiver) is not MistType declaring)
        {
            return (new NotFoundResult(), null);
        }

        var found = TypeMemberLookup.Find(declaring, LookupRequest.Member(memberName, visibility, node));

        // Ahead of extensions and behind declared members, which is where a projection belongs: it
        // is the declaring type's own, so it beats an extension, but a member actually written
        // under the name beats it -- and writing one is MIST_VAR_0007 rather than a shadowing.
        if(found is NotFoundResult
            && SpreadProjection.Find(constructed, declaring, memberName, context) is { } projected)
        {
            found = projected;
        }

        // Only on a miss, which is what makes "own members win" a consequence of where this sits
        // rather than a rule anything has to state. The receiver as WRITTEN is asked about, not the
        // definition: an extension is declared for `Box<int>` or for `unknown`, never for `Box<,>`.
        if(found is NotFoundResult && callSite is not null)
        {
            found = ExtensionLookup.Find(
                type, memberName, visibility, callSite, allowExtensionProperties, context.ProgramModel);
        }

        Report(found, declaring, memberName, node, context);

        return (found, constructed);
    }

    internal readonly record struct StorageLookup(IMistType? Type, LookupResult Found);

    /// <summary>
    /// The type of a field or property reached on an instance of <paramref name="type"/>, for the two
    /// callers that want storage and nothing else: an initializer block assigns to it and a property
    /// pattern matches against it, so neither has any use for a method group or a nested type.
    /// <para>
    /// <see cref="StorageLookup.Found"/> comes back alongside because a null type conflates two
    /// outcomes the callers must separate -- a name that resolved to something unusable, which each
    /// reports in its own words, and one <see cref="Resolve"/> has already reported on.
    /// </para>
    /// </summary>
    public static StorageLookup ResolveStorage(
        IMistType type, string memberName, SyntaxNode node, BuilderContext context)
    {
        var (found, constructed) = Resolve(type, memberName, MemberVisibility.InstanceOnly, node, context);

        var memberType = found switch
        {
            FieldResult field => field.Field.Type,
            PropertyResult property => property.Property.Type,
            _ => null,
        };

        return new StorageLookup(
            memberType is null
                ? null
                : ClrInboundLifting.Close(
                    found,
                    TypeSubstitution
                        .ForMember(constructed, found, context.ProgramModel.TypeInterner)
                        .Apply(memberType, context.ProgramModel.TypeInterner),
                    context.Resolver.LookupWellKnown(WellKnownTypes.Void),
                    context.ProgramModel.TypeInterner),
            found);
    }

    public static bool AnswersMembers(IMistType type)
    {
        return type is not (ErrorType or UnknownType or NotProjectedType);
    }

    /// <summary>
    /// Reports a name that resolved to nothing, or to more than one kind of thing.
    /// <para>
    /// Suppressed when the type is itself unresolved, because a type that already failed has already
    /// been reported on: every access through it would otherwise add a second error saying so -- one
    /// mistake, a cascade of complaints.
    /// </para>
    /// </summary>
    private static void Report(
        LookupResult found,
        MistType declaring,
        string memberName,
        SyntaxNode node,
        BuilderContext context)
    {
        if(!AnswersMembers(declaring))
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