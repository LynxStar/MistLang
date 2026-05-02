using System.Collections.Generic;
using System.Linq;
using Storm.SourceGenerator.Diagnostics;
using Storm.SourceGenerator.Frontend;
using Storm.SourceGenerator.Resolve;

namespace Storm.SourceGenerator.Validate;

/// <summary>
/// Checks each rule's alternatives for unreachable alternatives under PEG ordered-choice
/// semantics with backtracking.
///
/// The generated parser tries each alternative in order, saving and restoring position
/// on failure. An alternative is only truly unreachable if an earlier alternative will
/// always succeed on any input that the later alternative would match.
///
/// This pass uses FIRST-k sequence analysis (bounded lookahead depth) rather than
/// single-token FIRST sets. Two alternatives that share the same first token but differ
/// on subsequent tokens are distinguishable via backtracking and are NOT flagged.
///
/// An alternative k is flagged as unreachable only if every token sequence it can start
/// with is also a prefix of an earlier alternative's token sequences — meaning the
/// earlier alternative will always be tried first and will match the same input.
///
/// Emits STORM_UNREACHABLE_ALTERNATIVE warnings.
/// </summary>
public static class ReachabilityPass
{
    /// <summary>
    /// Maximum lookahead depth for FIRST-k sequence computation.
    /// Deeper lookahead catches more cases but is more expensive.
    /// A depth of 3 handles the vast majority of grammar patterns.
    /// </summary>
    private const int MaxLookahead = 3;

