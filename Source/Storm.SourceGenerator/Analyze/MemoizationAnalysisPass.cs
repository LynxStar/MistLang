using System.Collections.Generic;
using System.Linq;
using Storm.SourceGenerator.IR;

namespace Storm.SourceGenerator.Analyze;

/// <summary>
/// Selects rules for selective packrat memoization. A rule <c>R</c> is memoized when:
/// <list type="number">
///   <item><c>R</c> participates in a recursion cycle (<c>R</c> is reachable from itself
///   through rule references), AND</item>
///   <item>some rule on that cycle contains a backtracking construct (a multi-alternative
///   ordered choice, an optional <c>?</c>, or a repetition <c>*</c>/<c>+</c>), AND</item>
///   <item><c>R</c> is speculative-safe (a failing invocation cannot latch the parser
///   error flag or commit a net diagnostic change).</item>
/// </list>
/// This is a pure structural analysis over <see cref="RuleIR"/>/<see cref="ExpressionIR"/>
/// — no runtime profiling, no I/O, deterministic. Conservative by design: under-inclusion
/// is a performance tradeoff, never a correctness risk (a PEG rule's result at a position
/// is a pure function of position).
/// </summary>
/// <remarks>
/// Mirrors the structure of <c>LeftRecursionPass</c> (builds a rule-reference graph and
/// walks it) and <c>ReachabilityPass</c> (classifies per-rule structural properties).
/// Unlike <c>LeftRecursionPass</c>, which collects only the leftmost references for
/// left-recursion detection, this pass collects <em>all</em> rule references anywhere in a
/// rule body, because exponential re-entry happens at arbitrary positions.
/// </remarks>
public static class MemoizationAnalysisPass
{
    /// <summary>
    /// Computes which rules to memoize from the lowered grammar and returns a
    /// <see cref="MemoizationPlan"/> with dense, declaration-ordered ids. Returns
    /// <see cref="MemoizationPlan.Empty"/> when nothing qualifies.
    /// </summary>
    public static MemoizationPlan Run(GrammarIR grammar)
    {
        // Phase 1 — build the full rule-reference graph (all references, any depth).
        var graph = BuildReferenceGraph(grammar);

        // Phase 2 — find rules on a recursion cycle (reachable-from-self) via Tarjan SCC.
        var (cyclic, sccOf) = FindRulesOnCycle(graph);

        // Phase 3 — keep only cycles that cross a backtracking construct.
        // For each SCC, record whether ANY member rule contains a backtracking construct.
        var backtracks = new HashSet<string>();
        foreach (var rule in grammar.Rules)
        {
            if (HasBacktrackingConstruct(rule))
            {
                _ = backtracks.Add(rule.Name);
            }
        }

        var sccHasBacktracking = new HashSet<int>();
        foreach (var ruleName in cyclic)
        {
            if (backtracks.Contains(ruleName) && sccOf.TryGetValue(ruleName, out var sccIndex))
            {
                _ = sccHasBacktracking.Add(sccIndex);
            }
        }

        // Map rule name -> RuleIR for the speculative-safety filter.
        var rulesByName = new Dictionary<string, RuleIR>(grammar.Rules.Count);
        foreach (var rule in grammar.Rules)
        {
            rulesByName[rule.Name] = rule;
        }

        // Mark every rule whose SCC is cyclic AND crosses a backtracking construct AND
        // is speculative-safe (SR-2 filter). Iterate in DECLARATION ORDER so ids are
        // stable and deterministic across builds.
        var marked = new List<string>();
        foreach (var rule in grammar.Rules)
        {
            if (cyclic.Contains(rule.Name)
                && sccOf.TryGetValue(rule.Name, out var sccIndex)
                && sccHasBacktracking.Contains(sccIndex)
                && IsSpeculativeSafe(rule))
            {
                marked.Add(rule.Name);
            }
        }

        return marked.Count == 0 ? MemoizationPlan.Empty : new MemoizationPlan(marked);
    }

    // ── Phase 1: reference graph ─────────────────────────────────────────

    /// <summary>
    /// Builds the rule-reference graph: rule name -&gt; set of rule names referenced
    /// anywhere in its body, at any nesting depth.
    /// </summary>
    private static Dictionary<string, HashSet<string>> BuildReferenceGraph(GrammarIR grammar)
    {
        var graph = new Dictionary<string, HashSet<string>>(grammar.Rules.Count);
        foreach (var rule in grammar.Rules)
        {
            var refs = new HashSet<string>();
            foreach (var alternative in rule.Alternatives)
            {
                CollectRuleRefs(alternative.Expression, refs);
            }

            graph[rule.Name] = refs;
        }

        return graph;
    }

