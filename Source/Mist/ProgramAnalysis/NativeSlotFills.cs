using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis;

/// <summary>
/// Which members of a source type fill a slot of a projected class it derives from, and which collide
/// with one instead (clr-slot-filling Requirement 2). A source member is judged against the NEAREST
/// projected declaration repeating its name and signature through the inheritance substitution, so a
/// CLR class overriding its base's abstract member presents a defaulted slot, and a Mist ancestor's
/// own fill is no projected declaration at all: a derived type restating it fills the same slot. A
/// chain with a base that did not resolve answers nothing, that base having been reported where it
/// was written. Stage 5 and the emitter both ask here; neither decides a fill itself.
/// </summary>
internal static class NativeSlotFills
{
    [ThreadStatic]
    private static HashSet<object>? fillersInScope;

    internal sealed record Fill(SlotMember Filler, SlotMember Slot, ConcreteType Ancestor, TypeSubstitution Inheritance);

    internal sealed record Collision(
        SlotMember Member, SlotMember Against, ConcreteType Ancestor, TypeSubstitution Inheritance, NativeSlotKind Kind,
        string? Mismatch, string? Declared);

    public static IDisposable Enter(ProgramModel.Model.ProgramModel programModel)
    {
        return Enter(SourceTypes.Of(programModel).OfType<ConcreteType>(), programModel.TypeInterner);
    }

    public static IDisposable Enter(IEnumerable<ConcreteType> types, TypeInterner interner)
    {
        fillersInScope = types.SelectMany(type => Of(type, interner)).Select(fill => fill.Filler.Member)
            .ToHashSet(ReferenceEqualityComparer.Instance);
        return new Scope();
    }

    public static bool Fills(object member)
    {
        return fillersInScope is { } fillers && fillers.Contains(member);
    }

    public static IEnumerable<Fill> Of(ConcreteType type, TypeInterner interner)
    {
        return Judge(type, interner).OfType<Fill>();
    }

    public static IEnumerable<Collision> Collisions(ConcreteType type, TypeInterner interner)
    {
        return Judge(type, interner).OfType<Collision>();
    }

    public static IEnumerable<(SlotMember Slot, ConcreteType Ancestor, TypeSubstitution Inheritance)> Unfilled(
        ConcreteType type, TypeInterner interner)
    {
        if(Chain(type, interner) is not { } chain)
        {
            yield break;
        }

        var filled = chain.Where(link => !IsProjected(link.Type)).Select(link => link.Type).Prepend(type)
            .SelectMany(each => Of(each, interner))
            .Select(fill => fill.Slot)
            .ToHashSet();
        var nearer = new List<(SlotMember Member, TypeSubstitution Inheritance)>();

        foreach(var (ancestor, inheritance) in chain.Where(link => IsProjected(link.Type)))
        {
            foreach(var slot in SlotMember.Of(ancestor).Where(member => !member.Modifiers.IsStatic))
            {
                var overridden = nearer.Any(earlier => Repeats(earlier.Member, earlier.Inheritance, slot, inheritance, interner));
                nearer.Add((slot, inheritance));

                if(!overridden && slot.SlotKind == NativeSlotKind.Required && !filled.Contains(slot))
                {
                    yield return (slot, ancestor, inheritance);
                }
            }
        }
    }

    private static IEnumerable<object> Judge(ConcreteType type, TypeInterner interner)
    {
        if(Chain(type, interner) is not { } chain || !chain.Any(link => IsProjected(link.Type)))
        {
            yield break;
        }

        foreach(var member in SlotMember.Of(type).Where(member => member.DeclarationNode is not null))
        {
            if(Nearest(member, chain, interner) is not var (slot, ancestor, inheritance))
            {
                continue;
            }

            if(slot.SlotKind is NativeSlotKind.None or NativeSlotKind.Sealed)
            {
                yield return new Collision(member, slot, ancestor, inheritance, slot.SlotKind, null, null);
            }
            else if(Mismatch(member, slot, inheritance, interner) is var (mismatch, declared))
            {
                yield return new Collision(member, slot, ancestor, inheritance, slot.SlotKind, mismatch, declared);
            }
            else
            {
                yield return new Fill(member, slot, ancestor, inheritance);
            }
        }
    }

