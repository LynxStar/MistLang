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
/// of the abstract-type-unification spec, and order 7 of the analysis register. Slots are the bodiless
/// members of the type's own base list, transitively through the abstract hierarchy; fillers are the
/// public members the type declares or inherits along its concrete base chain. A default fills its own
/// slot and NOTHING ELSE: one restated on a derived abstract hides the base's slot rather than filling
/// it, so the obligation stays with the conformer, which is what both pipelines measure and what C#
/// does with a hiding interface member. A with block answers the slots of the abstract it names by its
/// mappings or by the type's own members, and is redundant when the abstract is reached already. A slot
/// that is `readonly`, which every slot of a readonly abstract is, is answered only by a member that
/// writes nothing, by its modifier or by <see cref="ReadonlyInference"/>: readonly-contexts Requirement
/// 13, which holds the member and not the conforming type, so a mutable type may meet it.
/// </summary>
internal sealed class ConformanceAnalyzer : IProgramAnalyzer
{
    public string Id => "abstract-conformance-checking";

    public void Analyze(AnalysisContext context)
    {
        var inference = new ReadonlyInference();

        foreach(var type in context.Types)
        {
            if(type is not ConcreteType { DeclarationNode: { } node } concrete)
            {
                continue;
            }

            var fillers = Fillers(concrete).ToList();

            foreach(var (abstractType, substitution) in Reached(concrete.BaseAbstractTypes, context.Model.TypeInterner))
            {
                CheckSlots(context, inference, concrete, node, abstractType, substitution, fillers, mappings: null);
                CheckTypeSlots(context, concrete, node, abstractType, substitution);
            }

            CheckConformances(context, inference, concrete, fillers);
        }
    }

    private static void CheckConformances(AnalysisContext context, ReadonlyInference inference, ConcreteType concrete, List<Filler> fillers)
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

