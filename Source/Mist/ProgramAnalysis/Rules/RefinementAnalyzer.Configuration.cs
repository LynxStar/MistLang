using Mist.ProgramAnalysis.Facts;
using Mist.ProgramAnalysis.Refinements;

namespace Mist.ProgramAnalysis.Rules;

internal sealed partial class RefinementAnalyzer
{
    public RefinementAnalyzer()
        : this(LiteralPredicates.MatchLimit, Budget.Default)
    {
    }

    public RefinementAnalyzer(TimeSpan matchLimit)
        : this(matchLimit, Budget.Default)
    {
    }

    public RefinementAnalyzer(Budget budget)
        : this(LiteralPredicates.MatchLimit, budget)
    {
    }

    public RefinementAnalyzer(int alternativeCap)
        : this(LiteralPredicates.MatchLimit, Budget.Default, alternativeCap)
    {
    }
}