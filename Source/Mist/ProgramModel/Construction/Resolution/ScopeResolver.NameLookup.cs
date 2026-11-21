using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Resolution;

internal sealed partial class ScopeResolver
{
    // ═══════════════════════════════════════════════════════════════════════
    // Name Lookup
    //
    // Resolving a bare name is the scopes' own job -- see Scope.Lookup. What remains here
    // are the two callers that need something narrower than a raw symbol: a type reference,
    // which must reject non-types, and a qualification step off a namespace.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// One qualification step off a namespace. Exposed here so binders have a single resolution
    /// facade to talk to; the semantics are <see cref="Model.ProgramModel.LookupQualified"/>'s.
    /// </summary>
    public LookupResult LookupQualified(string namespaceName, string memberName)
    {
        return context.ProgramModel.LookupQualified(namespaceName, memberName);
    }

    /// <summary>
    /// Requests <see cref="MemberVisibility.None"/>: a type-reference position can never
    /// denote a field, property or method, so members are excluded from consideration rather
    /// than matched and then rejected. Excluding them lets the lookup continue outward, so a
    /// field sharing its name with a type does not mask the type here -- while in an
    /// expression position, where both are legal, the field still wins.
    /// </summary>
    public IMistType ResolveNamedType(string name, Scope scope, Storm.Runtime.SyntaxNode locationNode)
    {
        // `object` is a SPELLING of unknown rather than a type of its own. Mist's top type is
        // unknown, which the backend lowers to object, and a program naming the C# spelling means the
        // same thing -- so it resolves here rather than failing to find a type nothing declares.
        if(string.Equals(name, "object", StringComparison.Ordinal))
        {
            return UnknownType.Instance;
        }

        var result = scope.Lookup(new LookupRequest(name, MemberVisibility.None, locationNode));

        switch(result)
        {
            case TypeResult typeResult:
                if(typeResult.IsFailed)
                {
                    MarkResolutionFailure();
                }

                context.ProgramModel.TypeNamings.Record(typeResult.Type, scope, locationNode);
                return typeResult.Type;

            case AmbiguousResult:
                ReportResolutionFailure(ProgramModelDiagnostics.AmbiguousType, locationNode, null, new NameValue(name), new NameValue("<scope>"), new NameValue("<scope>"));
                return ErrorType.Instance;

            case VariableResult:
                ReportResolutionFailure(ProgramModelDiagnostics.UnresolvedType, locationNode, null, new NameValue(name));
                return ErrorType.Instance;

            default:
                ReportResolutionFailure(ProgramModelDiagnostics.UnresolvedType, locationNode, null, new NameValue(name));
                return ErrorType.Instance;
        }
    }
}