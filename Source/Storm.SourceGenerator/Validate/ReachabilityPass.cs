using System.Collections.Generic;
using System.Linq;
using Storm.SourceGenerator.Diagnostics;
using Storm.SourceGenerator.Frontend;
using Storm.SourceGenerator.Resolve;

namespace Storm.SourceGenerator.Validate
{
    /// <summary>
    /// Checks each rule's alternatives for unreachable alternatives under PEG ordered-choice
    /// semantics. Uses FIRST set analysis: if alternative k's FIRST set is a subset of the
    /// union of alternatives 1..k-1's FIRST sets, alternative k is unreachable.
    /// Emits STORM_UNREACHABLE_ALTERNATIVE warnings (not errors).
    /// </summary>
    public static class ReachabilityPass
    {
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
            // Only check rules with alternation at the top level
            var alternatives = GetTopLevelAlternatives(rule.Body);
            if (alternatives.Count <= 1)
                return;

            // Compute FIRST sets for each alternative
            var firstSets = new List<HashSet<string>>();
            foreach (var alt in alternatives)
            {
                firstSets.Add(ComputeFirstSet(alt, grammar));
            }

            // Check each alternative k against the union of 1..k-1
            var unionSoFar = new HashSet<string>();
            for (int i = 0; i < alternatives.Count; i++)
            {
                if (i > 0 && firstSets[i].Count > 0 && firstSets[i].IsSubsetOf(unionSoFar))
                {
                    diagnostics.Add(new StormDiagnostic(
                        StormDiagnosticIds.STORM_UNREACHABLE_ALTERNATIVE,
                        StormDiagnosticSeverity.Warning,
                        $"Alternative {i + 1} in rule '{rule.Name}' is unreachable; its FIRST set is covered by earlier alternatives",
                        null,
                        rule.Line,
                        rule.Column));
                }

                foreach (var token in firstSets[i])
                    unionSoFar.Add(token);
            }
        }

        /// <summary>
        /// Extracts the top-level alternatives from a rule body.
        /// If the body is an alternation, returns its alternatives.
        /// Otherwise returns a single-element list.
        /// </summary>
        internal static IReadOnlyList<StormExpression> GetTopLevelAlternatives(StormExpression body)
        {
            if (body is StormAlternationExpr altExpr)
                return altExpr.Alternatives;
            return new[] { body };
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

        private static void ComputeFirst(
            StormExpression expr,
            StormGrammar grammar,
            HashSet<string> result,
            HashSet<string> visited)
        {
            switch (expr)
            {
                case StormStringLiteralExpr strLit:
                    result.Add("str:" + strLit.Value);
                    break;

                case StormCharClassExpr charClass:
                    result.Add("cc:" + charClass.Text);
                    break;

                case StormReferenceExpr refExpr:
                    var resolved = grammar.LookupByName(refExpr.Name);
                    if (resolved is StormTokenDecl || resolved is StormHiddenTokenDecl)
                    {
                        result.Add("tok:" + refExpr.Name);
                    }
                    else if (resolved is StormFragmentDecl)
                    {
                        result.Add("frag:" + refExpr.Name);
                    }
                    else if (resolved is StormRuleDecl ruleDecl)
                    {
                        // Expand through the rule's body to get its FIRST set
                        if (visited.Add(refExpr.Name))
                        {
                            ComputeFirst(ruleDecl.Body, grammar, result, visited);
                        }
                    }
                    break;

                case StormQualifiedReferenceExpr qualRef:
                    var qualName = qualRef.Qualifier + "." + qualRef.Name;
                    var qualResolved = grammar.LookupByName(qualName);
                    if (qualResolved is StormTokenDecl || qualResolved is StormHiddenTokenDecl)
                    {
                        result.Add("tok:" + qualName);
                    }
                    else if (qualResolved is StormFragmentDecl)
                    {
                        result.Add("frag:" + qualName);
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
                            break;
                    }
                    break;

                case StormAlternationExpr altExpr:
                    foreach (var alt in altExpr.Alternatives)
                        ComputeFirst(alt, grammar, result, visited);
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
        private static bool IsNullable(StormExpression expr)
        {
            switch (expr)
            {
                case StormOptionalExpr _:
                case StormZeroOrMoreExpr _:
                    return true;

                case StormGroupExpr groupExpr:
                    return IsNullable(groupExpr.Inner);

                case StormLabeledExpr labeledExpr:
                    return IsNullable(labeledExpr.Element);

                case StormNamedAlternative namedAlt:
                    return IsNullable(namedAlt.Expression);

                case StormSequenceExpr seqExpr:
                    return seqExpr.Elements.All(e => IsNullable(e));

                case StormAlternationExpr altExpr:
                    return altExpr.Alternatives.Any(a => IsNullable(a));

                default:
                    return false;
            }
        }
    }
}
