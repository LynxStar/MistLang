using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;

namespace Mist.ProgramAnalysis;

internal static class ConformanceAnswers
{
    public static MethodAnswer? Method(
        Method slot, TypeSubstitution substitution, IReadOnlyList<ConformanceAnalyzer.Filler> fillers,
        ConformanceDeclaration? mappings, TypeInterner interner)
    {
        foreach(var mapping in mappings?.Mappings.OfType<MethodMapping>() ?? [])
        {
            var source = SourceClosing(mapping.Source, fillers);

            if(mapping.TargetName == slot.Name && SlotSignatures.Answers(slot, mapping.Source, substitution, source, interner))
            {
                return new MethodAnswer(mapping.Source, source, SlotSignatures.Closing(slot, mapping.Source, substitution));
            }
        }

        foreach(var filler in SlotSignatures.ExactFirst(fillers, slot, substitution, interner))
        {
            if(filler.Method is { } method && method.Name == slot.Name
                && SlotSignatures.Answers(slot, method, substitution, filler.Substitution, interner))
            {
                return new MethodAnswer(method, filler.Substitution, SlotSignatures.Closing(slot, method, substitution));
            }
        }

        return null;
    }

    public static PropertyAnswer? Property(
        Property slot, TypeSubstitution substitution, IReadOnlyList<ConformanceAnalyzer.Filler> fillers,
        ConformanceDeclaration? mappings, TypeInterner interner)
    {
        foreach(var mapping in mappings?.Mappings.OfType<PropertyMapping>() ?? [])
        {
            var source = SourceClosing(mapping.Source, fillers);

            if(mapping.TargetName == slot.Name
                && SlotSignatures.Agree(substitution.Apply(slot.Type, interner), source.Apply(mapping.Source.Type, interner)))
            {
                return new PropertyAnswer(mapping.Source, source, substitution);
            }
        }

        foreach(var filler in fillers)
        {
            if(filler.Property is { } property && property.Name == slot.Name
                && SlotSignatures.Agree(substitution.Apply(slot.Type, interner), filler.Substitution.Apply(property.Type, interner)))
            {
                return new PropertyAnswer(property, filler.Substitution, substitution);
            }
        }

        return null;
    }

    public static Indexer? Indexer(
        Indexer slot, TypeSubstitution substitution, IReadOnlyList<ConformanceAnalyzer.Filler> fillers, TypeInterner interner)
    {
        return fillers.FirstOrDefault(filler => filler.Indexer is { } indexer
            && SlotSignatures.Answers(slot, indexer, substitution, filler.Substitution, interner))?.Indexer;
    }

    public static TypeSubstitution SourceClosing(object member, IReadOnlyList<ConformanceAnalyzer.Filler> fillers)
    {
        return fillers.FirstOrDefault(filler => ReferenceEquals(filler.Method, member) || ReferenceEquals(filler.Property, member))?.Substitution
            ?? TypeSubstitution.Empty;
    }

    internal sealed record MethodAnswer(Method Member, TypeSubstitution Source, TypeSubstitution Target);

    internal sealed record PropertyAnswer(Property Member, TypeSubstitution Source, TypeSubstitution Target);
}