using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis;

internal readonly record struct FormatStep(TypeParameter? Open, IMistType Formatted, Method? Formatter);

internal readonly record struct FormatUse(object Callee, MemberModifiers Declared, MistType? Scope, Expression At, TypeSubstitution Closing);

/// <summary>
/// The formatter callbacks a body sets off, followed through generic closings. A hole or a string
/// concatenation operand whose type is a Mist <c>System.IFormattable</c> calls its two-argument
/// <c>ToString</c>, as does each element of a tuple and the value of an optional, which the target formats
/// in turn; a wider union is formatted as its carrier and calls nothing. Such a call is a
/// <see cref="FormatStep"/> naming its <see cref="FormatStep.Formatter"/>. A value whose type is a type
/// parameter is an <see cref="FormatStep.Open"/> step instead, since what it calls is decided where the
/// parameter is closed, and so is a call into a generic member that formats one of its own parameters, a
/// <see cref="FormatUse"/> whose closing says what that parameter is there. <see cref="Of"/> answers which
/// type parameters a member formats, a least fixed point over the members it calls.
/// <para>
/// A callee or formatter that DECLARES readonly is not followed. Its promise is judged where its formatted
/// parameters are closed, so following it as well would report one mistake twice. Only a type parameter in
/// scope of the member is collected: one a use leaves unbound belongs to some other declaration, and
/// promising it would judge closings the member never reaches. Only Mist types are judged, by their static
/// type, exactly as for a concrete hole.
/// </para>
/// </summary>
internal sealed class FormattingCallbacks
{
    private static readonly IReadOnlySet<TypeParameter> None = new HashSet<TypeParameter>();

    private readonly Dictionary<object, IReadOnlySet<TypeParameter>> settled = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, HashSet<TypeParameter>> settling = new(ReferenceEqualityComparer.Instance);

    public static IEnumerable<Expression> Sites(object node)
    {
        return node switch
        {
            InterpolatedStringExpression interpolated => interpolated.Parts.OfType<InterpolatedStringHole>().Select(hole => hole.Expression),
            BinaryExpression { Operator: null, OperatorKind: BinaryOperatorKind.Add or BinaryOperatorKind.Concatenate } concatenation
                when PurityJudge.DefinitionOf(concatenation.Type) is ConcreteType { IsIntrinsic: true, Name: "String" } => [concatenation.Left, concatenation.Right],
            _ => [],
        };
    }

    public static IEnumerable<FormatUse> Uses(object node, MistType? enclosing)
    {
        switch(node)
        {
            case InvocationExpression { Method: { } method } call:
                var declared = method.SourceDeclaration ?? method;
                var (receiver, typeArguments) = call.Receiver switch
                {
                    MemberAccessExpression access => (access.Receiver.Type, access.TypeArguments),
                    MethodReferenceExpression reference => (enclosing, reference.TypeArguments),
                    _ => (null, []),
                };
                var closing = method.BlockClosing
                    .Extend(Inherited(receiver, declared.DeclaringType))
                    .Extend(TypeSubstitution.ForParameters(declared.TypeParametersList, typeArguments));

                yield return new FormatUse(declared, declared.Modifiers, declared.DeclaringType, call, closing);
                break;

            case NewExpression { Constructor: { } constructor } allocation:
                yield return new FormatUse(
                    constructor, constructor.Modifiers, constructor.DeclaringType, allocation, TypeSubstitution.ForReceiver(Constructed(allocation.Type)));
                break;

            case MemberAccessExpression { Member: PropertyResult { Property: { Getter: { } getter } property } found } access:
                yield return new FormatUse(
                    getter, ReadonlyInference.Declared(getter, property.Modifiers), property.DeclaringType, access,
                    TypeSubstitution.ForMember(Constructed(access.Receiver.Type), found, interner: null));
                break;

            case IndexExpression { Indexer: { Getter: { } getter } indexer } index:
                yield return new FormatUse(
                    getter, ReadonlyInference.Declared(getter, indexer.Modifiers), indexer.DeclaringType, index, Inherited(index.Receiver.Type, indexer.DeclaringType));
                break;

            case BinaryExpression { Operator: { Modifiers.IsIntrinsic: false } binaryOperator } binary:
                yield return new FormatUse(binaryOperator, binaryOperator.Modifiers, binaryOperator.DeclaringType, binary, binary.OperatorClosing);
                break;

            case UnaryExpression { Operator: { Modifiers.IsIntrinsic: false } unaryOperator } unary:
                yield return new FormatUse(unaryOperator, unaryOperator.Modifiers, unaryOperator.DeclaringType, unary, unary.OperatorClosing);
                break;

            case CastExpression { Operator: { Modifiers.IsIntrinsic: false } conversion } cast:
                yield return new FormatUse(conversion, conversion.Modifiers, conversion.DeclaringType, cast, cast.OperatorClosing);
                break;

            case ImplicitConversionExpression { Operator: { Modifiers.IsIntrinsic: false } conversion } converted:
                yield return new FormatUse(conversion, conversion.Modifiers, conversion.DeclaringType, converted, converted.OperatorClosing);
                break;

            case PropertyReferenceExpression { Property: { Getter: { } getter } property } read:
                yield return new FormatUse(
                    getter, ReadonlyInference.Declared(getter, property.Modifiers), property.DeclaringType, read, Inherited(enclosing, property.DeclaringType));
                break;
        }
    }

