using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// Whether a concrete type fills every slot of every abstract it reaches, from Requirements 14 and 28
/// of the abstract-type-unification spec. Slots are the bodiless
/// members of the type's own base list, transitively through the abstract hierarchy; fillers are the
/// public members the type declares or inherits along its concrete base chain. A default fills its own
/// slot and NOTHING ELSE: one restated on a derived abstract hides the base's slot rather than filling
/// it, so the obligation stays with the conformer, which is what both pipelines measure and what C#
/// does with a hiding interface member. A with block answers the slots of the abstract it names by its
/// mappings or by the type's own members, and is redundant when the abstract is reached already. A slot
/// that is `readonly`, which every slot of a readonly abstract is, is answered only by a member that
/// writes nothing, by its modifier or by <see cref="PurityInference"/>: readonly-contexts Requirement
/// 13, which holds the member and not the conforming type, so a mutable type may meet it. A slot that
/// declares `local`, `local readonly` or `pure` is likewise answered only by a member that keeps that
/// promise (purity Requirement 8.4), a default included once a conformer's own member replaces it.
/// </summary>
internal sealed class ConformanceAnalyzer : IProgramAnalyzer
{
    public string Id => "abstract-conformance-checking";

    public void Analyze(AnalysisContext context)
    {
        var inference = new PurityInference(context);

        foreach(var type in context.Types)
        {
            if(type is not ConcreteType { DeclarationNode: { } node } concrete)
            {
                continue;
            }

            var fillers = Fillers(concrete).ToList();
            var reached = Reached(concrete.BaseAbstractTypes, context.Model.TypeInterner, concrete).ToList();
            var definitions = reached.ConvertAll(each => each.Abstract);

            foreach(var (abstractType, substitution) in reached)
            {
                CheckSlots(context, inference, concrete, node, abstractType, substitution, fillers, mappings: null, definitions);
                CheckTypeSlots(context, concrete, node, abstractType, substitution);
            }

            CheckConformances(context, inference, concrete, fillers);
        }
    }

    private static void CheckConformances(AnalysisContext context, PurityInference inference, ConcreteType concrete, List<Filler> fillers)
    {
        var reached = concrete.BaseAbstractTypes.Select(Name).ToHashSet(StringComparer.Ordinal);

        foreach(var conformance in concrete.ConformancesList)
        {
            if(!reached.Add(Name(conformance.AbstractType)))
            {
                context.Report(
                    ProgramAnalysisDiagnostics.RedundantConformance, conformance.DeclarationNode, [],
                    TypeDisplay.Mist(concrete), TypeDisplay.Mist(conformance.AbstractType));
                continue;
            }

            var conformed = Reached([conformance.AbstractType], context.Model.TypeInterner, concrete).ToList();
            var definitions = conformed.ConvertAll(each => each.Abstract);

            foreach(var (abstractType, substitution) in conformed)
            {
                CheckSlots(context, inference, concrete, conformance.DeclarationNode, abstractType, substitution, fillers, conformance, definitions);
                CheckTypeSlots(context, concrete, conformance.DeclarationNode, abstractType, substitution);
            }
        }
    }

