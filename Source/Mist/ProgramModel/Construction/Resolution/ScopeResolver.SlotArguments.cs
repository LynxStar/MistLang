using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
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

    public TypeResolution ResolveWithFailure(TypeReferenceNode node, Scope scope, ISlotValueBinder values)
    {
        return ResolveWithFailure(() => WithSlotValues(values, () => Resolve(node, scope)));
    }

    public IMistType Resolve(TypeReferenceNode node, Scope scope, ISlotValueBinder values)
    {
        return WithSlotValues(values, () => Resolve(node, scope));
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
        if(argument is not TypeReferenceTypeArgumentTypeArgumentNode
            {
                Type: { SingleTypeRefs.Length: 0, First: NamedSingleTypeRefSingleTypeRefNode { TypeArgs: null, ArrayRank: null, Optional: null, Pure: null } named }
            })
        {
            return null;
        }

        var first = named.Name.IDENTIFIER.Lexeme;

        if(slotValues is { } values)
        {
            return scope.Lookup(new LookupRequest(first, MemberVisibility.InstanceAndStatic, null)) is VariableResult or FieldResult or PropertyResult
                ? values.Bind(named.Name, slot)
                : null;
        }

        var stem = named.Name.IDENTIFIERs.Length > 0 ? QualifiedNameHelper.Build(named.Name) : first;

        if(FindGenericDefinition(stem, arity: 0, scope) is not null || string.Equals(stem, "object", StringComparison.Ordinal)
            || (stem.Split('.') is [var parameterName, var slotName] && SlotThrough(parameterName, slotName, scope) is not null))
        {
            return null;
        }

        ReportResolutionFailure(ProgramModelDiagnostics.SlotValueOutsideBody, named, null, new NameValue(stem));
        return ErrorType.Instance;
    }
}