    private static (SlotMember Slot, ConcreteType Ancestor, TypeSubstitution Inheritance)? Nearest(
        SlotMember member, List<(ConcreteType Type, TypeSubstitution Inheritance)> chain, TypeInterner interner)
    {
        foreach(var (ancestor, inheritance) in chain.Where(link => IsProjected(link.Type)))
        {
            foreach(var candidate in SlotMember.Of(ancestor))
            {
                var native = candidate.SlotKind is NativeSlotKind.Required or NativeSlotKind.Defaulted;

                if(!candidate.Modifiers.IsStatic && (!member.Modifiers.IsStatic || native)
                    && candidate.Name == member.Name && candidate.Member.GetType() == member.Member.GetType()
                    && (candidate.Overload is not { } earlier || member.Overload is not { } later
                        || OverloadSignature.RepeatsProjected(later, earlier, inheritance, interner, slot: true)))
                {
                    return (candidate, ancestor, inheritance);
                }
            }
        }

        return null;
    }

    private static bool Repeats(
        SlotMember earlier, TypeSubstitution earlierInheritance, SlotMember later, TypeSubstitution laterInheritance,
        TypeInterner interner)
    {
        if(earlier.Name != later.Name || earlier.Member.GetType() != later.Member.GetType())
        {
            return false;
        }

        return earlier.Overload is not { } slot || later.Overload is not { } member
            || (OverloadSignature.Of(member, laterInheritance, interner) is { } signature
                && signature == OverloadSignature.Of(slot, earlierInheritance, interner));
    }

    private static (string Mismatch, string Declared)? Mismatch(
        SlotMember member, SlotMember slot, TypeSubstitution inheritance, TypeInterner interner)
    {
        if(member.Modifiers.IsStatic)
        {
            return ("static", "an instance member");
        }

        var slotType = member.Member is Method filler && slot.Member is Method declared
            ? SlotSignatures.Closing(declared, filler, inheritance).Apply(declared.ReturnType, interner)
            : inheritance.Apply(slot.Type, interner);

        if(!SlotSignatures.SameReturnPassing(slot.Modifiers, member.Modifiers) || !ReturnAgrees(slot, slotType, member.Type, interner))
        {
            return ("return type", $"'{TypeDisplay.Mist(slotType)}'");
        }

        if(member.Modifiers.Accessibility != slot.Modifiers.Accessibility)
        {
            return ("accessibility", slot.Modifiers.Accessibility.ToString().ToLowerInvariant());
        }

        if(member.Getter is not null && slot.Getter is null)
        {
            return ("get accessor", "no get accessor");
        }

        if(member.Setter is not null && slot.Setter is null)
        {
            return ("set accessor", "no set accessor");
        }

        if(member.Member is Property property && IsAuto(property))
        {
            if(member.Getter is null && slot.Getter is not null)
            {
                return ("get accessor", "a get accessor, which an auto-property filling it must declare too");
            }

            if(member.Setter is null && slot.Setter is not null)
            {
                return ("set accessor", "a set accessor, which an auto-property filling it must declare too");
            }
        }

        if(member.Member is Method generic && slot.Member is Method promised && member.DeclarationNode is { } at
            && SlotSignatures.Strengthened(promised, generic, inheritance, TypeSubstitution.Empty, at, interner).FirstOrDefault()
                is ({ } parameter, { } constraint))
        {
            var spelled = constraint is TypeConstraint typed ? TypeDisplay.Mist(typed.ConstraintType) : ConformanceAnalyzer.Spelled(constraint);
            return ($"the constraints of '{parameter.Name}'", $"no '{spelled}' constraint");
        }

        return null;
    }