    private static void CheckSlots(
        AnalysisContext context, PurityInference inference, ConcreteType concrete, SyntaxNode at, AbstractType abstractType,
        TypeSubstitution substitution, List<Filler> fillers, ConformanceDeclaration? mappings, IReadOnlyList<AbstractType> reached)
    {
        var interner = context.Model.TypeInterner;

        foreach(var slot in abstractType.MethodsList.Where(method => !method.Modifiers.IsStatic))
        {
            var answer = ConformanceAnswers.Method(slot, substitution, fillers, mappings, interner);
            var answering = answer?.Member;

            if(answering is not null)
            {
                if(Strengthened(context, concrete, abstractType, slot, answer!, substitution, answering.DeclarationNode ?? at))
                {
                    continue;
                }

                if(SlotSignatures.ReturnsOwned(slot) && !SlotSignatures.ReturnsOwned(answering))
                {
                    context.Report(
                        ProgramAnalysisDiagnostics.SlotReturnNotOwned, answering.DeclarationNode ?? at, [],
                        TypeDisplay.Mist(concrete), SlotSignatures.Display(slot, substitution, interner), TypeDisplay.Mist(abstractType), answering.Name);
                }
                else if(slot is { Modifiers.IsReadonly: true } && !inference.IsReadonly(answering))
                {
                    ReportContract(context, concrete, abstractType, answering.Name, answering.DeclarationNode ?? at);
                }
                else
                {
                    _ = CheckPromise(context, inference.ThroughSlot(slot, answering), concrete, abstractType, slot.Name, slot.Modifiers, answering.Name, answering.DeclarationNode ?? at);
                }

                continue;
            }

            if(slot.Body is not null || NativeDefaults.Answer(slot, abstractType, reached) || mappings?.UnresolvedTargets.Contains(slot.Name) == true)
            {
                continue;
            }

            ReportUnanswered(
                context, concrete, at, abstractType, mappings, SlotSignatures.Display(slot, substitution, interner),
                slot.Name, fillers.Any(filler => filler.Method?.Name == slot.Name));
        }

        foreach(var slot in abstractType.PropertiesList.Where(IsPropertySlot))
        {
            var answer = ConformanceAnswers.Property(slot, substitution, fillers, mappings, interner)?.Member;

            if(answer is null && mappings?.UnresolvedTargets.Contains(slot.Name) == true)
            {
                continue;
            }

            if(answer is null)
            {
                ReportUnanswered(
                    context, concrete, at, abstractType, mappings, $"{TypeDisplay.Mist(substitution.Apply(slot.Type, interner))} {slot.Name}",
                    slot.Name, named: false);
                continue;
            }

            foreach(var accessor in MissingAccessors(slot.Getter, slot.Setter, answer.Getter, answer.Setter))
            {
                context.Report(
                    ProgramAnalysisDiagnostics.PropertySlotAccessorMissing, at, [],
                    TypeDisplay.Mist(concrete), slot.Name, accessor, TypeDisplay.Mist(abstractType));
            }

            if(slot is { Getter: not null, Modifiers.IsReadonly: true } && answer.Getter is not null && !inference.IsReadonlyGetter(answer))
            {
                ReportContract(context, concrete, abstractType, answer.Name, answer.DeclarationNode ?? at);
                continue;
            }

            CheckAccessors(context, inference, concrete, abstractType, slot.Name, slot.Modifiers, slot.Getter, slot.Setter, answer.Name, answer.Getter, answer.Setter, answer.DeclarationNode ?? at);
        }

        foreach(var slot in abstractType.IndexersList.Where(indexer => IsSlot(indexer.Getter, indexer.Setter)))
        {
            var name = SlotSignatures.Display(slot, substitution, interner);

            if(ConformanceAnswers.Indexer(slot, substitution, fillers, interner) is not { } answer)
            {
                ReportUnanswered(context, concrete, at, abstractType, mappings, name, "this[]", fillers.Any(filler => filler.Indexer is not null));
                continue;
            }

            foreach(var accessor in MissingAccessors(slot.Getter, slot.Setter, answer.Getter, answer.Setter))
            {
                context.Report(
                    ProgramAnalysisDiagnostics.PropertySlotAccessorMissing, at, [],
                    TypeDisplay.Mist(concrete), SlotSignatures.Name(slot, substitution, interner), accessor, TypeDisplay.Mist(abstractType));
            }

            if(slot is { Getter: not null, Modifiers.IsReadonly: true } && answer.Getter is not null && !inference.IsReadonlyGetter(answer))
            {
                ReportContract(context, concrete, abstractType, name, answer.DeclarationNode ?? at);
                continue;
            }

            CheckAccessors(context, inference, concrete, abstractType, name, slot.Modifiers, slot.Getter, slot.Setter, name, answer.Getter, answer.Setter, answer.DeclarationNode ?? at);
        }
    }

    private static bool Strengthened(
        AnalysisContext context, ConcreteType concrete, AbstractType abstractType, Method slot, ConformanceAnswers.MethodAnswer answer,
        TypeSubstitution substitution, SyntaxNode at)
    {
        var interner = context.Model.TypeInterner;
        var found = false;

        foreach(var (parameter, constraint) in SlotSignatures.Strengthened(slot, answer.Member, substitution, answer.Source, at, interner))
        {
            context.Report(
                ProgramAnalysisDiagnostics.SlotConstraintStrengthened, at, [],
                TypeDisplay.Mist(concrete), SlotSignatures.Display(slot, substitution, interner), TypeDisplay.Mist(abstractType),
                answer.Member.Name, Spelled(constraint), parameter.Name);
            found = true;
        }

        return found;
    }

