using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
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
    internal readonly record struct MemberLookup(
        LookupResult Found, ConstructedType? Constructed, bool Reported);

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
    public static MemberLookup Resolve(
        IMistType type,
        string memberName,
        MemberVisibility visibility,
        SyntaxNode node,
        BuilderContext context,
        Scope? callSite = null,
        bool allowExtensionProperties = false,
        bool reportMissing = true)
    {
        var receiver = Answering(type);
        var constructed = receiver as ConstructedType;

        if((constructed?.Definition ?? receiver) is not MistType declaring)
        {
            return new MemberLookup(new NotFoundResult(), null, Reported: false);
        }

        var found = TypeMemberLookup.Find(declaring, LookupRequest.Member(memberName, visibility, node));
        HiddenMember? hidden = null;
        var closedWithUnknown = constructed is not null && UnknownViews.IsClosedWithUnknown(constructed);

        if(closedWithUnknown && found is MethodGroupResult declaredGroup)
        {
            found = (LookupResult?)UnknownViews.Filter(declaredGroup, constructed!, context, out hidden) ?? new NotFoundResult();
        }

        // Ahead of extensions and behind declared members, which is where a projection belongs: it
        // is the declaring type's own, so it beats an extension, but a member actually written
        // under the name beats it -- and writing one is MIST0412 rather than a shadowing.
        if(found is NotFoundResult
            && SpreadProjection.Find(constructed, declaring, memberName, context) is { } projected)
        {
            found = projected;
        }

        if(found is NotFoundResult && callSite is not null)
        {
            found = ExtensionLookup.Find(
                type, memberName, visibility, callSite, allowExtensionProperties, context.ProgramModel);

            if(closedWithUnknown && found is MethodGroupResult extensions)
            {
                found = (LookupResult?)UnknownViews.FilterExtensions(extensions, context, out var hiddenExtension) ?? new NotFoundResult();
                hidden ??= hiddenExtension;
            }
        }
        else if(memberName == "ToString"
            && visibility is MemberVisibility.InstanceOnly or MemberVisibility.InstanceAndStatic
            && found is MethodGroupResult group
            && !group.Methods.Any(ExtensionLookup.IsParameterlessToString)
            && callSite is not null
            && ExtensionLookup.FindUniversalToString(callSite, context.ProgramModel) is { Methods.Count: > 0 } universal)
        {
            found = new MethodGroupResult([.. group.Methods, .. universal.Methods], group.Paths);
        }

        var reported = (found is not NotFoundResult || reportMissing)
            && Report(found, declaring, receiver, memberName, node, context, hidden);

        return new MemberLookup(found, constructed, reported);
    }

    public static MemberLookup ResolveExtensionMethods(
        IMistType type, string memberName, MemberVisibility visibility, BuilderContext context, Scope callSite)
    {
        var found = ExtensionLookup.Find(
            type, memberName, visibility, callSite, allowProperties: false, context.ProgramModel);

        return new MemberLookup(found, Answering(type) as ConstructedType, Reported: false);
    }

    // A refinement is a nominal SUBTYPE of what it refines, so its members are that type's. The
    // predicate says which VALUES the type admits, never which members they answer to. An alias
    // named in expression position, `Task.Yield()`, is likewise its aliased type's.
    private static IMistType Answering(IMistType type)
    {
        var unaliased = type is TypeAlias { AliasedType: { } aliased } ? aliased : type;

        return unaliased is RefinedType refined ? refined.BaseType : unaliased;
    }

    internal readonly record struct StorageLookup(IMistType? Type, LookupResult Found)
    {
        public bool Reported { get; init; }
    }

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
        var (found, constructed, reported) = Resolve(type, memberName, MemberVisibility.InstanceAndStatic, node, context);

        var memberType = found switch
        {
            FieldResult { Field.Modifiers.IsStatic: false } field => field.Field.Type,
            PropertyResult { Property.Modifiers.IsStatic: false } property => property.Property.Type,
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
            found)
        { Reported = reported };
    }

    internal static bool IsStaticStorage(LookupResult found)
    {
        return found is FieldResult { Field.Modifiers.IsStatic: true } or PropertyResult { Property.Modifiers.IsStatic: true };
    }

    internal static SequenceValue Describe(AmbiguousResult ambiguous)
    {
        return new SequenceValue(SlotKind.Symbol, [.. ambiguous.Candidates.SelectMany(candidate => candidate switch
        {
            FieldResult field => new[] { Symbol(field.DeclaringType, field.Field.Name, $"field '{Qualified(field.DeclaringType, field.Field.Name)}'") },
            PropertyResult property => [Symbol(property.DeclaringType, property.Property.Name, $"property '{Qualified(property.DeclaringType, property.Property.Name)}'")],
            MethodGroupResult group => group.Methods.Select(method => DiagnosticSnapshot.Symbol(method, $"method '{Qualified(method.DeclaringType, method.Name)}'")),
            TypeResult type => [new SymbolValue(DiagnosticSnapshot.Type(type.Type).Identity, $"type '{type.Type.FullyQualifiedName}'")],
            EnumMemberResult member => [Symbol(member.DeclaringType, member.Member.Name, $"variant '{Qualified(member.DeclaringType, member.Member.Name)}'")],
            _ => [new SymbolValue(candidate.GetType().Name, candidate.ToString() ?? "")],
        })]);
    }

    private static SymbolValue Symbol(IMistType? owner, string name, string spelling)
    {
        var identity = owner is null ? "<local>" : DiagnosticSnapshot.Type(owner).Identity;
        return new SymbolValue($"{identity}.{name}", spelling);
    }

    private static string Qualified(IMistType? type, string memberName)
    {
        return type is null ? memberName : $"{type.FullyQualifiedName}.{memberName}";
    }

    public static bool AnswersMembers(IMistType type)
    {
        return type is not (ErrorType or UnknownType or NotProjectedType);
    }

    /// <summary>
    /// Reports a name that resolved to nothing, or to more than one kind of thing, and answers whether
    /// it did. The answer travels out on <see cref="MemberLookup.Reported"/> and is recorded on the
    /// access, because a caller cannot reconstruct it from the result alone: the same
    /// <see cref="NotFoundResult"/> is a refusal already spoken for on a type that answers members and
    /// a silence on one that does not.
    /// <para>
    /// Suppressed when the type is itself unresolved, because a type that already failed has already
    /// been reported on: every access through it would otherwise add a second error saying so -- one
    /// mistake, a cascade of complaints. An unknown receiver is silent here for the opposite reason --
    /// nothing has said anything yet, and refusing it is Stage 5's.
    /// </para>
    /// </summary>
    private static bool Report(
        LookupResult found,
        MistType declaring,
        IMistType receiver,
        string memberName,
        SyntaxNode node,
        BuilderContext context,
        HiddenMember? hidden)
    {
        if(!AnswersMembers(declaring))
        {
            return false;
        }

        switch(found)
        {
            case NotFoundResult when hidden is { } hiddenMember:
                UnknownViews.Report(hiddenMember, receiver, node, context);
                return true;

            case NotFoundResult:
                ProjectedWithholding.Report(context.Diagnostics, ProgramModelDiagnostics.UnresolvedMember, node,
                    ProjectedWithholding.Frames(declaring, memberName), DiagnosticSnapshot.Type(receiver), new NameValue(memberName));
                return true;

            case AmbiguousResult ambiguous:
                context.Diagnostics.ReportAtNodeTyped(
                    ProgramModelDiagnostics.AmbiguousMember, node, null,
                    DiagnosticSnapshot.Type(receiver), new NameValue(memberName), Describe(ambiguous));
                return true;

            default:
                return false;
        }
    }
}