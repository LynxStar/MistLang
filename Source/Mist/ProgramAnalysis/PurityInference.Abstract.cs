using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
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

            foreach(var (definition, substitution) in ConformanceAnalyzer.Reached(reaches, interner))
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
        var mapped = concrete.ConformancesList.SelectMany(conformance => conformance.Mappings).OfType<MethodMapping>()
            .FirstOrDefault(mapping => mapping.TargetName == slot.Name && SlotSignatures.Answers(slot, mapping.Source, substitution, interner));

        return mapped?.Source
            ?? FillersOf(concrete).Find(filler => filler.Method is { } method && method.Name == slot.Name
                && SlotSignatures.Answers(slot, method, substitution.Extend(filler.Substitution), interner))?.Method;
    }

    private PropertyAccessor? AnsweringAccessor(ConcreteType concrete, PropertyAccessor slot, TypeSubstitution substitution, TypeInterner interner)
    {
        var owner = accessors[slot];

        if(owner.Property is { } property)
        {
            var mapped = concrete.ConformancesList.SelectMany(conformance => conformance.Mappings).OfType<PropertyMapping>()
                .FirstOrDefault(mapping => mapping.TargetName == property.Name
                    && SlotSignatures.Agree(substitution.Apply(property.Type, interner), mapping.Source.Type));

            var answering = mapped?.Source
                ?? FillersOf(concrete).Find(filler => filler.Property is { } candidate && candidate.Name == property.Name
                    && SlotSignatures.Agree(substitution.Extend(filler.Substitution).Apply(property.Type, interner), candidate.Type))?.Property;

            return owner.IsSetter ? answering?.Setter : answering?.Getter;
        }

        var indexer = owner.Indexer!;

        for(var current = concrete; current is not null; current = current.BaseType as ConcreteType)
        {
            var answering = current.IndexersList.Find(candidate => candidate.ParametersList.Count == indexer.ParametersList.Count
                && SlotSignatures.Agree(substitution.Apply(indexer.Type, interner), candidate.Type)
                && candidate.ParametersList.Zip(indexer.ParametersList).All(pair => SlotSignatures.Agree(substitution.Apply(pair.Second.Type, interner), pair.First.Type)));

            if(answering is not null)
            {
                return owner.IsSetter ? answering.Setter : answering.Getter;
            }
        }

        return null;
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