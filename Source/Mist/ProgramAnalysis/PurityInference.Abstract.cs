using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis;

internal sealed partial class PurityInference
{
    private IReadOnlyList<object> ImplementationsOf(object slot, AbstractType abstractType)
    {
        if(implementations.TryGetValue(slot, out var known))
        {
            return known;
        }

        var interner = context.Model.TypeInterner;
        var found = new List<object>();

        foreach(var concrete in context.Types.OfType<ConcreteType>())
        {
            var reaches = concrete.BaseAbstractTypes.Concat(concrete.ConformancesList.Select(conformance => conformance.AbstractType));

            foreach(var (definition, substitution) in ConformanceAnalyzer.Reached(reaches, interner, concrete))
            {
                if(!ReferenceEquals(definition, abstractType))
                {
                    continue;
                }

                object? answering = slot switch
                {
                    Method method => AnsweringMethod(concrete, method, substitution, interner),
                    PropertyAccessor accessor => AnsweringAccessor(concrete, accessor, substitution, interner),
                    _ => null,
                };

                if(answering is not null)
                {
                    found.Add(answering);
                }
            }
        }

        implementations[slot] = found;
        return found;
    }

    private Method? AnsweringMethod(ConcreteType concrete, Method slot, TypeSubstitution substitution, TypeInterner interner)
    {
        var fillers = FillersOf(concrete);
        var mapped = concrete.ConformancesList.SelectMany(conformance => conformance.Mappings).OfType<MethodMapping>()
            .FirstOrDefault(mapping => mapping.TargetName == slot.Name && Declares(mapping.Declarer, slot.DeclaringType)
                && SlotSignatures.Answers(slot, mapping.Source, substitution, ConformanceAnswers.SourceClosing(mapping.Source, fillers), interner));

        return mapped?.Source
            ?? SlotSignatures.ExactFirst(fillers, slot, substitution, interner).FirstOrDefault(filler => filler.Method is { } method && method.Name == slot.Name
                && SlotSignatures.Answers(slot, method, substitution, filler.Substitution, interner))?.Method;
    }

    private static bool Declares(IMistType declarer, MemberType? slotOwner)
    {
        return ReferenceEquals(PurityJudge.DefinitionOf(declarer), slotOwner);
    }

    private PropertyAccessor? AnsweringAccessor(ConcreteType concrete, PropertyAccessor slot, TypeSubstitution substitution, TypeInterner interner)
    {
        var owner = accessors[slot];
        var fillers = FillersOf(concrete);

        if(owner.Property is { } property)
        {
            var mapped = concrete.ConformancesList.SelectMany(conformance => conformance.Mappings).OfType<PropertyMapping>()
                .FirstOrDefault(mapping => mapping.TargetName == property.Name && Declares(mapping.Declarer, property.DeclaringType)
                    && SlotSignatures.Agree(
                        substitution.Apply(property.Type, interner),
                        ConformanceAnswers.SourceClosing(mapping.Source, fillers).Apply(mapping.Source.Type, interner)));

            var answering = mapped?.Source
                ?? fillers.Find(filler => filler.Property is { } candidate && candidate.Name == property.Name
                    && SlotSignatures.Agree(substitution.Apply(property.Type, interner), filler.Substitution.Apply(candidate.Type, interner)))?.Property;

            return owner.IsSetter ? answering?.Setter : answering?.Getter;
        }

        var answered = ConformanceAnswers.Indexer(owner.Indexer!, substitution, fillers, interner);

        return owner.IsSetter ? answered?.Setter : answered?.Getter;
    }

    private List<ConformanceAnalyzer.Filler> FillersOf(ConcreteType concrete)
    {
        if(!fillers.TryGetValue(concrete, out var known))
        {
            known = ConformanceAnalyzer.Fillers(concrete).ToList();
            fillers[concrete] = known;
        }

        return known;
    }
}