    /// <summary>
    /// Walks the expression tree and collects ALL <see cref="RuleRefIR"/> targets.
    /// Terminals (<see cref="TokenRefIR"/>, <see cref="StringLiteralIR"/>,
    /// <see cref="CharClassIR"/>, <see cref="RegexLiteralIR"/>) add no edges.
    /// </summary>
    private static void CollectRuleRefs(ExpressionIR expression, HashSet<string> refs)
    {
        switch (expression)
        {
            case RuleRefIR ruleRef:
                _ = refs.Add(ruleRef.Rule.Name);
                break;

            case SequenceIR sequence:
                foreach (var element in sequence.Elements)
                {
                    CollectRuleRefs(element, refs);
                }

                break;

            case AlternationIR alternation:
                foreach (var alternative in alternation.Alternatives)
                {
                    CollectRuleRefs(alternative, refs);
                }

                break;

            case OptionalIR optional:
                CollectRuleRefs(optional.Inner, refs);
                break;

            case ZeroOrMoreIR zeroOrMore:
                CollectRuleRefs(zeroOrMore.Inner, refs);
                break;

            case OneOrMoreIR oneOrMore:
                CollectRuleRefs(oneOrMore.Inner, refs);
                break;

            case InlineFragmentIR fragment:
                CollectRuleRefs(fragment.Body, refs);
                break;

            // TokenRefIR, StringLiteralIR, CharClassIR, RegexLiteralIR: terminals — no edges.
            default:
                break;
        }
    }

    // ── Phase 2: cycle detection (Tarjan SCC) ────────────────────────────

    /// <summary>
    /// Runs Tarjan's strongly-connected-component algorithm over the reference graph.
    /// A rule is on a cycle iff its SCC has more than one member or it has a self-edge.
    /// Returns the set of cyclic rule names and a map from rule name to its SCC index.
    /// </summary>
    private static (HashSet<string> Cyclic, Dictionary<string, int> SccOf) FindRulesOnCycle(
        Dictionary<string, HashSet<string>> graph)
    {
        var tarjan = new TarjanScc(graph);
        var components = tarjan.Run();

        var cyclic = new HashSet<string>();
        var sccOf = new Dictionary<string, int>();

        for (var i = 0; i < components.Count; i++)
        {
            var component = components[i];
            foreach (var ruleName in component)
            {
                sccOf[ruleName] = i;
            }

            if (component.Count > 1)
            {
                // Mutual recursion: every member is on a cycle.
                cyclic.UnionWith(component);
            }
            else
            {
                // Length-1 SCC: on a cycle only if it has a self-edge.
                var ruleName = component[0];
                if (graph.TryGetValue(ruleName, out var edges) && edges.Contains(ruleName))
                {
                    _ = cyclic.Add(ruleName);
                }
            }
        }

        return (cyclic, sccOf);
    }

