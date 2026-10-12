using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

internal static partial class PatternBinder
{
    /// <summary>
    /// Null when any predicate in the chain is unbound, rather than the predicates that did bind: see
    /// <see cref="BindRefinement"/>.
    /// </summary>
    internal static Pattern? ComposeRefinement(IMistType tested, SyntaxNode node, Func<RefinedType, Pattern?> predicateOf)
    {
        if(tested is not RefinedType refined || predicateOf(refined) is not { } own)
        {
            return null;
        }

        if(Unaliased(refined.BaseType) is not RefinedType inner)
        {
            return own;
        }

        return ComposeRefinement(inner, node, predicateOf) is { } beneath
            ? new ConjunctionPattern(node, tested, [.. Conjuncts(beneath), .. Conjuncts(own)])
            : null;
    }

    private static IEnumerable<Pattern> Conjuncts(Pattern pattern)
    {
        return pattern is ConjunctionPattern conjunction ? conjunction.Operands : [pattern];
    }

    private static IMistType Unaliased(IMistType type)
    {
        return type is TypeAlias { AliasedType: { } aliased } ? Unaliased(aliased) : type;
    }
}