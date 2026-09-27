using System.Runtime.CompilerServices;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis;

/// <summary>
/// A type's static initializers, as the one member the inference keys them by: its static field and
/// property initializers and its static constructor's body, or an enum's variant values, which the
/// target builds as static fields. They run as a unit the first time any static field of the type is
/// read -- and, once the type declares a static constructor, the first time anything of it is used at
/// all, which is what <see cref="IsPrecise"/> tells a caller.
/// </summary>
internal sealed class TypeInitializer
{
    private static readonly ConditionalWeakTable<MistType, TypeInitializer> Keys = [];

    private TypeInitializer(MistType type)
    {
        Type = type;
    }

    public MistType Type { get; }

    public bool IsPrecise => Type is ConcreteType concrete && concrete.ConstructorsList.Exists(constructor => constructor.Modifiers.IsStatic);

    public static TypeInitializer Of(MistType type)
    {
        return Keys.GetValue(type, declared => new TypeInitializer(declared));
    }

    public static IEnumerable<Trigger> TriggeredBy(object node)
    {
        switch(node)
        {
            case FieldReferenceExpression { Field: { Modifiers.IsStatic: true, DeclaringType: { } declaring } }:
                yield return new Trigger(declaring, Precise: false, Named: null);
                break;

            case MemberAccessExpression { Member: FieldResult { Field: { Modifiers.IsStatic: true, DeclaringType: { } declaring } } } access:
                yield return new Trigger(declaring, Precise: false, Named(access.Receiver));
                break;

            case MemberAccessExpression { Member: PropertyResult { Property: { Modifiers.IsStatic: true, DeclaringType: { } declaring } property } } access:
                yield return new Trigger(declaring, !property.HasBackingStorage, Named(access.Receiver));
                break;

            case PropertyReferenceExpression { Property: { Modifiers.IsStatic: true, DeclaringType: { } declaring } property }:
                yield return new Trigger(declaring, !property.HasBackingStorage, Named: null);
                break;

            case MemberAccessExpression { Member: EnumMemberResult found }:
                if(PurityJudge.DefinitionOf(found.DeclaringType) is MistType variant)
                {
                    yield return new Trigger(variant, Precise: false, Named: null);
                }

                break;

            case InvocationExpression { Method: { Modifiers.IsStatic: true, DeclaringType: { } declaring } } call:
                yield return new Trigger(declaring, Precise: true, Named(ThisTiers.ReceiverOf(call)));
                break;

            case NewExpression allocation when allocation.Constructor is not null || PurityJudge.ImplicitConstructorOf(allocation) is not null:
                foreach(var built in Chain(allocation.Type))
                {
                    yield return new Trigger(built, Precise: true, allocation.Type);
                }

                break;

            case DefaultExpression fallback when PurityJudge.DefinitionOf(fallback.Type) is ConcreteType { IsIntrinsic: false }:
                foreach(var built in Chain(fallback.Type))
                {
                    yield return new Trigger(built, Precise: true, fallback.Type);
                }

                break;

            case CastExpression { LookupParameterType: not null, TargetType: EnumType target }:
                yield return new Trigger(target, Precise: false, Named: null);
                break;

            case Expression when DeclaredOperator(node) is { DeclaringType: { } declaring }:
                yield return new Trigger(declaring, Precise: true, Named: null);
                break;
        }
    }

    private static Method? DeclaredOperator(object node)
    {
        return node switch
        {
            BinaryExpression binary => binary.Operator,
            UnaryExpression unary => unary.Operator,
            CastExpression cast => cast.Operator,
            ImplicitConversionExpression conversion => conversion.Operator,
            _ => null,
        };
    }

    public bool NotRunBy(Trigger trigger, MistType? scope)
    {
        return trigger.Precise && (!IsPrecise || (ReferenceEquals(Type, scope) && IsOwnInstantiation(trigger.Named)));
    }

    private static IMistType? Named(Expression? receiver)
    {
        return receiver is TypeReferenceExpression reference ? reference.ReferencedType : null;
    }

    private static bool IsOwnInstantiation(IMistType? named)
    {
        return named is not ConstructedType { Definition: MemberType definition } constructed
            || constructed.TypeArguments.Select((argument, i) => ReferenceEquals(argument, definition.TypeParametersList.ElementAtOrDefault(i))).All(same => same);
    }

    private static IEnumerable<MistType> Chain(IMistType type)
    {
        for(var built = PurityJudge.DefinitionOf(type) as ConcreteType; built is not null; built = built.BaseType is { } baseType ? PurityJudge.DefinitionOf(baseType) as ConcreteType : null)
        {
            yield return built;
        }
    }

    public IEnumerable<object> Initializers()
    {
        if(Type is EnumType enumType)
        {
            foreach(var variant in enumType.MembersList)
            {
                if(variant.Value is { } value)
                {
                    yield return value;
                }
            }

            yield break;
        }

        if(Type is not ConcreteType concrete)
        {
            yield break;
        }

        foreach(var field in concrete.FieldsList)
        {
            if(field is { Modifiers.IsStatic: true, Initializer: { } initializer })
            {
                yield return initializer;
            }
        }

        foreach(var property in concrete.PropertiesList)
        {
            if(property is { Initializer: { } initializer, Modifiers.IsStatic: true })
            {
                yield return initializer;
            }
        }

        foreach(var constructor in concrete.ConstructorsList)
        {
            if(constructor is { Modifiers.IsStatic: true, Body: { } body })
            {
                yield return body;
            }
        }
    }
}

internal readonly record struct Trigger(MistType Type, bool Precise, IMistType? Named);