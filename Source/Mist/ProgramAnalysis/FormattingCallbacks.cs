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
/// <c>ToString</c>, a <see cref="FormatStep"/> naming it as its <see cref="FormatStep.Formatter"/>; one whose type is a type parameter is an
/// <see cref="FormatStep.Open"/> step, since what it calls is decided where the parameter is closed, and so
/// is a call into a generic member that formats one of its own parameters, a <see cref="FormatUse"/> whose
/// closing says what that parameter is there. <see cref="Of"/> answers which type parameters a member
/// formats, a least fixed point over the members it calls.
/// <para>
/// A callee or formatter that DECLARES readonly is not followed. Its promise is judged where its formatted
/// parameters are closed, so following it as well would report one mistake twice. Only Mist types are
/// judged, by their static type, exactly as for a concrete hole; a value typed by an abstract type, or
/// reached through an inherited generic method or a generic extension block, is not resolved here.
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

    public static IEnumerable<FormatUse> Uses(object node)
    {
        switch(node)
        {
            case InvocationExpression { Method: { } method } call:
                var declared = method.SourceDeclaration ?? method;
                var (receiver, typeArguments) = call.Receiver switch
                {
                    MemberAccessExpression access => (access.Receiver.Type, access.TypeArguments),
                    MethodReferenceExpression reference => (null, reference.TypeArguments),
                    _ => (null, []),
                };
                var closing = TypeSubstitution.ForReceiver(Constructed(receiver))
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

            case PropertyReferenceExpression { Property: { Getter: { } getter } property } read:
                yield return new FormatUse(getter, ReadonlyInference.Declared(getter, property.Modifiers), property.DeclaringType, read, TypeSubstitution.Empty);
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

            var found = EdgesOf(next);
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
                            changed |= step.Open is { } open && settling[next].Add(open);
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

    private static Edges EdgesOf(object member)
    {
        var opens = new HashSet<TypeParameter>();
        var uses = new List<FormatUse>();

        foreach(var node in ReadonlyInference.Roots(member).SelectMany(BodyWalker.DescendantsAndSelf))
        {
            foreach(var formatted in Sites(node))
            {
                if(PurityJudge.DefinitionOf(formatted.Type) is TypeParameter open)
                {
                    _ = opens.Add(open);
                }
                else if(ClrCallbacks.Implements(formatted.Type, "System.IFormattable")
                    && ClrCallbacks.Answering(formatted.Type, "ToString", 2) is { Modifiers.IsReadonly: false } toString)
                {
                    uses.Add(new FormatUse(toString, toString.Modifiers, toString.DeclaringType, formatted, TypeSubstitution.ForReceiver(Constructed(formatted.Type))));
                }
            }

            uses.AddRange(Uses(node).Where(use => !use.Declared.IsReadonly));
        }

        return new Edges(opens, uses);
    }

    private static bool IsGeneric(object member, MistType? scope)
    {
        if(member is Method { TypeParametersList.Count: > 0 })
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

    private sealed record Edges(HashSet<TypeParameter> Opens, List<FormatUse> Uses);
}