    private static string Spelled(TypeParameterConstraint constraint)
    {
        return constraint switch
        {
            TypeConstraint typed => TypeDisplay.Mist(typed.ConstraintType),
            ConstructorConstraint constructor => $"new({string.Join(", ", constructor.ParameterTypes.Select(TypeDisplay.Mist))})",
            _ => constraint.Kind.ToString().ToLowerInvariant(),
        };
    }

    private static void CheckAccessors(
        AnalysisContext context, PurityInference inference, ConcreteType concrete, AbstractType abstractType,
        string slot, MemberModifiers declared, PropertyAccessor? slotGetter, PropertyAccessor? slotSetter,
        string member, PropertyAccessor? getter, PropertyAccessor? setter, SyntaxNode at)
    {
        foreach(var (required, answering) in AnsweringAccessors(slotGetter, slotSetter, getter, setter))
        {
            if(!CheckPromise(context, inference.Of(answering), concrete, abstractType, slot, required.OwnModifiers ?? declared, member, at))
            {
                break;
            }
        }
    }

    private static void ReportUnanswered(
        AnalysisContext context, ConcreteType concrete, SyntaxNode at, AbstractType abstractType, ConformanceDeclaration? mappings,
        string signature, string member, bool named)
    {
        if(InheritsUnseen(concrete))
        {
            return;
        }

        if(mappings is not null)
        {
            context.Report(
                ProgramAnalysisDiagnostics.ConformanceSlotUnanswered, at, [],
                TypeDisplay.Mist(concrete), TypeDisplay.Mist(abstractType), signature);
        }
        else
        {
            context.Report(
                named ? ProgramAnalysisDiagnostics.SlotSignatureMismatch : ProgramAnalysisDiagnostics.SlotNotFilled, at, [],
                TypeDisplay.Mist(concrete), signature, TypeDisplay.Mist(abstractType), member);
        }
    }

    private static bool InheritsUnseen(ConcreteType concrete)
    {
        for(IMistType? current = concrete; current is not null;)
        {
            if((current is ConstructedType constructed ? constructed.Definition : current) is not ConcreteType type)
            {
                return false;
            }

            if(type.BaseUnresolved)
            {
                return true;
            }

            current = type.BaseType;
        }

        return false;
    }

    private static void ReportContract(AnalysisContext context, ConcreteType concrete, AbstractType abstractType, string member, SyntaxNode at)
    {
        context.Report(
            ProgramAnalysisDiagnostics.ReadonlyContractNotMet, at, [],
            member, TypeDisplay.Mist(concrete), TypeDisplay.Mist(abstractType));
    }

    private static bool CheckPromise(
        AnalysisContext context, Promise kept, ConcreteType concrete, AbstractType abstractType,
        string slot, MemberModifiers declared, string member, SyntaxNode at)
    {
        var required = Promise.Declared(declared);

        if(required is not ({ Local: true } or { Pure: true }))
        {
            return true;
        }

        if(required.Meet(kept) == required)
        {
            return true;
        }

        context.Report(
            ProgramAnalysisDiagnostics.PurityContractNotMet, at, [],
            member, TypeDisplay.Mist(concrete), required.Display, slot, TypeDisplay.Mist(abstractType), kept.Display);
        return false;
    }

    private static IEnumerable<(PropertyAccessor Required, PropertyAccessor Answering)> AnsweringAccessors(
        PropertyAccessor? slotGetter, PropertyAccessor? slotSetter, PropertyAccessor? getter, PropertyAccessor? setter)
    {
        if(slotGetter is not null && getter is not null)
        {
            yield return (slotGetter, getter);
        }

        if(slotSetter is not null && setter is not null)
        {
            yield return (slotSetter, setter);
        }
    }

    private static void CheckTypeSlots(
        AnalysisContext context, ConcreteType concrete, SyntaxNode at, AbstractType abstractType, TypeSubstitution substitution)
    {
        foreach(var slot in abstractType.TypeSlotsList)
        {
            if(TypeSlots.Filler(slot, concrete) is not { } filler)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.TypeSlotUnfilled, at, [],
                    TypeDisplay.Mist(concrete), MistType.StripArity(slot.Name), TypeDisplay.Mist(abstractType));
                continue;
            }

