using Mist.ProgramAnalysis;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// Which type parameters a shared C# body cannot answer for from the C# type once a closing of a value-routed type
/// fills them, decided once over the whole program beside <see cref="SlotRoutes"/> (runtime-dependent-values design,
/// <em>A closing as a type argument</em>). Every closing of a value-routed type is one C# type, so a body tells closings
/// apart only through a description of its argument: a test against a type that mentions the parameter, storage of such
/// a type that starts from the CLR zero, static storage of the declaring type, a Mist generic whose closing a test
/// compares by value, a closing passed on to a parameter of another declaration that does one of these, or an abstract's
/// method slot whose answer does, to a fixed point; <see cref="ClosingWitnesses"/> passes that description. Three
/// reasons no description answers stay refused at the closing, until the owner rules: the parameter read as a type
/// value, a test that nests it in a CLR container, and a CLR generic it reaches, for a struct closing. A test is exempt
/// when the value's own type admits only the tested closing, the parameter itself or a union holding it, because the
/// C# test then decides the same question the slot would.
/// </summary>
internal sealed partial class SlotSensitivity
{
    public enum Kind
    {
        Test,
        Storage,
        Static,
        TypeValue,
        Forwarded,
        Answered,
        Nested,
        Contained,
        Clr,
    }

    public sealed record Reason(Kind Kind, TypeParameter Parameter, TypeParameter? Target = null, IMistType? Through = null);

    private readonly Dictionary<TypeParameter, List<Reason>> reasons = new(ReferenceEqualityComparer.Instance);

    private SlotSensitivity()
    {
    }

    public bool IsSensitive(TypeParameter parameter)
    {
        return reasons.ContainsKey(parameter);
    }

    public IReadOnlyList<Reason> ReasonsOf(TypeParameter parameter)
    {
        return reasons.GetValueOrDefault(parameter) ?? [];
    }

    public IReadOnlyCollection<TypeParameter> Sensitive => reasons.Keys;

    public static bool AdmitsOnly(IMistType? subject, IMistType tested)
    {
        if(subject is null)
        {
            return false;
        }

        var target = Unaliased(tested);
        var source = Unaliased(subject);

        return ReferenceEquals(source, target)
            || (source is UnionType union && union.Constituents.Any(constituent => ReferenceEquals(Unaliased(constituent), target)));
    }

    public static SlotSensitivity Discover(ProgramModelClass model)
    {
        var roots = SourceBodies.Of(model).Select(root => root.Root).ToList();

        return Discover(roots, SourceDeclared(model), Closings(model, roots), SlotRoutes.Answers(model, slot => slot.TypeParametersList.Count > 0));
    }

    public static SlotSensitivity Discover(
        IEnumerable<object> roots,
        IEnumerable<MemberType> types,
        IEnumerable<(IHasTypeParameters Definition, IReadOnlyList<IMistType> Arguments)> closings,
        IEnumerable<(Method Slot, Method Answer)>? answers = null)
    {
        var found = new SlotSensitivity();

        foreach(var root in roots)
        {
            foreach(var node in BodyWalker.DescendantsAndSelf(root))
            {
                found.Mark(node);
            }
        }

        foreach(var type in types)
        {
            if(!HasStaticStorage(type))
            {
                continue;
            }

            for(var owner = type; owner is not null; owner = owner.DeclaringType as MemberType)
            {
                foreach(var parameter in owner.TypeParametersList)
                {
                    _ = found.Add(new Reason(Kind.Static, parameter));
                }
            }
        }

        var fills = closings.ToList();

        foreach(var (definition, arguments) in fills.Where(fill => IsClrGeneric(fill.Definition)))
        {
            foreach(var argument in arguments)
            {
                foreach(var parameter in Parameters(argument))
                {
                    _ = found.Add(new Reason(Kind.Clr, parameter, Through: argument));
                }
            }
        }

        var answered = answers?.ToList() ?? [];
        var changed = true;

        while(changed)
        {
            changed = false;

            foreach(var (definition, arguments) in fills)
            {
                for(var i = 0; i < arguments.Count && i < definition.TypeParametersList.Count; i++)
                {
                    var filled = definition.TypeParametersList[i];

                    if(!found.IsSensitive(filled))
                    {
                        continue;
                    }

                    if(found.ReasonsOf(filled).Any(reason => reason.Kind is Kind.Test or Kind.Nested))
                    {
                        changed |= found.MarkNested(arguments[i], new HashSet<IMistType>(ReferenceEqualityComparer.Instance));
                    }

                    foreach(var mentioned in Parameters(arguments[i]))
                    {
                        if(!ReferenceEquals(mentioned, filled))
                        {
                            changed |= found.Add(new Reason(Kind.Forwarded, mentioned, filled, arguments[i]));
                        }
                    }
                }
            }

            foreach(var (slot, answer) in answered)
            {
                for(var i = 0; i < slot.TypeParametersList.Count && i < answer.TypeParametersList.Count; i++)
                {
                    if(found.IsSensitive(answer.TypeParametersList[i]))
                    {
                        changed |= found.Add(new Reason(Kind.Answered, slot.TypeParametersList[i], answer.TypeParametersList[i]));
                    }
                }
            }
        }

        return found;
    }

