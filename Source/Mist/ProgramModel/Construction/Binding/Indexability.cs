using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// Whether a receiver can be indexed or sliced, and what either yields. The ARGUMENT's type asks the
/// question -- an index reads one element out, a range reads a contiguous sub-sequence -- so an element
/// access cannot be typed from its receiver alone, and a list pattern needs both answers at once for
/// its positional elements and its slice.
/// </summary>
internal static class Indexability
{
    internal static bool RefusesElement(IMistType receiverType, IReadOnlyList<Indexer> indexers)
    {
        return MemberResolution.AnswersMembers(receiverType) && ArrayElement(receiverType) is null && indexers.Count == 0;
    }

    public static bool IsRange(IMistType type)
    {
        return type.FullyQualifiedName == WellKnownTypes.Range.FullyQualifiedName;
    }

    public static bool IsIndex(IMistType type)
    {
        return type.FullyQualifiedName == WellKnownTypes.Index.FullyQualifiedName;
    }

    public static IMistType? ArrayElement(IMistType type)
    {
        return type is ConstructedType constructed
            && constructed.Definition.FullyQualifiedName == WellKnownTypes.Array.FullyQualifiedName
            ? constructed.TypeArguments[0]
            : null
            ;
    }

    public static IMistType ElementType(IMistType receiverType, Expression index, BuilderContext context)
    {
        if(ArrayElement(receiverType) is { } element)
        {
            return element;
        }

        return Indexers(receiverType) is { Count: > 0 } indexers
            && Select(indexers, receiverType, index, context) is { Selected: { } selected } choice
            && Takes(choice, index.Type, context)
            ? IndexerType(selected, choice.Substitution, context)
            : ErrorType.Instance
            ;
    }

    public static IMistType SliceType(IMistType receiverType, BuilderContext context)
    {
        if(ArrayElement(receiverType) is { } element)
        {
            return context.Resolver.ConstructArray(element, null);
        }

        if(receiverType is ConstructedType { TypeArguments.Count: 2 } spanLike && SpanKind(spanLike.Definition) is { } kind)
        {
            return context.Resolver.ConstructLengthIndexed(kind, spanLike.TypeArguments[0], null);
        }

        return receiverType.FullyQualifiedName == WellKnownTypes.String.FullyQualifiedName
            ? receiverType
            : ErrorType.Instance
            ;
    }

    private static WellKnownType? SpanKind(IMistType definition)
    {
        return definition.FullyQualifiedName == WellKnownTypes.Span.FullyQualifiedName ? WellKnownTypes.Span
            : definition.FullyQualifiedName == WellKnownTypes.ReadOnlySpan.FullyQualifiedName ? WellKnownTypes.ReadOnlySpan
            : null;
    }

    /// <summary>
    /// Every indexer the receiver answers, its own and each one it inherits, paired with the
    /// constructed bases it was reached through so a generic base's slot closes as the receiver sees
    /// it. A call's candidates walk the chain through the scope tree; an index has no name to look up,
    /// so the walk is here.
    /// </summary>
    public static IReadOnlyList<Indexer> Indexers(IMistType receiverType)
    {
        return Reachable(receiverType).ConvertAll(reached => reached.Indexer);
    }

    private static List<(Indexer Indexer, IReadOnlyList<ConstructedType> Path)> Reachable(IMistType receiverType)
    {
        var found = new List<(Indexer, IReadOnlyList<ConstructedType>)>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var pending = new Queue<(IMistType Type, IReadOnlyList<ConstructedType> Path)>();
        pending.Enqueue((receiverType, []));

        while(pending.Count > 0)
        {
            var (type, path) = pending.Dequeue();

            if(type is TypeParameter parameter && seen.Add(parameter))
            {
                foreach(var constraint in parameter.ConstraintsList.OfType<TypeConstraint>())
                {
                    pending.Enqueue((constraint.ConstraintType, constraint.ConstraintType is ConstructedType bound ? [.. path, bound] : path));
                }

                continue;
            }

            if(((type as ConstructedType)?.Definition ?? type) is not MemberType member || !seen.Add(member))
            {
                continue;
            }

            foreach(var indexer in member.IndexersList)
            {
                found.Add((indexer, path));
            }

            foreach(var inherited in Bases(member))
            {
                pending.Enqueue((inherited, inherited is ConstructedType constructed ? [.. path, constructed] : path));
            }
        }

        return found;
    }

    private static IEnumerable<IMistType> Bases(MemberType member)
    {
        if(member is ConcreteType { BaseType: { } baseType })
        {
            yield return baseType;
        }

        foreach(var abstractBase in member.BaseAbstractTypes.Where(abstractBase => TypeMemberLookup.Searches(member, abstractBase)))
        {
            yield return abstractBase;
        }
    }

    /// <summary>
    /// A from-end index that no indexer takes as it is reaches the element through a position instead,
    /// as C# lowers <c>c[^1]</c> onto Count and an int indexer, so it selects as a list pattern's
    /// positional element does.
    /// </summary>
    public static OverloadChoice<Indexer> Select(
        IReadOnlyList<Indexer> indexers, IMistType receiverType, Expression index, BuilderContext context)
    {
        var constructed = receiverType as ConstructedType;
        var interner = context.ProgramModel.TypeInterner;
        var paths = Paths(receiverType);
        var choice = OverloadResolution.Select(indexers, [index], constructed, null, interner, null, paths);

        return choice.Selected is null && IsIndex(index.Type)
            ? OverloadResolution.Select(indexers, [Position(index.Node, context)], constructed, null, interner, null, paths)
            : choice
            ;
    }