    private static bool IsAuto(Property property)
    {
        return property.Getter is null or { Body: null, ExpressionBody: null }
            && property.Setter is null or { Body: null, ExpressionBody: null };
    }

    private static bool ReturnAgrees(SlotMember slot, IMistType slotType, IMistType fillerType, TypeInterner interner)
    {
        return SlotSignatures.Agree(slotType, fillerType)
            || (slot.Modifiers is { IsClrProjected: true, ClrResultIsNonNull: false }
                && SlotSignatures.Agree(OptionalLifting.NonVoidPortion(slotType, interner), fillerType));
    }

    private static List<(ConcreteType Type, TypeSubstitution Inheritance)>? Chain(ConcreteType type, TypeInterner interner)
    {
        var chain = new List<(ConcreteType, TypeSubstitution)>();
        var path = new List<ConstructedType>();
        var seen = new HashSet<ConcreteType> { type };

        for(var current = type; !current.BaseUnresolved; current = chain[^1].Item1)
        {
            if(Inherits(current.BaseType) is not { } ancestor || !seen.Add(ancestor))
            {
                return chain;
            }

            if(current.BaseType is ConstructedType constructed)
            {
                path.Add(constructed);
            }

            chain.Add((ancestor, TypeSubstitution.ForPath(null, path, interner)));
        }

        return null;
    }

    private static ConcreteType? Inherits(MistType? baseType)
    {
        return baseType switch
        {
            ConstructedType { Definition: ConcreteType constructed } => constructed,
            ConcreteType concrete => concrete,
            _ => null,
        };
    }

    private static bool IsProjected(ConcreteType type)
    {
        return type.Assembly.Kind == MistAssemblyKind.Ambient;
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose()
        {
            fillersInScope = null;
        }
    }
}

internal readonly record struct SlotMember(object Member)
{
    public string Name => Member switch
    {
        Method method => method.Name,
        Property property => property.Name,
        _ => "this[]",
    };

    public MemberModifiers Modifiers => Member switch
    {
        Method method => method.Modifiers,
        Property property => property.Modifiers,
        _ => ((Indexer)Member).Modifiers,
    };

    public NativeSlotKind SlotKind => Member switch
    {
        Method method => method.SlotKind,
        Property property => property.SlotKind,
        _ => ((Indexer)Member).SlotKind,
    };

    public Storm.Runtime.SyntaxNode? DeclarationNode => Member switch
    {
        Method method => method.DeclarationNode,
        Property property => property.DeclarationNode,
        _ => ((Indexer)Member).DeclarationNode,
    };

    public IMistType Type => Member switch
    {
        Method method => method.ReturnType,
        Property property => property.Type,
        _ => ((Indexer)Member).Type,
    };

    public PropertyAccessor? Getter => Member switch
    {
        Property property => property.Getter,
        Indexer indexer => indexer.Getter,
        _ => null,
    };

    public PropertyAccessor? Setter => Member switch
    {
        Property property => property.Setter,
        Indexer indexer => indexer.Setter,
        _ => null,
    };

    public IOverload? Overload => Member as IOverload;

    public static IEnumerable<SlotMember> Of(MemberType type)
    {
        return type.MethodsList.Select(method => new SlotMember(method))
            .Concat(type.PropertiesList.Select(property => new SlotMember(property)))
            .Concat(type.IndexersList.Select(indexer => new SlotMember(indexer)));
    }

    public string Display(TypeSubstitution inheritance, TypeInterner interner)
    {
        return Member switch
        {
            Method method => SlotSignatures.Display(method, inheritance, interner),
            Property property => $"{TypeDisplay.Mist(inheritance.Apply(property.Type, interner))} {property.Name}",
            _ => SlotSignatures.Display((Indexer)Member, inheritance, interner),
        };
    }
}