    public static bool IsClrGeneric(IHasTypeParameters definition)
    {
        return definition switch
        {
            Method method => (method.TypeParametersList.Count > 0 || method.DeclaringBlock?.TypeParametersList.Count > 0)
                && (method.Modifiers.IsIntrinsic || ClrBodied(method.DeclaringType) || method.DeclaringBlock is { DeclarationNode: null }),
            ClassType or StructType => definition is MemberType { TypeParametersList.Count: > 0 } type
                && ClrBodied(type)
                && !LengthErasure.IsLengthIndexed(new ConstructedType(type, type.TypeParametersList)),
            _ => false,
        };
    }

    private static bool ClrBodied(IMistType? type)
    {
        return type is MemberType { Assembly.Kind: MistAssemblyKind.Ambient } or ConcreteType { IsIntrinsic: true, CloneSource: null };
    }

    private bool Add(Reason reason)
    {
        if(!reasons.TryGetValue(reason.Parameter, out var found))
        {
            found = [];
            reasons[reason.Parameter] = found;
        }

        if(found.Exists(existing => existing.Kind == reason.Kind && ReferenceEquals(existing.Target, reason.Target) && ReferenceEquals(existing.Through, reason.Through)))
        {
            return false;
        }

        found.Add(reason);
        return true;
    }

    private void Mark(object node)
    {
        switch(node)
        {
            case IsPatternExpression test:
                MarkPattern(test.Pattern, test.Scrutinee.Type);
                break;

            case SwitchExpression switched:
                foreach(var arm in switched.Arms)
                {
                    MarkPattern(arm.Pattern, switched.Scrutinee.Type);
                }

                break;

            case AsExpression conversion:
                MarkTest(conversion.Operand.Type, conversion.TargetType);
                break;

            case ArrayCreationExpression array:
                MarkEach(Kind.Storage, array.ElementType);
                break;

            case StackallocExpression allocation:
                MarkEach(Kind.Storage, allocation.ElementType);
                break;

            case TypeLiteralExpression literal:
                MarkEach(Kind.TypeValue, literal.ReferencedType);
                break;
        }
    }

    private void MarkPattern(Pattern pattern, IMistType? subject)
    {
        switch(pattern)
        {
            case TypeTestPattern typeTest:
                MarkTest(subject, typeTest.NarrowedType);
                break;

            case PropertyPattern property:
                MarkTest(subject, property.TypeTest?.NarrowedType ?? property.NarrowedType);

                foreach(var subpattern in property.Subpatterns)
                {
                    MarkPattern(subpattern.Pattern, subpattern.MemberType);
                }

                break;

            case NegatedPattern negated:
                MarkPattern(negated.Operand, subject);
                break;

            case ConjunctionPattern conjunction:
                foreach(var operand in conjunction.Operands)
                {
                    MarkPattern(operand, subject);
                }

                break;

            case DisjunctionPattern disjunction:
                foreach(var operand in disjunction.Operands)
                {
                    MarkPattern(operand, subject);
                }

                break;

            case TuplePattern tuple:
                if(tuple.TestedType is { } tested)
                {
                    MarkTest(subject, tested);
                }

                foreach(var element in tuple.Elements)
                {
                    MarkPattern(element, subject: null);
                }

                break;

            case ListPattern list:
                foreach(var element in list.Elements)
                {
                    if(element.Subpattern is { } subpattern)
                    {
                        MarkPattern(subpattern, subject: null);
                    }
                }

                break;
        }
    }

