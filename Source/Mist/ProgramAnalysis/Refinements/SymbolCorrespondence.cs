using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;

namespace Mist.ProgramAnalysis.Refinements;

/// <summary>
/// Slot and filler members read through this, or parameters in corresponding positions, name one value.
/// Conflicting fillers under different closings establish no correspondence; all other symbols require
/// object identity, regardless of spelling.
/// </summary>
internal sealed class SymbolCorrespondence
{
    public static readonly SymbolCorrespondence None = new([], []);

    private readonly IReadOnlyList<(object Slot, object Filler)> members;
    private readonly IReadOnlyList<(Parameter Slot, Parameter Filler)> parameters;

    private SymbolCorrespondence(IReadOnlyList<(object Slot, object Filler)> members, IReadOnlyList<(Parameter Slot, Parameter Filler)> parameters)
    {
        this.members = members;
        this.parameters = parameters;
    }

    public static SymbolCorrespondence OfMembers(IEnumerable<(object Slot, object Filler)> answers)
    {
        var selected = answers
            .GroupBy(answer => answer.Slot, ReferenceEqualityComparer.Instance)
            .Where(answered => answered.Select(answer => answer.Filler).Distinct(ReferenceEqualityComparer.Instance).Count() == 1)
            .Select(answered => answered.First())
            .ToList();

        return new SymbolCorrespondence(selected, []);
    }

    public SymbolCorrespondence WithParameters(IReadOnlyList<Parameter> slot, IReadOnlyList<Parameter> filler)
    {
        if(slot.Count != filler.Count || slot.Any(parameter => ValuePack.PackParameterOf(parameter) is not null))
        {
            return new SymbolCorrespondence(members, []);
        }

        return new SymbolCorrespondence(members, [.. slot.Zip(filler).Where(pair =>
            pair.First.Modifier.Direction != ByRefDirection.Out && pair.Second.Modifier.Direction != ByRefDirection.Out)]);
    }

    public bool SameMember(object left, object right)
    {
        return ReferenceEquals(left, right) || members.Any(pair => Pairs(pair.Slot, pair.Filler, left, right));
    }

    public bool SameParameter(Parameter left, Parameter right)
    {
        return ReferenceEquals(left, right) || parameters.Any(pair => Pairs(pair.Slot, pair.Filler, left, right));
    }

    public bool SameReceiver(LocalVariable left, LocalVariable right)
    {
        return ReferenceEquals(left, right) || (left.Parameter is { } named && right.Parameter is { } other && SameParameter(named, other));
    }

    private static bool Pairs(object slot, object filler, object left, object right)
    {
        return (ReferenceEquals(slot, left) && ReferenceEquals(filler, right)) || (ReferenceEquals(slot, right) && ReferenceEquals(filler, left));
    }
}