    private static IReadOnlyDictionary<Indexer, IReadOnlyList<ConstructedType>>? Paths(IMistType receiverType)
    {
        var paths = new Dictionary<Indexer, IReadOnlyList<ConstructedType>>(ReferenceEqualityComparer.Instance);

        foreach(var (indexer, path) in Reachable(receiverType).Where(reached => reached.Path.Count > 0))
        {
            paths[indexer] = path;
        }

        return paths.Count == 0 ? null : paths;
    }

    public static Property? Count(IMistType receiverType)
    {
        if(Declaring(receiverType) is not { } declaring)
        {
            return null;
        }

        foreach(var name in CountNames)
        {
            if(TypeMemberLookup.Find(declaring, LookupRequest.Member(name, MemberVisibility.InstanceOnly, null)) is PropertyResult { Property: { Getter: not null } property }
                && IsInt(property.Type))
            {
                return property;
            }
        }

        return null;
    }

    private static readonly string[] CountNames = ["Length", "Count"];

    public static (IMistType Type, CountedSlice Read)? CountedSliceOf(IMistType receiverType, Storm.Runtime.SyntaxNode node, BuilderContext context)
    {
        if(Count(receiverType) is not { } count)
        {
            return null;
        }

        var range = new DefaultExpression(node, context.Resolver.LookupWellKnown(WellKnownTypes.Range));

        if(Indexers(receiverType) is { Count: > 0 } indexers
            && Select(indexers, receiverType, range, context) is { Selected: { Modifiers.IsIntrinsic: false } indexer } choice
            && Takes(choice, range.Type, context))
        {
            return (IndexerType(indexer, choice.Substitution, context), new CountedSlice(count, indexer, null));
        }

        return SelectSlice(receiverType, node, context) is { Selected: { } method } sliced
            ? (SliceResult(method, sliced.Substitution, context), new CountedSlice(count, null, method))
            : null;
    }

    /// <summary>
    /// The Slice C#'s implicit range support calls, which it takes only as an instance method of no type
    /// parameters whose two parameters are int values, so the candidates are narrowed to that shape before
    /// the ordinary selection weighs them. Null when the receiver has none of that shape.
    /// </summary>
    public static OverloadChoice<Method>? SelectSlice(IMistType receiverType, Storm.Runtime.SyntaxNode node, BuilderContext context)
    {
        if(Declaring(receiverType) is not { } declaring
            || TypeMemberLookup.Find(declaring, LookupRequest.Member(SliceName, MemberVisibility.InstanceOnly, null)) is not MethodGroupResult group)
        {
            return null;
        }

        var shaped = group.Methods.Where(IsSliceShape).ToList();
        var position = Position(node, context);

        return shaped.Count == 0
            ? null
            : OverloadResolution.Select(shaped, [position, position], Unwrapped(receiverType) as ConstructedType, [], context.ProgramModel.TypeInterner, paths: group.Paths);
    }

    private const string SliceName = "Slice";

    public static bool Takes(OverloadChoice<Indexer>? choice, IMistType argumentType, BuilderContext context)
    {
        var interner = context.ProgramModel.TypeInterner;

        if(choice is not { Selected.ParametersList: [var parameter] })
        {
            return false;
        }

        var slot = ConversionRelation.Unrefined(choice.Substitution.Apply(parameter.Type, interner));

        return (IsInt(argumentType) && IsIndex(slot))
            || ConversionClassifier.Classify(argumentType, slot, interner) is not (ConversionKind.None or ConversionKind.Explicit);
    }

    private static bool IsSliceShape(Method method)
    {
        return method is { TypeParametersList.Count: 0, ParametersList: [var start, var length] }
            && IsIntValue(start)
            && IsIntValue(length);
    }

    private static bool IsIntValue(Parameter parameter)
    {
        return parameter is { IsFlattened: false, Modifier: { IsRef: false, Direction: ByRefDirection.None } } && IsInt(parameter.Type);
    }

    public static IMistType SliceResult(Method slice, TypeSubstitution substitution, BuilderContext context)
    {
        return ClrInboundLifting.Close(
            slice.Modifiers,
            substitution.Apply(slice.ReturnType, context.ProgramModel.TypeInterner),
            context.Resolver.LookupWellKnown(WellKnownTypes.Void),
            context.ProgramModel.TypeInterner);
    }

    private static IMistType Unwrapped(IMistType receiverType)
    {
        var unaliased = receiverType is TypeAlias { AliasedType: { } aliased } ? aliased : receiverType;
        return unaliased is RefinedType refined ? refined.BaseType : unaliased;
    }

    private static MistType? Declaring(IMistType receiverType)
    {
        var receiver = Unwrapped(receiverType);
        return ((receiver as ConstructedType)?.Definition ?? receiver) as MistType;
    }

    private static bool IsInt(IMistType type)
    {
        return type switch
        {
            RefinedType refined => IsInt(refined.BaseType),
            TypeAlias { AliasedType: { } aliased } => IsInt(aliased),
            _ => type.FullyQualifiedName == WellKnownTypes.Int32.FullyQualifiedName,
        };
    }

    public static Expression Position(Storm.Runtime.SyntaxNode node, BuilderContext context)
    {
        return new DefaultExpression(node, context.Resolver.LookupWellKnown(WellKnownTypes.Int32));
    }

    public static IMistType IndexerType(Indexer indexer, TypeSubstitution substitution, BuilderContext context)
    {
        return ClrInboundLifting.Close(
            indexer.Modifiers,
            substitution.Apply(indexer.Type, context.ProgramModel.TypeInterner),
            context.Resolver.LookupWellKnown(WellKnownTypes.Void),
            context.ProgramModel.TypeInterner);
    }
}