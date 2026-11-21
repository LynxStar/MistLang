using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Resolution;

internal sealed partial class ScopeResolver
{
    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static ArrayRankSuffixNode? GetArrayRank(SingleTypeRefNode singleRef)
    {
        return singleRef switch
        {
            PrimitiveSingleTypeRefSingleTypeRefNode p => p.ArrayRank,
            NamedSingleTypeRefSingleTypeRefNode n => n.ArrayRank,
            NestedGenericSingleTypeRefSingleTypeRefNode nested => nested.ArrayRank,
            ParenthesizedTypeRefSingleTypeRefNode pr => pr.ArrayRank,
            FuncSingleTypeRefSingleTypeRefNode func => func.ArrayRank,
            _ => null,
        };
    }

    /// <summary>
    /// A method with no result asks for a task of void rather than a second task type, void being
    /// the unit value here.
    /// </summary>
    public IMistType ConstructTask(IMistType result)
    {
        return ConstructTask(WellKnownTypes.TaskGeneric, result);
    }

    /// <summary>
    /// The same construction over either task kind. They differ in what they promise about
    /// allocation and in nothing this layer cares about, so the caller names which.
    /// </summary>
    public IMistType ConstructTask(WellKnownType kind, IMistType result)
    {
        var definition = LookupWellKnown(kind);

        return definition is ErrorType
            ? definition
            : context.ProgramModel.TypeInterner.GetOrCreateConstructedType(definition, new[] { result })
            ;
    }

    /// <summary>
    /// The single place an array type is built. A null <paramref name="length"/> is a length not
    /// statically known; a known length is lifted into a <see cref="LiftedType"/> so
    /// <c>ArrayBounds</c> can constrain indexing. Callers that hold array syntax report their own
    /// diagnostic for the <see cref="ErrorType"/> returned when <c>Standard.Array</c> is not registered.
    /// </summary>
    public IMistType ConstructArray(IMistType elementType, int? length)
    {
        return ConstructLengthIndexed(WellKnownTypes.Array, elementType, length is { } known ? LiftInt(known) : null);
    }

    /// <summary>
    /// The same construction for any of the length-indexed Standard types, the spans included: they
    /// differ in where the memory lives and not in how the length is carried.
    /// </summary>
    public IMistType ConstructLengthIndexed(WellKnownType definition, IMistType elementType, IMistType? length)
    {
        var arrayDefinition = LookupWellKnown(definition);
        if(arrayDefinition is ErrorType)
        {
            return arrayDefinition;
        }

        // Lifted through the same helper as an explicit type argument, so a length
        // written as string[7] and one written as Array<string, 7> reach the same type.
        //
        // An unknown length carries its DOMAIN rather than being the bare top type, so a
        // `arr.Length` read can still be emitted as an int. The domain is int for the same
        // reason LiftInt lifts as int -- a length inhabits a refinement over int -- and it is
        // hardcoded here rather than read off the Length parameter because
        // TypeParameter.Domain is not populated yet.
        var lengthArgument = length
            ?? context.ProgramModel.TypeInterner.GetOrCreateUnknownValue(LookupWellKnown(WellKnownTypes.Int32));

        return context.ProgramModel.TypeInterner.GetOrCreateConstructedType(arrayDefinition, new IMistType[] { elementType, lengthArgument });
    }

    private IMistType ResolveArrayType(SingleTypeRefNode singleRef, IMistType elementType, Scope scope)
    {
        var rank = GetArrayRank(singleRef)!;
        int? length = null;

        if(rank is NamedSizedArrayRankArrayRankSuffixNode named)
        {
            return ResolveNamedLength(named, elementType, scope);
        }

        if(rank is SizedArrayRankArrayRankSuffixNode sized)
        {
            length = IntegerLiterals.Length(sized.Size.Lexeme);

            if(length is null)
            {
                ReportResolutionFailure(
                    ProgramModelDiagnostics.NumericLiteralOutOfRange, sized, null, new TextValue(sized.Size.Lexeme), new TypeValue(WellKnownTypes.Int32.FullyQualifiedName, WellKnownTypes.Int32.Keyword ?? WellKnownTypes.Int32.Name));
                return ErrorType.Instance;
            }
        }

        var arrayType = ConstructArray(elementType, length);
        if(arrayType is ErrorType)
        {
            ReportResolutionFailure(ProgramModelDiagnostics.UnresolvedType, singleRef, null, new NameValue(WellKnownTypes.Array.FullyQualifiedName));
        }

        return arrayType;
    }

    /// <summary>
    /// The semantics of unpacking are handled at the use site (type alias expansion, for one).
    /// </summary>
    private IMistType ResolveSpread(SpreadSingleTypeRefSingleTypeRefNode spread, Scope scope)
    {
        var name = spread.Name.IDENTIFIER.Lexeme;
        var resolved = ResolveNamedType(name, scope, spread);

        if(resolved is TypeParameter { IsPack: true } or LiftedType { Value: TypePackValue })
        {
            return new SpreadType(resolved);
        }

        ReportResolutionFailure(ProgramModelDiagnostics.UnknownTypeRefNode, spread, null, new NameValue(name));
        return ErrorType.Instance;
    }

    private IMistType ReportUnsupported(SingleTypeRefNode node)
    {
        ReportResolutionFailure(ProgramModelDiagnostics.UnknownTypeRefNode, node, null, new NameValue(node.GetType().Name));
        return ErrorType.Instance;
    }
}