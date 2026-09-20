using System.Collections.Generic;
using System.Linq;
using Storm.SourceGenerator.Diagnostics;
using Storm.SourceGenerator.Frontend;
using Storm.SourceGenerator.Resolve;

namespace Storm.SourceGenerator.Validate;

/// <summary>
/// Builds the rule-call graph from the effective grammar (only rules, not tokens/fragments)
/// and detects direct and indirect left-recursion cycles. A rule is left-recursive if it
/// can reach itself through the leftmost position of its alternatives.
/// Emits STORM_LEFT_RECURSION diagnostics with the cycle path.
/// </summary>
public static class LeftRecursionPass
{
    public static List<StormDiagnostic> Run(StormGrammar grammar)
    {
        var diagnostics = new List<StormDiagnostic>();

        // Collect all rule declarations by name
        var rules = new Dictionary<string, StormRuleDecl>();
        foreach(var decl in grammar.Declarations)
        {
            if(decl is StormRuleDecl ruleDecl)
            {
                rules[ruleDecl.Name] = ruleDecl;
            }
        }

        if(rules.Count == 0)
        {
            return diagnostics;
        }

        // Build the left-call graph: for each rule, find which rules can be called
        // at the leftmost position of any alternative
        var leftCallGraph = new Dictionary<string, HashSet<string>>();
        foreach(var kvp in rules)
        {
            leftCallGraph[kvp.Key] = CollectLeftmostRuleReferences(kvp.Value.Body, rules, grammar);
        }

        // Detect cycles using DFS with coloring
        var visited = new HashSet<string>();
        var reportedRules = new HashSet<string>();

        foreach(var ruleName in rules.Keys)
        {
            if(!visited.Contains(ruleName))
            {
                var path = new List<string>();
                var onStack = new HashSet<string>();
                DetectCycles(ruleName, leftCallGraph, path, onStack, visited, reportedRules, rules, diagnostics);
            }
        }

        return diagnostics;
    }

    /// <summary>
    /// Collects the set of rule names that can appear at the leftmost position
    /// of the given expression. This follows through sequences (only the first
    /// element matters unless it's nullable), alternations (all alternatives),
    /// groups, optionals, etc.
    /// </summary>
    private static HashSet<string> CollectLeftmostRuleReferences(
        StormExpression expr,
        Dictionary<string, StormRuleDecl> rules,
        StormGrammar grammar)
    {
        var result = new HashSet<string>();
        CollectLeftmost(expr, rules, grammar, result);
        return result;
    }

    private static void CollectLeftmost(
        StormExpression expr,
        Dictionary<string, StormRuleDecl> rules,
        StormGrammar grammar,
        HashSet<string> result)
    {
        switch(expr)
        {
            case StormReferenceExpr refExpr:
                if(rules.ContainsKey(refExpr.Name))
                {
                    _ = result.Add(refExpr.Name);
                }

                break;

            case StormQualifiedReferenceExpr qualRef:
                var qualName = qualRef.Qualifier + "." + qualRef.Name;
                var resolved = grammar.LookupByName(qualName);
                if(resolved is StormRuleDecl)
                {
                    _ = result.Add(qualName);
                }

                break;

            case StormSequenceExpr seqExpr:
                // The leftmost element of a sequence is the first element.
                // If the first element is nullable (optional, zero-or-more),
                // then the second element is also leftmost, etc.
                foreach(var element in seqExpr.Elements)
                {
                    CollectLeftmost(element, rules, grammar, result);
                    if(!IsNullable(element))
                    {
                        break;
                    }
                }

                break;

            case StormAlternationExpr altExpr:
                foreach(var alt in altExpr.Alternatives)
                {
                    CollectLeftmost(alt, rules, grammar, result);
                }

                break;

            case StormOptionalExpr optExpr:
                CollectLeftmost(optExpr.Inner, rules, grammar, result);
                break;

            case StormZeroOrMoreExpr zomExpr:
                CollectLeftmost(zomExpr.Inner, rules, grammar, result);
                break;

            case StormOneOrMoreExpr oomExpr:
                CollectLeftmost(oomExpr.Inner, rules, grammar, result);
                break;

            case StormGroupExpr groupExpr:
                CollectLeftmost(groupExpr.Inner, rules, grammar, result);
                break;

            case StormPredicateExpr predicateExpr:
                CollectLeftmost(predicateExpr.Inner, rules, grammar, result);
                break;

            case StormLabeledExpr labeledExpr:
                CollectLeftmost(labeledExpr.Element, rules, grammar, result);
                break;

            case StormNamedAlternative namedAlt:
                CollectLeftmost(namedAlt.Expression, rules, grammar, result);
                break;

            case StormStringLiteralExpr:
            case StormCharClassExpr:
                // Terminal — not a rule reference
                break;
        }
    }

    /// <summary>
    /// Determines if an expression can match the empty string.
    /// </summary>
    private static bool IsNullable(StormExpression expr)
    {
        return expr switch
        {
            StormOptionalExpr or StormZeroOrMoreExpr or StormPredicateExpr => true,
            StormGroupExpr groupExpr => IsNullable(groupExpr.Inner),
            StormLabeledExpr labeledExpr => IsNullable(labeledExpr.Element),
            StormNamedAlternative namedAlt => IsNullable(namedAlt.Expression),
            StormSequenceExpr seqExpr => seqExpr.Elements.All(IsNullable),
            StormAlternationExpr altExpr => altExpr.Alternatives.Any(IsNullable),
            _ => false,
        };
    }

    /// <summary>
    /// DFS-based cycle detection in the left-call graph.
    /// </summary>
    private static void DetectCycles(
        string current,
        Dictionary<string, HashSet<string>> graph,
        List<string> path,
        HashSet<string> onStack,
        HashSet<string> visited,
        HashSet<string> reportedRules,
        Dictionary<string, StormRuleDecl> rules,
        List<StormDiagnostic> diagnostics)
    {
        path.Add(current);
        _ = onStack.Add(current);

        if(graph.TryGetValue(current, out var neighbors))
        {
            foreach(var neighbor in neighbors)
            {
                if(onStack.Contains(neighbor))
                {
                    // Found a cycle — extract the cycle path
                    var cycleStart = path.IndexOf(neighbor);
                    var cyclePath = path.GetRange(cycleStart, path.Count - cycleStart);
                    cyclePath.Add(neighbor); // close the cycle

                    // Only report if we haven't already reported this cycle's starting rule
                    if(!reportedRules.Contains(neighbor))
                    {
                        _ = reportedRules.Add(neighbor);
                        var cycleStr = string.Join(" -> ", cyclePath);

                        var ruleDecl = rules.ContainsKey(neighbor) ? rules[neighbor] : null;
                        diagnostics.Add(new StormDiagnostic(
                            StormDiagnosticIds.STORM_LEFT_RECURSION,
                            StormDiagnosticSeverity.Error,
                            $"Left-recursion detected: {cycleStr}",
                            null,
                            ruleDecl?.Line ?? 0,
                            ruleDecl?.Column ?? 0));
                    }
                }
                else if(!visited.Contains(neighbor) && graph.ContainsKey(neighbor))
                {
                    DetectCycles(neighbor, graph, path, onStack, visited, reportedRules, rules, diagnostics);
                }
            }
        }

        path.RemoveAt(path.Count - 1);
        _ = onStack.Remove(current);
        _ = visited.Add(current);
    }
}