using Mist.ProgramAnalysis;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// The derived <c>Clone</c>s this program reaches, which are the only ones written: a type no clone, call, conversion
/// or <c>IClonable</c> requirement reaches keeps the C# it had before derivation. Reaching one class reaches its
/// deriving subclasses, which a copy of the class may turn out to be, and its deriving bases, whose <c>Clone</c> its
/// own overrides; reaching a type reaches whatever its copy clones in turn. An unsubstituted slot
/// retains every derivable generic argument type: inferred and forwarded arguments need not have a
/// written closing, and C# excludes ref structs from those arguments.
/// </summary>
internal sealed class CloneDemand
{
    [ThreadStatic]
    private static CloneDemand? active;

    private readonly HashSet<ConcreteType> reached = new(ReferenceEqualityComparer.Instance);

    private readonly HashSet<ConcreteType> copying = new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<ConcreteType, List<ConcreteType>> subclasses = new(ReferenceEqualityComparer.Instance);

    private readonly List<ConcreteType> derivable;

    private readonly Queue<ConcreteType> pending = new();

    private readonly CloneDerivation clones;

    private readonly AbstractType? clonable;

    private bool copiesGenericSlots;

    private CloneDemand(ProgramModelClass model)
    {
        clones = model.Clones;
        clonable = model.TypesByFQN.GetValueOrDefault(WellKnownTypes.IClonable.FullyQualifiedName) as AbstractType;
        var types = SourceTypes.Of(model).OfType<ConcreteType>().ToList();
        derivable = types.Where(type => type.DerivedClone is not null && type is not StructType { IsRef: true }).ToList();

        foreach(var type in types)
        {
            if(Definition(type.BaseType) is ConcreteType baseType)
            {
                if(!subclasses.TryGetValue(baseType, out var known))
                {
                    subclasses[baseType] = known = [];
                }

                known.Add(type);
            }

            if(type is { DerivedClone: not null, DerivedCloneConformance: null }
                && type.BaseAbstractTypes.Exists(declared => declared is ConstructedType constructed && ReferenceEquals(constructed.Definition, clonable)))
            {
                pending.Enqueue(type);
            }
        }

        foreach(var closing in model.ConstructorConstraintClosures.Closings)
        {
            if(clonable?.TypeParametersList.Contains(closing.Parameter) == true || closing.Parameter.ConstraintsList.Exists(constraint => constraint is TypeConstraint { ConstraintType: var bound } && Mentions(bound)))
            {
                Need(closing.Argument);
            }
        }

        foreach(var root in SourceBodies.Of(model))
        {
            foreach(var node in BodyWalker.DescendantsAndSelf(root.Root))
            {
                switch(node)
                {
                    case CloneExpression clone:
                        Need(clone.Source.Type);
                        break;

                    case InvocationExpression { Method: { } called } when(called.SourceDeclaration ?? called).IsDerivedClone:
                        Need((called.SourceDeclaration ?? called).DeclaringType);
                        break;

                    case MethodGroupExpression group:
                        foreach(var candidate in group.Candidates.Where(candidate => candidate.IsDerivedClone))
                        {
                            Need(candidate.DeclaringType);
                        }

                        break;

                    case CastExpression { Operator: CastOperator { CopiesDerivedClone: true } conversion }:
                        Need(conversion.SourceParameter.Type);
                        Need(conversion.TargetType);
                        break;
                }
            }
        }

        while(pending.TryDequeue(out var type))
        {
            Reach(type);
        }
    }

    public static IDisposable Enter(ProgramModelClass model)
    {
        var enclosing = active;
        active = new CloneDemand(model);
        return new Scope(() => active = enclosing);
    }

    public static CloneDerivation? Clones => active?.clones;

    public static AbstractType? ClonableAbstract => active?.clonable;

    public static bool CopiesGenericSlots => active?.copiesGenericSlots == true;

    public static bool Emits(ConcreteType type)
    {
        return active?.reached.Contains(type) == true;
    }

    public static bool CopiesSlotsOf(ConcreteType type)
    {
        return active?.copying.Contains(type) == true;
    }

    public static bool Overrides(ConcreteType type)
    {
        return Definition(type.BaseType) is ConcreteType { DerivedClone: not null } baseType && Emits(baseType);
    }

    public static bool IsOverridden(ConcreteType type)
    {
        return active?.subclasses.GetValueOrDefault(type)?.Exists(Emits) == true;
    }

    private void Reach(ConcreteType type)
    {
        if(!reached.Add(type))
        {
            return;
        }

        for(var current = type; current is not null; current = Definition(current.BaseType) as ConcreteType)
        {
            if(current.DerivedClone is not null)
            {
                pending.Enqueue(current);
            }

            if(SlotsNeedingCopy(current, clones).Any())
            {
                _ = copying.Add(current);
            }
        }

        foreach(var subclass in subclasses.GetValueOrDefault(type) ?? [])
        {
            Need(subclass);
        }

        foreach(var slot in SlotsNeedingCopy(type, clones))
        {
            Need(slot.Type);
        }
    }

    private void Need(IMistType? type)
    {
        switch(type)
        {
            case null:
                return;

            case TypeAlias { AliasedType: var aliased }:
                Need(aliased);
                return;

            case RefinedType refined:
                Need(refined.BaseType);
                return;

            case Newtype { SourceType: var source }:
                Need(source);
                return;

            case UnionType union:
                foreach(var constituent in union.Constituents)
                {
                    Need(constituent);
                }

                return;

            case ITupleType tuple:
                foreach(var element in tuple.ElementTypes)
                {
                    Need(element);
                }

                return;

            case TypeParameter:
                if(copiesGenericSlots)
                {
                    return;
                }

                copiesGenericSlots = true;

                foreach(var candidate in derivable)
                {
                    pending.Enqueue(candidate);
                }

                return;
        }

        if(Definition(type) is ConcreteType concrete && !reached.Contains(concrete))
        {
            if(concrete.DerivedClone is not null)
            {
                pending.Enqueue(concrete);
            }
            else
            {
                foreach(var subclass in subclasses.GetValueOrDefault(concrete) ?? [])
                {
                    Need(subclass);
                }
            }
        }
    }

    public static IEnumerable<InstanceStorage> SlotsNeedingCopy(ConcreteType type, CloneDerivation clones)
    {
        return InstanceStorage.Of(type).Where(slot => clones.Judge(slot.Type).Kind != CloneKind.Unchangeable);
    }

    private static bool Mentions(IMistType type)
    {
        return type is ConstructedType constructed
            && (constructed.Definition.FullyQualifiedName == WellKnownTypes.IClonable.FullyQualifiedName || constructed.TypeArguments.Any(Mentions));
    }

    private static MistType? Definition(IMistType? type)
    {
        return (type is ConstructedType constructed ? constructed.Definition : type) as MistType;
    }

    private sealed class Scope(Action exit) : IDisposable
    {
        public void Dispose()
        {
            exit();
        }
    }
}