    private void MarkTest(IMistType? subject, IMistType tested)
    {
        if(!AdmitsOnly(subject, tested))
        {
            MarkTested(tested, contained: false, new HashSet<IMistType>(ReferenceEqualityComparer.Instance));
        }
    }

    private void MarkTested(IMistType type, bool contained, HashSet<IMistType> seen)
    {
        if(!seen.Add(type))
        {
            return;
        }

        switch(type)
        {
            case TypeParameter parameter:
                var filled = parameter is PackElementType element ? element.Pack : parameter;

                if(!filled.IsDependent)
                {
                    _ = Add(new Reason(contained ? Kind.Contained : Kind.Test, filled));
                }

                break;

            case ConstructedType constructed:
                var container = contained || IsContainer(constructed);

                for(var i = 0; i < constructed.TypeArguments.Count; i++)
                {
                    if(!container && constructed.Definition is IHasTypeParameters generic && i < generic.TypeParametersList.Count
                        && !generic.TypeParametersList[i].IsDependent)
                    {
                        _ = Add(new Reason(Kind.Nested, generic.TypeParametersList[i]));
                    }

                    MarkTested(constructed.TypeArguments[i], container, seen);
                }

                if(constructed.Enclosing is { } enclosing)
                {
                    MarkTested(enclosing, contained, seen);
                }

                break;

            case FunctionType function:
                MarkTested(function.ReturnType, contained: true, seen);

                foreach(var parameterType in function.ParameterTypes)
                {
                    MarkTested(parameterType, contained: true, seen);
                }

                break;

            default:
                foreach(var part in Parts(type))
                {
                    MarkTested(part, contained, seen);
                }

                break;
        }
    }

    private bool MarkNested(IMistType type, HashSet<IMistType> seen)
    {
        if(!seen.Add(type))
        {
            return false;
        }

        var changed = false;

        if(type is ConstructedType constructed)
        {
            if(IsContainer(constructed))
            {
                return false;
            }

            for(var i = 0; i < constructed.TypeArguments.Count; i++)
            {
                if(constructed.Definition is IHasTypeParameters generic && i < generic.TypeParametersList.Count && !generic.TypeParametersList[i].IsDependent)
                {
                    changed |= Add(new Reason(Kind.Nested, generic.TypeParametersList[i]));
                }

                changed |= MarkNested(constructed.TypeArguments[i], seen);
            }

            return changed;
        }

        foreach(var part in Parts(type))
        {
            changed |= MarkNested(part, seen);
        }

        return changed;
    }

    private void MarkEach(Kind kind, IMistType type)
    {
        foreach(var parameter in Parameters(type))
        {
            _ = Add(new Reason(kind, parameter));
        }
    }

    internal static IEnumerable<TypeParameter> Parameters(IMistType type)
    {
        foreach(var parameter in TypeParameterReach.Collect(type))
        {
            var filled = parameter is PackElementType element ? element.Pack : parameter;

            if(!filled.IsDependent)
            {
                yield return filled;
            }
        }
    }

    internal static bool IsContainer(ConstructedType constructed)
    {
        return LengthErasure.IsLengthIndexed(constructed)
            || ClrBodied(constructed.Definition)
            || ClrProjection.Projects(constructed.Definition);
    }

    private static bool HasStaticStorage(MemberType type)
    {
        if(type is ConcreteType concrete && concrete.FieldsList.Exists(field => field.Modifiers is { IsStatic: true, IsIntrinsic: false }))
        {
            return true;
        }

        return type.PropertiesList.Exists(property => property.Modifiers is { IsStatic: true, IsIntrinsic: false }
            && (property.UsesBackingField || property.Initializer is not null || property.Getter is { IsAuto: true } || property.Setter is { IsAuto: true }));
    }

    private static IMistType Unaliased(IMistType type)
    {
        return type is TypeAlias { AliasedType: { } aliased } ? Unaliased(aliased) : type;
    }

    private static IEnumerable<IMistType> Parts(IMistType type)
    {
        return type switch
        {
            TypeAlias { AliasedType: { } aliased } => [aliased],
            RefinedType refined => [refined.BaseType],
            UnionType union => union.Constituents,
            NamedTupleType named => named.ElementTypes,
            TupleType tuple => tuple.ElementTypes,
            LiftedType { Value: TypePackValue pack } lifted => pack.Elements.Prepend(lifted.Type),
            LiftedType lifted => [lifted.Type],
            SpreadType spread => [spread.Operand],
            _ => [],
        };
    }
}