    // ── Phase 3: backtracking detection ──────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> when the rule contains a backtracking construct:
    /// more than one alternative (ordered choice), OR any optional/repetition anywhere
    /// in its single alternative's body.
    /// </summary>
    private static bool HasBacktrackingConstruct(RuleIR rule)
    {
        // (a) ordered choice: more than one alternative.
        if (rule.Alternatives.Count > 1)
        {
            return true;
        }

        // (b) optional or repetition anywhere in the (single) alternative body.
        foreach (var alternative in rule.Alternatives)
        {
            if (ContainsOptionalOrRepetition(alternative.Expression))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the expression contains an
    /// <see cref="OptionalIR"/>, <see cref="ZeroOrMoreIR"/>, or <see cref="OneOrMoreIR"/>
    /// at any nesting depth.
    /// </summary>
    private static bool ContainsOptionalOrRepetition(ExpressionIR expression)
    {
        return expression switch
        {
            OptionalIR => true,
            ZeroOrMoreIR => true,
            OneOrMoreIR => true,
            SequenceIR sequence => sequence.Elements.Any(ContainsOptionalOrRepetition),
            AlternationIR alternation => alternation.Alternatives.Any(ContainsOptionalOrRepetition),
            InlineFragmentIR fragment => ContainsOptionalOrRepetition(fragment.Body),
            // RuleRefIR / TokenRefIR / StringLiteralIR / CharClassIR / RegexLiteralIR.
            _ => false,
        };
    }

    // ── SR-2: speculative-safety filter ──────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> when a candidate rule is speculative-safe — a
    /// failing invocation cannot latch the parser error flag or commit a net diagnostic
    /// change. Rules that are not speculative-safe are excluded from the plan so a cached
    /// failure can be replayed as a no-op (Requirement 2.4 / SR-2).
    /// </summary>
    private static bool IsSpeculativeSafe(RuleIR rule)
    {
        // Multi-alternative rules are bracketed per-alternative by the emitter: each
        // alternative saves/restores _pos and _hasError and truncates the diagnostic bag,
        // and the final fall-through restores to the entry state. They are always safe.
        if (rule.Alternatives.Count > 1)
        {
            return true;
        }

        // Single-alternative rules are safe iff their body contains no mandatory token
        // match that can latch _hasError. If unsure, exclude.
        return rule.Alternatives.Count == 1
            && !ContainsMandatoryTokenMatch(rule.Alternatives[0].Expression);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the expression contains a mandatory token
    /// match that the emitter would commit on (latching <c>_hasError</c> and emitting a
    /// hard-error diagnostic).
    /// </summary>
    /// <remarks>
    /// Grounded against <c>ParserEmitter</c>: rule bodies are emitted through
    /// <c>EmitAltBodyWithCheck</c> / <c>EmitFullExprParse</c> with <c>withCheck == true</c>,
    /// which matches <em>every</em> token in the body — mandatory or not — speculatively
    /// via <c>Peek()</c>/<c>Match</c> guarded by an <c>ok</c> flag. A non-match merely sets
    /// <c>ok = false</c> and the caller restores <c>_pos</c>; it never latches
    /// <c>_hasError</c> nor commits a diagnostic. The committing <c>Require(...)</c> path is
    /// reserved for emission contexts that are not used for rule bodies, so no rule-body
    /// expression contains a committing mandatory token match. This traversal therefore
    /// reports no mandatory match for the current emitter while remaining the explicit hook
    /// to exclude such rules should a future emitter introduce a committing construct.
    /// </remarks>
    private static bool ContainsMandatoryTokenMatch(ExpressionIR expression)
    {
        return expression switch
        {
            SequenceIR sequence => sequence.Elements.Any(ContainsMandatoryTokenMatch),
            AlternationIR alternation => alternation.Alternatives.Any(ContainsMandatoryTokenMatch),
            OptionalIR optional => ContainsMandatoryTokenMatch(optional.Inner),
            ZeroOrMoreIR zeroOrMore => ContainsMandatoryTokenMatch(zeroOrMore.Inner),
            OneOrMoreIR oneOrMore => ContainsMandatoryTokenMatch(oneOrMore.Inner),
            InlineFragmentIR fragment => ContainsMandatoryTokenMatch(fragment.Body),
            // TokenRefIR / StringLiteralIR / CharClassIR / RegexLiteralIR are emitted
            // speculatively (no committing Require) and RuleRefIR delegates to the callee;
            // none latches _hasError in the current emitter.
            _ => false,
        };
    }

    /// <summary>
    /// Iterative Tarjan strongly-connected-components over a string-keyed adjacency graph.
    /// Visits nodes in the graph's enumeration order (grammar declaration order) so the
    /// component partition is computed deterministically.
    /// </summary>
    private sealed class TarjanScc
    {
        private readonly Dictionary<string, HashSet<string>> _graph;
        private readonly Dictionary<string, int> _indexOf = new();
        private readonly Dictionary<string, int> _lowLink = new();
        private readonly HashSet<string> _onStack = new();
        private readonly Stack<string> _stack = new();
        private readonly List<List<string>> _components = new();
        private int _index;

        public TarjanScc(Dictionary<string, HashSet<string>> graph) => _graph = graph;

        public List<List<string>> Run()
        {
            foreach (var node in _graph.Keys)
            {
                if (!_indexOf.ContainsKey(node))
                {
                    StrongConnect(node);
                }
            }

            return _components;
        }

        private void StrongConnect(string root)
        {
            // Explicit work stack to avoid deep recursion on large grammars.
            // Each frame tracks the node and an enumerator over its outgoing edges.
            var work = new Stack<(string Node, IEnumerator<string> Edges)>();

            _indexOf[root] = _index;
            _lowLink[root] = _index;
            _index++;
            _stack.Push(root);
            _ = _onStack.Add(root);
            work.Push((root, GetEdges(root)));

            while (work.Count > 0)
            {
                var (node, edges) = work.Peek();

                var pushedChild = false;
                while (edges.MoveNext())
                {
                    var next = edges.Current;
                    if (!_indexOf.ContainsKey(next))
                    {
                        _indexOf[next] = _index;
                        _lowLink[next] = _index;
                        _index++;
                        _stack.Push(next);
                        _ = _onStack.Add(next);
                        work.Push((next, GetEdges(next)));
                        pushedChild = true;
                        break;
                    }

                    if (_onStack.Contains(next))
                    {
                        _lowLink[node] = System.Math.Min(_lowLink[node], _indexOf[next]);
                    }
                }

                if (pushedChild)
                {
                    continue;
                }

                // All edges processed for `node`: finalize it.
                _ = work.Pop();

                if (work.Count > 0)
                {
                    var parent = work.Peek().Node;
                    _lowLink[parent] = System.Math.Min(_lowLink[parent], _lowLink[node]);
                }

                if (_lowLink[node] == _indexOf[node])
                {
                    var component = new List<string>();
                    string member;
                    do
                    {
                        member = _stack.Pop();
                        _ = _onStack.Remove(member);
                        component.Add(member);
                    }
                    while (member != node);

                    _components.Add(component);
                }
            }
        }

        private IEnumerator<string> GetEdges(string node)
        {
            return _graph.TryGetValue(node, out var edges)
                ? edges.GetEnumerator()
                : Enumerable.Empty<string>().GetEnumerator();
        }
    }
}