    public static List<StormDiagnostic> Run(StormGrammar grammar)
    {
        var diagnostics = new List<StormDiagnostic>();

        foreach (var decl in grammar.Declarations)
        {
            if (decl is StormRuleDecl ruleDecl)
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
        var alternatives = GetTopLevelAlternatives(rule.Body);
        if (alternatives.Count <= 1)
        {
            return;
        }

        // Compute FIRST-k sequence sets for each alternative
        var sequenceSets = new List<HashSet<string>>();
        foreach (var alt in alternatives)
        {
            sequenceSets.Add(ComputeFirstKSequences(alt, grammar, MaxLookahead));
        }

        // Check each alternative k against the union of 1..k-1
        // An alternative is unreachable only if ALL of its FIRST-k sequences
        // are also present in the union of earlier alternatives' sequences.
        var unionSoFar = new HashSet<string>();
        for (var i = 0; i < alternatives.Count; i++)
        {
            if (i > 0 && sequenceSets[i].Count > 0 && sequenceSets[i].IsSubsetOf(unionSoFar))
            {
                diagnostics.Add(new StormDiagnostic(
                    StormDiagnosticIds.STORM_UNREACHABLE_ALTERNATIVE,
                    StormDiagnosticSeverity.Warning,
                    $"Alternative {i + 1} in rule '{rule.Name}' is unreachable; its lookahead sequences are covered by earlier alternatives",
                    null,
                    rule.Line,
                    rule.Column));
            }

            foreach (var seq in sequenceSets[i])
            {
                _ = unionSoFar.Add(seq);
            }
        }
    }

    /// <summary>
    /// Extracts the top-level alternatives from a rule body.
    /// If the body is an alternation, returns its alternatives.
    /// Otherwise returns a single-element list.
    /// </summary>
    internal static IReadOnlyList<StormExpression> GetTopLevelAlternatives(StormExpression body)
    {
        return body is StormAlternationExpr altExpr
            ? altExpr.Alternatives
            : new List<StormExpression> { body };
    }

    /// <summary>
    /// Computes the FIRST set of an expression — the set of token/string-literal/char-class
    /// identifiers that can appear at the start position. Uses string representations
    /// as set elements for comparison.
    /// </summary>
    internal static HashSet<string> ComputeFirstSet(StormExpression expr, StormGrammar grammar)
    {
        var result = new HashSet<string>();
        ComputeFirst(expr, grammar, result, new HashSet<string>());
        return result;
    }

    /// <summary>
    /// Computes FIRST-k sequences for an expression — the set of token sequences
    /// (up to length k) that can appear at the start of a match. Each sequence is
    /// represented as a string of space-separated token identifiers.
    /// </summary>
    internal static HashSet<string> ComputeFirstKSequences(StormExpression expr, StormGrammar grammar, int k)
    {
        var prefixes = ComputePrefixes(expr, grammar, k, new HashSet<string>());
        var result = new HashSet<string>();
        foreach (var prefix in prefixes)
        {
            result.Add(string.Join(" ", prefix));
        }
        return result;
    }

    /// <summary>
    /// Computes all possible token-sequence prefixes of length up to k for an expression.
    /// Returns a set of lists, where each list is a sequence of token identifiers.
    /// </summary>
    private static List<List<string>> ComputePrefixes(
        StormExpression expr,
        StormGrammar grammar,
        int k,
        HashSet<string> visited)
    {
        if (k <= 0)
        {
            return new List<List<string>> { new List<string>() };
        }

        switch (expr)
        {
            case StormStringLiteralExpr strLit:
                return new List<List<string>> { new List<string> { "str:" + strLit.Value } };

            case StormCharClassExpr charClass:
                return new List<List<string>> { new List<string> { "cc:" + charClass.Text } };

            case StormReferenceExpr refExpr:
            {
                var resolved = grammar.LookupByName(refExpr.Name);
                if (resolved is StormTokenDecl or StormHiddenTokenDecl)
                {
                    return new List<List<string>> { new List<string> { "tok:" + refExpr.Name } };
                }
                if (resolved is StormFragmentDecl)
                {
                    return new List<List<string>> { new List<string> { "frag:" + refExpr.Name } };
                }
                if (resolved is StormRuleDecl ruleDecl && visited.Add(refExpr.Name))
                {
                    var result = ComputePrefixes(ruleDecl.Body, grammar, k, visited);
                    visited.Remove(refExpr.Name);
                    return result;
                }
                return new List<List<string>> { new List<string>() };
            }

            case StormQualifiedReferenceExpr qualRef:
            {
                var qualName = qualRef.Qualifier + "." + qualRef.Name;
                var qualResolved = grammar.LookupByName(qualName);
                if (qualResolved is StormTokenDecl or StormHiddenTokenDecl)
                {
                    return new List<List<string>> { new List<string> { "tok:" + qualName } };
                }
                if (qualResolved is StormFragmentDecl)
                {
                    return new List<List<string>> { new List<string> { "frag:" + qualName } };
                }
                if (qualResolved is StormRuleDecl qualRuleDecl && visited.Add(qualName))
                {
                    var result = ComputePrefixes(qualRuleDecl.Body, grammar, k, visited);
                    visited.Remove(qualName);
                    return result;
                }
                return new List<List<string>> { new List<string>() };
            }

            case StormSequenceExpr seqExpr:
                return ComputeSequencePrefixes(seqExpr.Elements, grammar, k, visited);

            case StormAlternationExpr altExpr:
            {
                var result = new List<List<string>>();
                foreach (var alt in altExpr.Alternatives)
                {
                    result.AddRange(ComputePrefixes(alt, grammar, k, visited));
                }
                return result;
            }

            case StormOptionalExpr optExpr:
            {
                var result = new List<List<string>> { new List<string>() };
                result.AddRange(ComputePrefixes(optExpr.Inner, grammar, k, visited));
                return result;
            }

            case StormZeroOrMoreExpr zomExpr:
            {
                var result = new List<List<string>> { new List<string>() };
                result.AddRange(ComputePrefixes(zomExpr.Inner, grammar, k, visited));
                return result;
            }

            case StormOneOrMoreExpr oomExpr:
                return ComputePrefixes(oomExpr.Inner, grammar, k, visited);

            case StormGroupExpr groupExpr:
                return ComputePrefixes(groupExpr.Inner, grammar, k, visited);

            case StormLabeledExpr labeledExpr:
                return ComputePrefixes(labeledExpr.Element, grammar, k, visited);

            case StormNamedAlternative namedAlt:
                return ComputePrefixes(namedAlt.Expression, grammar, k, visited);

            default:
                return new List<List<string>> { new List<string>() };
        }
    }

    /// <summary>
    /// Computes prefixes for a sequence of elements by concatenating the prefixes
    /// of each element in order, respecting nullable elements.
    /// </summary>
    private static List<List<string>> ComputeSequencePrefixes(
        IReadOnlyList<StormExpression> elements,
        StormGrammar grammar,
        int k,
        HashSet<string> visited)
    {
        // Start with a single empty prefix
        var current = new List<List<string>> { new List<string>() };

        foreach (var element in elements)
        {
            // If all current prefixes have reached length k, stop
            if (current.All(p => p.Count >= k))
            {
                break;
            }

            var next = new List<List<string>>();

            foreach (var prefix in current)
            {
                if (prefix.Count >= k)
                {
                    // This prefix is already at max length, keep it as-is
                    next.Add(prefix);
                    continue;
                }

                var remaining = k - prefix.Count;
                var elementPrefixes = ComputePrefixes(element, grammar, remaining, visited);

                foreach (var ep in elementPrefixes)
                {
                    var combined = new List<string>(prefix);
                    combined.AddRange(ep);
                    // Truncate to k
                    if (combined.Count > k)
                    {
                        combined = combined.Take(k).ToList();
                    }
                    next.Add(combined);
                }
            }

            current = next;

            // If the element is not nullable, we don't continue past it
            // (the nullable case is already handled by ComputePrefixes returning
            // empty-list prefixes for optional/zero-or-more elements)
        }

        return current;
    }

    private static void ComputeFirst(
        StormExpression expr,
        StormGrammar grammar,
        HashSet<string> result,
        HashSet<string> visited)
    {
        switch (expr)
        {
            case StormStringLiteralExpr strLit:
                _ = result.Add("str:" + strLit.Value);
                break;

            case StormCharClassExpr charClass:
                _ = result.Add("cc:" + charClass.Text);
                break;

            case StormReferenceExpr refExpr:
                var resolved = grammar.LookupByName(refExpr.Name);
                if (resolved is StormTokenDecl or StormHiddenTokenDecl)
                {
                    _ = result.Add("tok:" + refExpr.Name);
                }
                else if (resolved is StormFragmentDecl)
                {
                    _ = result.Add("frag:" + refExpr.Name);
                }
                else if (resolved is StormRuleDecl ruleDecl)
                {
                    if (visited.Add(refExpr.Name))
                    {
                        ComputeFirst(ruleDecl.Body, grammar, result, visited);
                    }
                }
                break;

            case StormQualifiedReferenceExpr qualRef:
                var qualName = qualRef.Qualifier + "." + qualRef.Name;
                var qualResolved = grammar.LookupByName(qualName);
                if (qualResolved is StormTokenDecl or StormHiddenTokenDecl)
                {
                    _ = result.Add("tok:" + qualName);
                }
                else if (qualResolved is StormFragmentDecl)
                {
                    _ = result.Add("frag:" + qualName);
                }
                else if (qualResolved is StormRuleDecl qualRuleDecl)
                {
                    if (visited.Add(qualName))
                    {
                        ComputeFirst(qualRuleDecl.Body, grammar, result, visited);
                    }
                }
                break;

            case StormSequenceExpr seqExpr:
                foreach (var element in seqExpr.Elements)
                {
                    ComputeFirst(element, grammar, result, visited);
                    if (!IsNullable(element))
                    {
                        break;
                    }
                }
                break;

            case StormAlternationExpr altExpr:
                foreach (var alt in altExpr.Alternatives)
                {
                    ComputeFirst(alt, grammar, result, visited);
                }
                break;

            case StormOptionalExpr optExpr:
                ComputeFirst(optExpr.Inner, grammar, result, visited);
                break;

            case StormZeroOrMoreExpr zomExpr:
                ComputeFirst(zomExpr.Inner, grammar, result, visited);
                break;

            case StormOneOrMoreExpr oomExpr:
                ComputeFirst(oomExpr.Inner, grammar, result, visited);
                break;

            case StormGroupExpr groupExpr:
                ComputeFirst(groupExpr.Inner, grammar, result, visited);
                break;

            case StormLabeledExpr labeledExpr:
                ComputeFirst(labeledExpr.Element, grammar, result, visited);
                break;

            case StormNamedAlternative namedAlt:
                ComputeFirst(namedAlt.Expression, grammar, result, visited);
                break;
        }
    }

    /// <summary>
    /// Determines if an expression can match the empty string.
    /// </summary>
    internal static bool IsNullable(StormExpression expr)
    {
        return expr switch
        {
            StormOptionalExpr or StormZeroOrMoreExpr => true,
            StormGroupExpr groupExpr => IsNullable(groupExpr.Inner),
            StormLabeledExpr labeledExpr => IsNullable(labeledExpr.Element),
            StormNamedAlternative namedAlt => IsNullable(namedAlt.Expression),
            StormSequenceExpr seqExpr => seqExpr.Elements.All(IsNullable),
            StormAlternationExpr altExpr => altExpr.Alternatives.Any(IsNullable),
            _ => false,
        };
    }
}