            foreach(var constraint in slot.ConstraintsList)
            {
                var (satisfied, spelling) = constraint switch
                {
                    TypeConstraint typed => (
                        ConversionClassifier.Classify(filler, substitution.Apply(typed.ConstraintType, context.Model.TypeInterner), context.Model.TypeInterner)
                            is ConversionKind.Identity or ConversionKind.Implicit or ConversionKind.Unclassified,
                        TypeDisplay.Mist(typed.ConstraintType)),
                    KeywordConstraint { Kind: TypeParameterConstraintKind.Class } => (filler is not StructType, "class"),
                    KeywordConstraint { Kind: TypeParameterConstraintKind.Struct } => (filler is not ClassType, "struct"),
                    KeywordConstraint { Kind: TypeParameterConstraintKind.Stampable } => (Stampability.IsStampable(filler), "stampable"),
                    _ => (true, ""),
                };

                if(!satisfied)
                {
                    context.Report(
                        ProgramAnalysisDiagnostics.TypeSlotConstraintNotSatisfied, at, [],
                        TypeDisplay.Mist(filler), spelling, TypeDisplay.Mist(abstractType), MistType.StripArity(slot.Name));
                }
            }
        }
    }

    private static bool IsPropertySlot(Property property)
    {
        return !property.Modifiers.IsStatic && IsSlot(property.Getter, property.Setter);
    }

    private static bool IsSlot(PropertyAccessor? getter, PropertyAccessor? setter)
    {
        return (getter is not null || setter is not null)
            && getter is not { Body: not null } and not { ExpressionBody: not null }
            && setter is not { Body: not null } and not { ExpressionBody: not null };
    }

    private static IEnumerable<string> MissingAccessors(
        PropertyAccessor? slotGetter, PropertyAccessor? slotSetter, PropertyAccessor? getter, PropertyAccessor? setter)
    {
        if(slotGetter is not null && getter is null)
        {
            yield return "get";
        }

        if(slotSetter is not null && setter is null)
        {
            yield return "set";
        }
    }

    internal static IEnumerable<(AbstractType Abstract, TypeSubstitution Substitution)> Reached(
        IEnumerable<IMistType> roots, TypeInterner interner, MemberType? conformer = null)
    {
        var reached = AbstractInheritance.Reached(roots, interner).ToList();
        var slots = conformer is null ? TypeSubstitution.Empty : TypeSlots.Closing(conformer, reached.Select(each => each.Definition));

        foreach(var (_, definition, substitution) in reached)
        {
            yield return (definition, substitution.Extend(slots));
        }
    }

    internal static IEnumerable<Filler> Fillers(ConcreteType concrete)
    {
        var substitution = TypeSubstitution.Empty;

        for(IMistType? current = concrete; current is not null;)
        {
            var definition = current is ConstructedType constructed ? constructed.Definition : current;

            if(definition is not ConcreteType type)
            {
                yield break;
            }

            substitution = substitution.Extend(TypeSubstitution.ForReceiver(current as ConstructedType));

            foreach(var method in type.MethodsList.Where(method => IsPublicInstance(method.Modifiers)))
            {
                yield return new Filler(method, null, null, substitution);
            }

            foreach(var property in type.PropertiesList.Where(property => IsPublicInstance(property.Modifiers)))
            {
                yield return new Filler(null, property, null, substitution);
            }

            foreach(var indexer in type.IndexersList.Where(indexer => IsPublicInstance(indexer.Modifiers)))
            {
                yield return new Filler(null, null, indexer, substitution);
            }

            current = type.BaseType is { } baseType ? substitution.Apply(baseType, interner: null) : null;
        }
    }

    private static bool IsPublicInstance(MemberModifiers modifiers)
    {
        return modifiers is { Accessibility: Accessibility.Public, IsStatic: false };
    }

    private static string Name(IMistType type)
    {
        return type.FullyQualifiedName;
    }

    internal sealed record Filler(Method? Method, Property? Property, Indexer? Indexer, TypeSubstitution Substitution);
}