            foreach(var (abstractType, substitution) in Reached([conformance.AbstractType], context.Model.TypeInterner))
            {
                CheckSlots(context, inference, concrete, conformance.DeclarationNode, abstractType, substitution, fillers, conformance);
                CheckTypeSlots(context, concrete, conformance.DeclarationNode, abstractType, substitution);
            }
        }
    }

    private static void CheckSlots(
        AnalysisContext context, ReadonlyInference inference, ConcreteType concrete, SyntaxNode at, AbstractType abstractType,
        TypeSubstitution substitution, List<Filler> fillers, ConformanceDeclaration? mappings)
    {
        var interner = context.Model.TypeInterner;

        foreach(var slot in abstractType.MethodsList.Where(method => method.Body is null && !method.Modifiers.IsStatic))
        {
            var answering = mappings?.Mappings.OfType<MethodMapping>()
                .FirstOrDefault(mapping => mapping.TargetName == slot.Name && SlotSignatures.Answers(slot, mapping.Source, substitution, interner))?.Source
                ?? fillers.FirstOrDefault(filler => filler.Method is { } method && method.Name == slot.Name && SlotSignatures.Answers(slot, method, substitution.Extend(filler.Substitution), interner))?.Method;

            if(answering is not null)
            {
                if(slot.Modifiers.IsReadonly && !inference.IsReadonly(answering))
                {
                    ReportContract(context, concrete, abstractType, answering.Name, answering.DeclarationNode ?? at);
                }

                continue;
            }

            if(mappings?.UnresolvedTargets.Contains(slot.Name) == true)
            {
                continue;
            }

            var signature = SlotSignatures.Display(slot, substitution, interner);

            if(mappings is not null)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.ConformanceSlotUnanswered, at, [],
                    TypeDisplay.Mist(concrete), TypeDisplay.Mist(abstractType), signature);
            }
            else if(fillers.Any(filler => filler.Method?.Name == slot.Name))
            {
                context.Report(
                    ProgramAnalysisDiagnostics.SlotSignatureMismatch, at, [],
                    TypeDisplay.Mist(concrete), signature, TypeDisplay.Mist(abstractType), slot.Name);
            }
            else
            {
                context.Report(
                    ProgramAnalysisDiagnostics.SlotNotFilled, at, [],
                    TypeDisplay.Mist(concrete), signature, TypeDisplay.Mist(abstractType), slot.Name);
            }
        }

        foreach(var slot in abstractType.PropertiesList.Where(IsPropertySlot))
        {
            var answer = mappings?.Mappings.OfType<PropertyMapping>()
                .FirstOrDefault(mapping => mapping.TargetName == slot.Name
                    && SlotSignatures.Agree(substitution.Apply(slot.Type, interner), mapping.Source.Type))?.Source
                ?? fillers.FirstOrDefault(filler => filler.Property is { } property && property.Name == slot.Name
                    && SlotSignatures.Agree(substitution.Extend(filler.Substitution).Apply(slot.Type, interner), property.Type))?.Property;

            if(answer is null && mappings?.UnresolvedTargets.Contains(slot.Name) == true)
            {
                continue;
            }

            if(answer is null)
            {
                var signature = $"{TypeDisplay.Mist(substitution.Apply(slot.Type, interner))} {slot.Name}";

                context.Report(
                    mappings is null ? ProgramAnalysisDiagnostics.SlotNotFilled : ProgramAnalysisDiagnostics.ConformanceSlotUnanswered,
                    at, [],
                    TypeDisplay.Mist(concrete),
                    mappings is null ? signature : TypeDisplay.Mist(abstractType),
                    mappings is null ? TypeDisplay.Mist(abstractType) : signature,
                    slot.Name);
                continue;
            }

            foreach(var accessor in MissingAccessors(slot, answer))
            {
                context.Report(
                    ProgramAnalysisDiagnostics.PropertySlotAccessorMissing, at, [],
                    TypeDisplay.Mist(concrete), slot.Name, accessor, TypeDisplay.Mist(abstractType));
            }

            if(slot is { Getter: not null, Modifiers.IsReadonly: true } && answer.Getter is not null && !inference.IsReadonlyGetter(answer))
            {
                ReportContract(context, concrete, abstractType, answer.Name, answer.DeclarationNode ?? at);
            }
        }
    }

    private static void ReportContract(AnalysisContext context, ConcreteType concrete, AbstractType abstractType, string member, SyntaxNode at)
    {
        context.Report(
            ProgramAnalysisDiagnostics.ReadonlyContractNotMet, at, [],
            member, TypeDisplay.Mist(concrete), TypeDisplay.Mist(abstractType));
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
        return !property.Modifiers.IsStatic
            && (property.Getter is not null || property.Setter is not null)
            && property.Getter is not { Body: not null } and not { ExpressionBody: not null }
            && property.Setter is not { Body: not null } and not { ExpressionBody: not null };
    }

    private static IEnumerable<string> MissingAccessors(Property slot, Property answer)
    {
        if(slot.Getter is not null && answer.Getter is null)
        {
            yield return "get";
        }

        if(slot.Setter is not null && answer.Setter is null)
        {
            yield return "set";
        }
    }

    internal static IEnumerable<(AbstractType Abstract, TypeSubstitution Substitution)> Reached(
        IEnumerable<IMistType> roots, TypeInterner interner)
    {
        foreach(var (_, definition, substitution) in AbstractInheritance.Reached(roots, interner))
        {
            yield return (definition, substitution);
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

            foreach(var method in type.MethodsList.Where(IsPublicInstance))
            {
                yield return new Filler(method, null, substitution);
            }

            foreach(var property in type.PropertiesList.Where(property => property.Modifiers is { Accessibility: Accessibility.Public, IsStatic: false }))
            {
                yield return new Filler(null, property, substitution);
            }

            current = type.BaseType;
        }
    }

    private static bool IsPublicInstance(Method method)
    {
        return method.Modifiers is { Accessibility: Accessibility.Public, IsStatic: false };
    }

    private static string Name(IMistType type)
    {
        return type.FullyQualifiedName;
    }

    internal sealed record Filler(Method? Method, Property? Property, TypeSubstitution Substitution);
}