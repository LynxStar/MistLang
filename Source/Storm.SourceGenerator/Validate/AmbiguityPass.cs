using System.Collections.Generic;
using Storm.SourceGenerator.Diagnostics;
using Storm.SourceGenerator.Frontend;
using Storm.SourceGenerator.Resolve;

namespace Storm.SourceGenerator.Validate;

/// <summary>
/// Checks each rule's alternatives for ambiguity that cannot be resolved within
/// bounded lookahead. If two alternatives share overlapping FIRST sets and cannot
/// be distinguished by bounded lookahead, emit STORM_AMBIGUOUS_RULE warnings.
/// For v1, this uses a simplified FIRST-set-based approach.
/// </summary>
public static class AmbiguityPass
{
    public static List<StormDiagnostic> Run(StormGrammar grammar)
    {
        var diagnostics = new List<StormDiagnostic>();

        foreach(var decl in grammar.Declarations)
        {
            if(decl is StormRuleDecl ruleDecl)
            {
                CheckRule(ruleDecl, grammar, diagnostics);
            }
        }

        return diagnostics;
    }

    private static void CheckRule(
        StormRuleDecl rule,
        StormGrammar grammar,
        List<StormDiagnostic> diagnostics)
    {
        var alternatives = ReachabilityPass.GetTopLevelAlternatives(rule.Body);
        if(alternatives.Count <= 1)
        {
            return;
        }

        // Compute FIRST sets for each alternative
        var firstSets = new List<HashSet<string>>();
        foreach(var alt in alternatives)
        {
            firstSets.Add(ReachabilityPass.ComputeFirstSet(alt, grammar));
        }

        // Check each pair of alternatives for overlapping FIRST sets
        var reportedPairs = new HashSet<string>();
        for(var i = 0; i < alternatives.Count; i++)
        {
            for(var j = i + 1; j < alternatives.Count; j++)
            {
                var overlap = new HashSet<string>(firstSets[i]);
                overlap.IntersectWith(firstSets[j]);

                if(overlap.Count > 0)
                {
                    var pairKey = $"{i},{j}";
                    if(reportedPairs.Add(pairKey))
                    {
                        diagnostics.Add(new StormDiagnostic(
                            StormDiagnosticIds.STORM_AMBIGUOUS_RULE,
                            StormDiagnosticSeverity.Warning,
                            $"Alternatives {i + 1} and {j + 1} in rule '{rule.Name}' have overlapping FIRST sets and may be ambiguous",
                            null,
                            rule.Line,
                            rule.Column));
                    }
                }
            }
        }
    }
}