    public IEnumerable<FormatStep> Reach(IMistType formatted)
    {
        return Reach(formatted, new HashSet<object>(ReferenceEqualityComparer.Instance));
    }

    public IEnumerable<FormatStep> Through(FormatUse use)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);

        return use.Declared.IsReadonly ? [] : Of(use.Callee, use.Scope).SelectMany(parameter => Reach(use.Closing.Apply(parameter, interner: null), visited));
    }

    public static bool InScope(TypeParameter parameter, object? member, MistType? scope)
    {
        if(member is Method method && (method.TypeParametersList.Contains(parameter) || method.DeclaringBlock?.TypeParametersList.Contains(parameter) == true))
        {
            return true;
        }

        for(var current = scope; current is not null; current = current.DeclaringType)
        {
            if(current is IHasTypeParameters generic && generic.TypeParametersList.Contains(parameter))
            {
                return true;
            }
        }

        return false;
    }

    public IReadOnlySet<TypeParameter> Of(object member, MistType? scope)
    {
        if(settled.TryGetValue(member, out var known))
        {
            return known;
        }

        if(settling.TryGetValue(member, out var partial))
        {
            return partial;
        }

        return IsGeneric(member, scope) ? Settle(member, scope) : None;
    }

    private IEnumerable<FormatStep> Reach(IMistType formatted, HashSet<object> visited)
    {
        return Leaves(formatted).SelectMany(leaf => ReachLeaf(leaf, visited));
    }

    private IEnumerable<FormatStep> ReachLeaf(IMistType formatted, HashSet<object> visited)
    {
        if(PurityJudge.DefinitionOf(formatted) is TypeParameter open)
        {
            yield return new FormatStep(open, formatted, null);
            yield break;
        }

        if(!ClrCallbacks.Implements(formatted, "System.IFormattable") || !visited.Add(formatted))
        {
            yield break;
        }

        var toString = ClrCallbacks.Answering(formatted, "ToString", 2);

        yield return new FormatStep(null, formatted, toString);

        if(toString is null || toString.Modifiers.IsReadonly)
        {
            yield break;
        }

        var closing = TypeSubstitution.ForReceiver(Constructed(formatted));

        foreach(var parameter in Of(toString, toString.DeclaringType))
        {
            foreach(var step in Reach(closing.Apply(parameter, interner: null), visited))
            {
                yield return step;
            }
        }
    }

    private IReadOnlySet<TypeParameter> Settle(object member, MistType? scope)
    {
        var edges = new Dictionary<object, Edges>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<(object Member, MistType? Scope)>();
        pending.Push((member, scope));

        while(pending.Count > 0)
        {
            var (next, nextScope) = pending.Pop();

            if(edges.ContainsKey(next) || settled.ContainsKey(next) || settling.ContainsKey(next) || !IsGeneric(next, nextScope))
            {
                continue;
            }

            var found = EdgesOf(next, nextScope);
            edges[next] = found;
            settling[next] = [.. found.Opens];

            foreach(var use in found.Uses)
            {
                pending.Push((use.Callee, use.Scope));
            }
        }

        for(var changed = true; changed;)
        {
            changed = false;

            foreach(var (next, found) in edges)
            {
                foreach(var use in found.Uses)
                {
                    foreach(var parameter in Of(use.Callee, use.Scope).ToList())
                    {
                        foreach(var step in Reach(use.Closing.Apply(parameter, interner: null)))
                        {
                            changed |= step.Open is { } open && InScope(open, next, found.Scope) && settling[next].Add(open);
                        }
                    }
                }
            }
        }

        foreach(var next in edges.Keys)
        {
            settled[next] = settling[next];
            _ = settling.Remove(next);
        }

        return settled[member];
    }

    private static Edges EdgesOf(object member, MistType? scope)
    {
        var opens = new HashSet<TypeParameter>();
        var uses = new List<FormatUse>();

        foreach(var node in ReadonlyInference.Roots(member).SelectMany(BodyWalker.DescendantsAndSelf))
        {
            foreach(var formatted in Sites(node))
            {
                foreach(var leaf in Leaves(formatted.Type))
                {
                    if(PurityJudge.DefinitionOf(leaf) is TypeParameter open)
                    {
                        if(InScope(open, member, scope))
                        {
                            _ = opens.Add(open);
                        }
                    }
                    else if(ClrCallbacks.Implements(leaf, "System.IFormattable")
                        && ClrCallbacks.Answering(leaf, "ToString", 2) is { Modifiers.IsReadonly: false } toString)
                    {
                        uses.Add(new FormatUse(toString, toString.Modifiers, toString.DeclaringType, formatted, TypeSubstitution.ForReceiver(Constructed(leaf))));
                    }
                }
            }

            uses.AddRange(Uses(node, scope).Where(use => !use.Declared.IsReadonly));
        }

        return new Edges(opens, uses, scope);
    }

    private static IEnumerable<IMistType> Leaves(IMistType formatted)
    {
        return formatted switch
        {
            TypeAlias { AliasedType: { } aliased } => Leaves(aliased),
            RefinedType refined => Leaves(refined.BaseType),
            ITupleType tuple => tuple.ElementTypes.SelectMany(Leaves),
            _ when OptionalUnion.InnerType(formatted) is { } inner => Leaves(inner),
            _ => [formatted],
        };
    }

    private static bool IsGeneric(object member, MistType? scope)
    {
        if(member is Method { TypeParametersList.Count: > 0 } or Method { DeclaringBlock.TypeParametersList.Count: > 0 })
        {
            return true;
        }

        for(var current = scope; current is not null; current = current.DeclaringType)
        {
            if(current is IHasTypeParameters { TypeParametersList.Count: > 0 })
            {
                return true;
            }
        }

        return false;
    }

    private static TypeSubstitution Inherited(IMistType? receiver, MemberType? declaring)
    {
        if(receiver is null)
        {
            return TypeSubstitution.Empty;
        }

        var path = new List<ConstructedType>();
        var visited = new HashSet<ConcreteType>(ReferenceEqualityComparer.Instance);

        for(var current = PurityJudge.DefinitionOf(receiver) as ConcreteType;
            current is not null && !ReferenceEquals(current, declaring) && visited.Add(current) && current.BaseType is { } baseType;
            current = PurityJudge.DefinitionOf(baseType) as ConcreteType)
        {
            if(Constructed(baseType) is { } constructedBase)
            {
                path.Add(constructedBase);
            }
        }

        return TypeSubstitution.ForPath(Constructed(receiver), path, interner: null);
    }

    private static ConstructedType? Constructed(IMistType? type)
    {
        return type switch
        {
            ConstructedType constructed => constructed,
            TypeAlias { AliasedType: { } aliased } => Constructed(aliased),
            RefinedType refined => Constructed(refined.BaseType),
            _ => null,
        };
    }

    private sealed record Edges(HashSet<TypeParameter> Opens, List<FormatUse> Uses, MistType? Scope);
}