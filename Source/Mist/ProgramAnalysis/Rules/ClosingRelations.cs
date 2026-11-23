using Mist.ProgramAnalysis.Facts;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// The part of a dependent parameter's <c>where</c> that relates it to a sibling slot, <c>B >= A</c>, judged
/// at every closing with each sibling read as the argument the closing gives it (adhoc-refinement-constraints
/// Requirements 5.2 to 5.6), so a body may rely on it as the facts of its declared refinement
/// (flow-sensitive-refinement-inference Requirement 1.1). A closing is judged where all it names can be read:
/// a bound naming a capture at the step the flow declares the last of them, any other bound with no facts but
/// the forwarded parameters' own. A slot argued by a capture owes its whole <c>where</c> at its own capture.
/// A sibling closed with <c>unknown</c> or a refinement is some object's own value, which its creation proved.
/// </summary>
internal static class ClosingRelations
{
    public delegate Term? SlotTerm(ref FactState state, TypeParameter sibling, IMistType argument);

    public static Requirement? Of(TypeParameter parameter)
    {
        return parameter is { IsDependent: true, IsPack: false, Refinement: { } predicate }
            && Requirement.Of(predicate, parameter.Domain is { } domain ? RealRequirement.KindOf(domain) : null) is { Bounds.IsEmpty: false } required
                ? new Requirement(Admitted.Everything, required.Bounds)
                : null;
    }

    public static Proof? Judge(
        FactState state, FactReading reading, TypeParameter parameter, IMistType argument, IReadOnlyDictionary<TypeParameter, IMistType> closing,
        Func<IReadOnlyList<IMistType>, bool> judges, SlotTerm sibling, Budget? budget = null)
    {
        if(Of(parameter) is not { } relation || argument is CapturedValue || SlotReads.Value(ref state, reading, argument, null, parameter) is not { } value)
        {
            return null;
        }

        Proof? judged = null;

        foreach(var bound in relation.Bounds)
        {
            var named = Named(bound);

            if(named.Exists(name => !closing.ContainsKey(name)) || !judges([.. named.Select(name => closing[name])]))
            {
                continue;
            }

            var current = state;
            var denial = ContractReader.Denial(
                new Requirement(Admitted.Everything, [bound]), value,
                symbol => symbol.Named is TypeParameter name && closing.TryGetValue(name, out var filled) ? sibling(ref current, name, filled) : null);

            if(denial is null)
            {
                continue;
            }

            judged = Chain.Prove(current.Bag, denial, value.Variables.Concat(denial.SelectMany(alternative => alternative.Facts).SelectMany(fact => fact.Term.Variables)).ToHashSet(), budget);

            if(judged is not Proven)
            {
                return judged;
            }
        }

        return judged;
    }

    private static List<TypeParameter> Named(SymbolicBound bound)
    {
        var named = new List<TypeParameter>();
        _ = ContractReader.Limit(bound, symbol =>
        {
            if(symbol.Named is TypeParameter name)
            {
                named.Add(name);
            }

            return new Term(0);
        });

        return named;
    }
}