using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Resolution;

/// <summary>
/// What a body has and a declaration does not: the means to read a name written in a dependent slot
/// as a value. Null from <see cref="Bind"/> says the name is not a value there, and the slot resolves
/// it as a type.
/// </summary>
internal interface ISlotValueBinder
{
    IMistType? Bind(QualifiedIdentifierNode name, TypeParameter slot);
}

internal sealed partial class ScopeResolver
{
    private ISlotValueBinder? slotValues;

    private (ArrayRankSuffixNode Rank, IReadOnlyList<Parameter> Parameters)? returnLength;

    public IMistType ResolveReturnType(TypeReferenceNode node, Scope scope, IReadOnlyList<Parameter> parameters)
    {
        var enclosing = returnLength;
        returnLength = node is { SingleTypeRefs.Length: 0 } && !HasOptionalToken(node.First)
            && GetArrayRank(node.First) is NamedSizedArrayRankArrayRankSuffixNode own
                ? (own, parameters)
                : null;

        try
        {
            return Resolve(node, scope);
        }
        finally
        {
            returnLength = enclosing;
        }
    }

    public TypeResolution ResolveWithFailure(TypeReferenceNode node, Scope scope, ISlotValueBinder values)
    {
        return ResolveWithFailure(() => WithSlotValues(values, () => Resolve(node, scope)));
    }

    public IMistType Resolve(TypeReferenceNode node, Scope scope, ISlotValueBinder values)
    {
        return WithSlotValues(values, () => Resolve(node, scope));
    }

    public IReadOnlyList<IMistType> ResolveTypeArguments(TypeArgumentListNode syntax, Scope scope, ISlotValueBinder values)
    {
        return WithSlotValues(values, () => ResolveTypeArguments(syntax, scope));
    }

    public IReadOnlyList<IMistType> ResolveTypeArguments(
        TypeArgumentListNode syntax, Scope scope, IMistType definition, ISlotValueBinder values)
    {
        return WithSlotValues(values, () => ResolveTypeArguments(syntax, scope, definition));
    }

    /// <summary>
    /// The arguments a method's stated list resolves to for one candidate, which alone says whether a
    /// position is a dependent slot that reads a value.
    /// </summary>
    public IReadOnlyList<IMistType> ResolveTypeArguments(
        TypeArgumentListNode syntax, Scope scope, IReadOnlyList<TypeParameter> parameters, ISlotValueBinder values)
    {
        return WithSlotValues(values, () => ResolveArguments(ArgumentNodes(syntax), scope, parameters));
    }

    public T WithValues<T>(ISlotValueBinder values, Func<T> resolve)
    {
        return WithSlotValues(values, resolve);
    }

    public static bool HasSlot(IReadOnlyList<TypeParameter> parameters, int written)
    {
        return Enumerable.Range(0, written).Any(position => SlotAt(parameters, written, position) is { IsDependent: true, IsPack: false });
    }

    private T WithSlotValues<T>(ISlotValueBinder? values, Func<T> resolve)
    {
        var enclosing = slotValues;
        slotValues = values;

        try
        {
            return resolve();
        }
        finally
        {
            slotValues = enclosing;
        }
    }

    private static TypeParameter? SlotAt(IReadOnlyList<TypeParameter> parameters, int written, int position)
    {
        if(PackBinding.AbsorbingSlot(parameters) is not { } absorbing)
        {
            return position < parameters.Count ? parameters[position] : null;
        }

        if(position < absorbing)
        {
            return parameters[position];
        }

        var fromEnd = parameters.Count - (written - position);

        return fromEnd > absorbing ? parameters[fromEnd] : null;
    }

    private IMistType? SlotValue(TypeArgumentNode argument, TypeParameter slot, Scope scope)
    {
        return argument is TypeReferenceTypeArgumentTypeArgumentNode
            {
                Type: { SingleTypeRefs.Length: 0, First: NamedSingleTypeRefSingleTypeRefNode { TypeArgs: null, ArrayRank: null, Optional: null, Pure: null } named }
            }
            ? SlotValue(named.Name, named, slot, scope, rank: null)
            : null;
    }

    private IMistType? SlotValue(QualifiedIdentifierNode name, Storm.Runtime.SyntaxNode written, TypeParameter slot, Scope scope, NamedSizedArrayRankArrayRankSuffixNode? rank)
    {
        var first = name.IDENTIFIER.Lexeme;

        if(slotValues is { } values)
        {
            return scope.Lookup(new LookupRequest(first, MemberVisibility.InstanceAndStatic, null)) is VariableResult or FieldResult or PropertyResult
                ? values.Bind(name, slot)
                : null;
        }

        var stem = name.IDENTIFIERs.Length > 0 ? QualifiedNameHelper.Build(name) : first;

        if(FindGenericDefinition(stem, arity: 0, scope) is not null || string.Equals(stem, "object", StringComparison.Ordinal)
            || (stem.Split('.') is [var parameterName, var slotName] && SlotThrough(parameterName, slotName, scope) is not null))
        {
            return null;
        }

        if(rank is not null && ReturnLengthParameter(rank) is { } named)
        {
            return ReturnLength(named, slot, name);
        }

        ReportResolutionFailure(ProgramModelDiagnostics.SlotValueOutsideBody, written, null, new NameValue(stem));
        return ErrorType.Instance;
    }

    private Parameter? ReturnLengthParameter(NamedSizedArrayRankArrayRankSuffixNode rank)
    {
        return returnLength is { } own && ReferenceEquals(own.Rank, rank) && rank.Length.IDENTIFIERs.Length == 0
            ? own.Parameters.FirstOrDefault(parameter => string.Equals(parameter.Name, rank.Length.IDENTIFIER.Lexeme, StringComparison.Ordinal))
            : null;
    }

    private IMistType ReturnLength(Parameter parameter, TypeParameter slot, QualifiedIdentifierNode name)
    {
        if(parameter.Modifier is not { IsReadonly: true, IsRef: false })
        {
            ReportResolutionFailure(ProgramModelDiagnostics.ReturnLengthNotReadonly, name, null, new NameValue(parameter.Name));
            return ErrorType.Instance;
        }

        return new ParameterValue(parameter, slot.Domain is { } domain ? ConversionRelation.Unrefined(domain) : LookupWellKnown(WellKnownTypes.Int32), name);
    }

    private IMistType ResolveNamedLength(NamedSizedArrayRankArrayRankSuffixNode rank, IMistType elementType, Scope scope)
    {
        var definition = LookupWellKnown(WellKnownTypes.Array);

        if(definition is not IHasTypeParameters { TypeParametersList: [_, { IsDependent: true } slot] })
        {
            ReportResolutionFailure(ProgramModelDiagnostics.UnresolvedType, rank, null, new NameValue(WellKnownTypes.Array.FullyQualifiedName));
            return ErrorType.Instance;
        }

        var length = SlotValue(rank.Length, rank.Length, slot, scope, rank) ?? ResolveNamed(rank.Length, null, scope, rank.Length);

        if(length is ErrorType && ReturnLengthParameter(rank) is not null)
        {
            return ErrorType.Instance;
        }

        if(length is not (ParameterValue or ErrorType))
        {
            context.ProgramModel.ConstructorConstraintClosures.RecordType(definition, [elementType, length], rank, boundIn: scope);
        }

        return ConstructLengthIndexed(WellKnownTypes.Array, elementType, length);
    }
}