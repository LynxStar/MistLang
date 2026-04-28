using System;
using System.Collections.Generic;
using System.Linq;
using Storm.SourceGenerator.Diagnostics;
using Storm.SourceGenerator.Frontend;
using Storm.SourceGenerator.Resolve;

namespace Storm.SourceGenerator.IR;

/// <summary>
/// Lowers a validated <see cref="StormGrammar"/> (effective grammar) into a
/// <see cref="GrammarIR"/>. The lowering performs:
/// <list type="bullet">
///   <item>Fragment inlining — replaces fragment references in token expressions
///         with the fragment's body expression.</item>
///   <item>Synthetic token creation — materializes anonymous string literals
///         appearing in rule expressions as synthetic <see cref="TokenIR"/> entries.</item>
///   <item>Declaration-order index assignment for tokens.</item>
///   <item>Reference resolution — all references become IR-level pointers
///         (<see cref="TokenRefIR"/> / <see cref="RuleRefIR"/>).</item>
/// </list>
/// </summary>
public static class GrammarIRLowering
{
    /// <summary>
    /// Result of the lowering pass.
    /// </summary>
    public sealed class LoweringResult
    {
        public GrammarIR? Grammar { get; }
        public IReadOnlyList<StormDiagnostic> Diagnostics { get; }
        public bool Succeeded => Grammar != null && !Diagnostics.Any(d => d.Severity == StormDiagnosticSeverity.Error);

        public LoweringResult(GrammarIR? grammar, IReadOnlyList<StormDiagnostic> diagnostics)
        {
            Grammar = grammar;
            Diagnostics = diagnostics;
        }
    }

    /// <summary>
    /// Lowers the given effective grammar to a <see cref="GrammarIR"/>.
    /// </summary>
    public static LoweringResult Lower(StormGrammar grammar)
    {
        if(grammar == null)
        {
            throw new ArgumentNullException(nameof(grammar));
        }

        var diagnostics = new List<StormDiagnostic>();

        // ---------------------------------------------------------------
        // 1. Collect fragments for inlining
        // ---------------------------------------------------------------
        var fragmentsByName = new Dictionary<string, StormDeclaration>(StringComparer.Ordinal);
        foreach(var decl in grammar.Declarations)
        {
            if(decl is StormFragmentDecl)
            {
                fragmentsByName[decl.Name] = decl;
            }
        }

        // Also check namespaced declarations for fragments
        foreach(var kvp in grammar.NamespacedDeclarations)
        {
            if(kvp.Value is StormFragmentDecl)
            {
                fragmentsByName[kvp.Key] = kvp.Value;
            }
        }

        // ---------------------------------------------------------------
        // 2. Build TokenIR entries (explicit tokens with fragments inlined)
        // ---------------------------------------------------------------
        var tokenIRs = new List<TokenIR>();
        var tokensByName = new Dictionary<string, TokenIR>(StringComparer.Ordinal);
        var tokenIndex = 0;

        foreach(var decl in grammar.Declarations)
        {
            if(decl is StormTokenDecl or StormHiddenTokenDecl)
            {
                var isHidden = decl is StormHiddenTokenDecl;
                var inlinedExpr = InlineFragments(decl.Body, fragmentsByName, new HashSet<string>(StringComparer.Ordinal), diagnostics);
                var tokenIR = new TokenIR(decl.Name, inlinedExpr, isHidden, tokenIndex);
                tokenIRs.Add(tokenIR);
                tokensByName[decl.Name] = tokenIR;
                tokenIndex++;
            }
        }

        // Also process namespaced token declarations
        foreach(var kvp in grammar.NamespacedDeclarations)
        {
            if(kvp.Value is StormTokenDecl or StormHiddenTokenDecl)
            {
                if(!tokensByName.ContainsKey(kvp.Key))
                {
                    var isHidden = kvp.Value is StormHiddenTokenDecl;
                    var inlinedExpr = InlineFragments(kvp.Value.Body, fragmentsByName, new HashSet<string>(StringComparer.Ordinal), diagnostics);
                    var tokenIR = new TokenIR(kvp.Key, inlinedExpr, isHidden, tokenIndex);
                    tokenIRs.Add(tokenIR);
                    tokensByName[kvp.Key] = tokenIR;
                    tokenIndex++;
                }
            }
        }

        // ---------------------------------------------------------------
        // 3. Collect rule declarations (pre-pass to build RuleIR shells
        //    so we can resolve rule references)
        // ---------------------------------------------------------------
        var ruleShells = new Dictionary<string, RuleIR>(StringComparer.Ordinal);
        var ruleOrder = new List<(string Name, StormDeclaration Decl)>();

        foreach(var decl in grammar.Declarations)
        {
            if(decl is StormRuleDecl)
            {
                // Create a shell with empty alternatives — we'll fill them in step 5
                var shell = new RuleIR(decl.Name, new List<RuleAlternativeIR>());
                ruleShells[decl.Name] = shell;
                ruleOrder.Add((decl.Name, decl));
            }
        }

        foreach(var kvp in grammar.NamespacedDeclarations)
        {
            if(kvp.Value is StormRuleDecl && !ruleShells.ContainsKey(kvp.Key))
            {
                var shell = new RuleIR(kvp.Key, new List<RuleAlternativeIR>());
                ruleShells[kvp.Key] = shell;
                ruleOrder.Add((kvp.Key, kvp.Value));
            }
        }

        // ---------------------------------------------------------------
        // 4. First pass over rule expressions: collect anonymous string
        //    literals and create synthetic tokens for them
        // ---------------------------------------------------------------
        var syntheticTokens = new Dictionary<string, TokenIR>(StringComparer.Ordinal);

        foreach(var (name, decl) in ruleOrder)
        {
            CollectAnonymousStringLiterals(decl.Body, syntheticTokens, tokensByName, ref tokenIndex);
        }

        // Append synthetic tokens to the token list
        foreach(var synth in syntheticTokens.Values)
        {
            tokenIRs.Add(synth);
            tokensByName[synth.Name] = synth;
        }

        // ---------------------------------------------------------------
        // 5. Lower rule expressions with all references resolved.
        //    IMPORTANT: We populate the existing shell objects in-place
        //    (by adding to their mutable List<RuleAlternativeIR>) so that
        //    RuleRefIR nodes created during lowering — which capture a
        //    reference to the shell — see the final alternatives.
        //    Creating new RuleIR objects would leave stale references
        //    with empty alternative lists, breaking FIRST-set computation.
        // ---------------------------------------------------------------
        var ruleIRs = new List<RuleIR>();

        foreach(var (name, decl) in ruleOrder)
        {
            var alternatives = LowerRuleAlternatives(decl.Body, tokensByName, ruleShells, syntheticTokens, diagnostics);
            var shell = ruleShells[name];
            // The shell was created with a mutable List<RuleAlternativeIR>;
            // cast back and populate it so all existing RuleRefIR pointers
            // see the real alternatives.
            var mutableAlts = (List<RuleAlternativeIR>)shell.Alternatives;
            mutableAlts.AddRange(alternatives);
            ruleIRs.Add(shell);
        }

        if(diagnostics.Any(d => d.Severity == StormDiagnosticSeverity.Error))
        {
            return new LoweringResult(null, diagnostics);
        }

        var grammarIR = new GrammarIR(tokenIRs, ruleIRs);
        return new LoweringResult(grammarIR, diagnostics);
    }

    // ---------------------------------------------------------------
    //  Fragment inlining
    // ---------------------------------------------------------------

    /// <summary>
    /// Recursively inlines fragment references in a token expression.
    /// </summary>
    private static ExpressionIR InlineFragments(
        StormExpression expr,
        Dictionary<string, StormDeclaration> fragmentsByName,
        HashSet<string> inliningStack,
        List<StormDiagnostic> diagnostics)
    {
        switch(expr)
        {
            case StormReferenceExpr refExpr:
                if(fragmentsByName.TryGetValue(refExpr.Name, out var fragment))
                {
                    if(!inliningStack.Add(refExpr.Name))
                    {
                        diagnostics.Add(new StormDiagnostic(
                            StormDiagnosticIds.STORM_UNSUPPORTED_CONSTRUCT,
                            StormDiagnosticSeverity.Error,
                            $"Circular fragment reference detected: '{refExpr.Name}'",
                            null, 0, 0));
                        return new StringLiteralIR(""); // placeholder
                    }

                    var inlined = InlineFragments(fragment.Body, fragmentsByName, inliningStack, diagnostics);
                    _ = inliningStack.Remove(refExpr.Name);
                    return new InlineFragmentIR(refExpr.Name, inlined);
                }
                // Not a fragment — leave as-is for now; this is a token-to-token reference
                // which will be resolved later. For token expressions, non-fragment references
                // are references to other tokens.
                return new StringLiteralIR(refExpr.Name); // placeholder — shouldn't happen in valid grammars

            case StormQualifiedReferenceExpr qualRef:
                var qualName = qualRef.Qualifier + "." + qualRef.Name;
                if(fragmentsByName.TryGetValue(qualName, out var qualFragment))
                {
                    if(!inliningStack.Add(qualName))
                    {
                        diagnostics.Add(new StormDiagnostic(
                            StormDiagnosticIds.STORM_UNSUPPORTED_CONSTRUCT,
                            StormDiagnosticSeverity.Error,
                            $"Circular fragment reference detected: '{qualName}'",
                            null, 0, 0));
                        return new StringLiteralIR("");
                    }

                    var inlined = InlineFragments(qualFragment.Body, fragmentsByName, inliningStack, diagnostics);
                    _ = inliningStack.Remove(qualName);
                    return new InlineFragmentIR(qualName, inlined);
                }

                return new StringLiteralIR(qualName); // placeholder

            case StormSequenceExpr seqExpr:
                var seqElements = new List<ExpressionIR>();
                foreach(var element in seqExpr.Elements)
                {
                    seqElements.Add(InlineFragments(element, fragmentsByName, inliningStack, diagnostics));
                }

                return new SequenceIR(seqElements);

            case StormAlternationExpr altExpr:
                var altElements = new List<ExpressionIR>();
                foreach(var alt in altExpr.Alternatives)
                {
                    altElements.Add(InlineFragments(alt, fragmentsByName, inliningStack, diagnostics));
                }

                return new AlternationIR(altElements);

            case StormOptionalExpr optExpr:
                return new OptionalIR(InlineFragments(optExpr.Inner, fragmentsByName, inliningStack, diagnostics));

            case StormZeroOrMoreExpr zomExpr:
                return new ZeroOrMoreIR(InlineFragments(zomExpr.Inner, fragmentsByName, inliningStack, diagnostics));

            case StormOneOrMoreExpr oomExpr:
                return new OneOrMoreIR(InlineFragments(oomExpr.Inner, fragmentsByName, inliningStack, diagnostics));

            case StormGroupExpr groupExpr:
                return InlineFragments(groupExpr.Inner, fragmentsByName, inliningStack, diagnostics);

            case StormStringLiteralExpr strExpr:
                return new StringLiteralIR(strExpr.Value);

            case StormCharClassExpr ccExpr:
                return new CharClassIR(ccExpr.Text);

            case StormRegexLiteralExpr regexExpr:
                return new RegexLiteralIR(regexExpr.Pattern);

            case StormLabeledExpr labeledExpr:
                // Labels don't appear in token expressions normally, but handle gracefully
                return InlineFragments(labeledExpr.Element, fragmentsByName, inliningStack, diagnostics);

            case StormNamedAlternative namedAlt:
                // Named alternatives don't appear in token expressions, but handle gracefully
                return InlineFragments(namedAlt.Expression, fragmentsByName, inliningStack, diagnostics);

            default:
                diagnostics.Add(new StormDiagnostic(
                    StormDiagnosticIds.STORM_UNSUPPORTED_CONSTRUCT,
                    StormDiagnosticSeverity.Error,
                    $"Unsupported expression type in token expression: {expr.GetType().Name}",
                    null, 0, 0));
                return new StringLiteralIR("");
        }
    }

    // ---------------------------------------------------------------
    //  Anonymous string literal collection
    // ---------------------------------------------------------------

    /// <summary>
    /// Walks a rule expression and collects all anonymous string literals,
    /// creating synthetic <see cref="TokenIR"/> entries for each unique literal.
    /// </summary>
    private static void CollectAnonymousStringLiterals(
        StormExpression expr,
        Dictionary<string, TokenIR> syntheticTokens,
        Dictionary<string, TokenIR> existingTokens,
        ref int tokenIndex)
    {
        switch(expr)
        {
            case StormStringLiteralExpr strExpr:
                var synthName = SyntheticTokenName(strExpr.Value);
                if(!syntheticTokens.ContainsKey(synthName) && !existingTokens.ContainsKey(synthName))
                {
                    var synthToken = new TokenIR(
                        synthName,
                        new StringLiteralIR(strExpr.Value),
                        isHidden: false,
                        declarationIndex: tokenIndex,
                        isSynthetic: true);
                    syntheticTokens[synthName] = synthToken;
                    tokenIndex++;
                }

                break;

            case StormSequenceExpr seqExpr:
                foreach(var element in seqExpr.Elements)
                {
                    CollectAnonymousStringLiterals(element, syntheticTokens, existingTokens, ref tokenIndex);
                }

                break;

            case StormAlternationExpr altExpr:
                foreach(var alt in altExpr.Alternatives)
                {
                    CollectAnonymousStringLiterals(alt, syntheticTokens, existingTokens, ref tokenIndex);
                }

                break;

            case StormOptionalExpr optExpr:
                CollectAnonymousStringLiterals(optExpr.Inner, syntheticTokens, existingTokens, ref tokenIndex);
                break;

            case StormZeroOrMoreExpr zomExpr:
                CollectAnonymousStringLiterals(zomExpr.Inner, syntheticTokens, existingTokens, ref tokenIndex);
                break;

            case StormOneOrMoreExpr oomExpr:
                CollectAnonymousStringLiterals(oomExpr.Inner, syntheticTokens, existingTokens, ref tokenIndex);
                break;

            case StormGroupExpr groupExpr:
                CollectAnonymousStringLiterals(groupExpr.Inner, syntheticTokens, existingTokens, ref tokenIndex);
                break;

            case StormLabeledExpr labeledExpr:
                CollectAnonymousStringLiterals(labeledExpr.Element, syntheticTokens, existingTokens, ref tokenIndex);
                break;

            case StormNamedAlternative namedAlt:
                CollectAnonymousStringLiterals(namedAlt.Expression, syntheticTokens, existingTokens, ref tokenIndex);
                break;

            // References, char classes — no string literals to collect
            default:
                break;
        }
    }

    /// <summary>
    /// Generates a synthetic token name for an anonymous string literal.
    /// E.g., <c>"+"</c> → <c>__SYNTH_PLUS</c>, <c>"fn"</c> → <c>__SYNTH_fn</c>.
    /// </summary>
    internal static string SyntheticTokenName(string literalValue)
    {
        // Build a safe identifier from the literal value
        var safeName = new System.Text.StringBuilder("__SYNTH_");
        foreach(var c in literalValue)
        {
            if(char.IsLetterOrDigit(c) || c == '_')
            {
                _ = safeName.Append(c);
            }
            else
            {
                // Map common operators to readable names
                _ = c switch
                {
                    '+' => safeName.Append("PLUS"),
                    '-' => safeName.Append("MINUS"),
                    '*' => safeName.Append("STAR"),
                    '/' => safeName.Append("SLASH"),
                    '=' => safeName.Append("EQ"),
                    '<' => safeName.Append("LT"),
                    '>' => safeName.Append("GT"),
                    '!' => safeName.Append("BANG"),
                    '&' => safeName.Append("AMP"),
                    '|' => safeName.Append("PIPE"),
                    '(' => safeName.Append("LPAREN"),
                    ')' => safeName.Append("RPAREN"),
                    '{' => safeName.Append("LBRACE"),
                    '}' => safeName.Append("RBRACE"),
                    '[' => safeName.Append("LBRACKET"),
                    ']' => safeName.Append("RBRACKET"),
                    '.' => safeName.Append("DOT"),
                    ',' => safeName.Append("COMMA"),
                    ':' => safeName.Append("COLON"),
                    ';' => safeName.Append("SEMI"),
                    '?' => safeName.Append("QUESTION"),
                    '@' => safeName.Append("AT"),
                    '#' => safeName.Append("HASH"),
                    '%' => safeName.Append("PERCENT"),
                    '^' => safeName.Append("CARET"),
                    '~' => safeName.Append("TILDE"),
                    _ => safeName.Append($"U{(int)c:X4}"),
                };
            }
        }

        return safeName.ToString();
    }

    // ---------------------------------------------------------------
    //  Rule expression lowering
    // ---------------------------------------------------------------

    /// <summary>
    /// Lowers a rule's body expression into a list of <see cref="RuleAlternativeIR"/>.
    /// </summary>
    private static IReadOnlyList<RuleAlternativeIR> LowerRuleAlternatives(
        StormExpression body,
        Dictionary<string, TokenIR> tokensByName,
        Dictionary<string, RuleIR> rulesByName,
        Dictionary<string, TokenIR> syntheticTokens,
        List<StormDiagnostic> diagnostics)
    {
        var alternatives = new List<RuleAlternativeIR>();

        // If the body is an alternation, each alternative becomes a RuleAlternativeIR.
        // Otherwise, the entire body is a single alternative.
        if(body is StormAlternationExpr altExpr)
        {
            foreach(var alt in altExpr.Alternatives)
            {
                alternatives.Add(LowerSingleAlternative(alt, tokensByName, rulesByName, syntheticTokens, diagnostics));
            }
        }
        else
        {
            alternatives.Add(LowerSingleAlternative(body, tokensByName, rulesByName, syntheticTokens, diagnostics));
        }

        return alternatives;
    }

    /// <summary>
    /// Lowers a single alternative expression, extracting any <c>as Name</c>
    /// label and field labels.
    /// </summary>
    private static RuleAlternativeIR LowerSingleAlternative(
        StormExpression expr,
        Dictionary<string, TokenIR> tokensByName,
        Dictionary<string, RuleIR> rulesByName,
        Dictionary<string, TokenIR> syntheticTokens,
        List<StormDiagnostic> diagnostics)
    {
        string? altName = null;
        var bodyExpr = expr;

        // Check if this alternative has an `as Name` wrapper
        if(expr is StormNamedAlternative namedAlt)
        {
            altName = namedAlt.AlternativeName;
            bodyExpr = namedAlt.Expression;
        }

        // Collect field labels
        var fieldLabels = new List<FieldLabelIR>();
        var loweredExpr = LowerRuleExpression(bodyExpr, tokensByName, rulesByName, syntheticTokens, fieldLabels, diagnostics);

        return new RuleAlternativeIR(altName, fieldLabels, loweredExpr);
    }

    /// <summary>
    /// Recursively lowers a rule expression, resolving references to
    /// IR-level pointers and replacing anonymous string literals with
    /// synthetic token references.
    /// </summary>
    private static ExpressionIR LowerRuleExpression(
        StormExpression expr,
        Dictionary<string, TokenIR> tokensByName,
        Dictionary<string, RuleIR> rulesByName,
        Dictionary<string, TokenIR> syntheticTokens,
        List<FieldLabelIR> fieldLabels,
        List<StormDiagnostic> diagnostics)
    {
        switch(expr)
        {
            case StormReferenceExpr refExpr:
                // Resolve to token or rule
                if(tokensByName.TryGetValue(refExpr.Name, out var tokenRef))
                {
                    return new TokenRefIR(tokenRef);
                }

                if(rulesByName.TryGetValue(refExpr.Name, out var ruleRef))
                {
                    return new RuleRefIR(ruleRef);
                }

                diagnostics.Add(new StormDiagnostic(
                    StormDiagnosticIds.STORM_UNDEFINED_REFERENCE,
                    StormDiagnosticSeverity.Error,
                    $"Cannot resolve reference '{refExpr.Name}' to a token or rule in IR lowering",
                    null, 0, 0));
                return new StringLiteralIR("");

            case StormQualifiedReferenceExpr qualRef:
                var qualName = qualRef.Qualifier + "." + qualRef.Name;
                if(tokensByName.TryGetValue(qualName, out var qualTokenRef))
                {
                    return new TokenRefIR(qualTokenRef);
                }

                if(rulesByName.TryGetValue(qualName, out var qualRuleRef))
                {
                    return new RuleRefIR(qualRuleRef);
                }

                diagnostics.Add(new StormDiagnostic(
                    StormDiagnosticIds.STORM_UNDEFINED_REFERENCE,
                    StormDiagnosticSeverity.Error,
                    $"Cannot resolve qualified reference '{qualName}' to a token or rule in IR lowering",
                    null, 0, 0));
                return new StringLiteralIR("");

            case StormStringLiteralExpr strExpr:
                // Replace with a reference to the synthetic token
                var synthName = SyntheticTokenName(strExpr.Value);
                if(syntheticTokens.TryGetValue(synthName, out var synthToken))
                {
                    return new TokenRefIR(synthToken);
                }

                if(tokensByName.TryGetValue(synthName, out var existingToken))
                {
                    return new TokenRefIR(existingToken);
                }
                // Should not happen if CollectAnonymousStringLiterals ran correctly
                diagnostics.Add(new StormDiagnostic(
                    StormDiagnosticIds.STORM_UNSUPPORTED_CONSTRUCT,
                    StormDiagnosticSeverity.Error,
                    $"Synthetic token for string literal '{strExpr.Value}' not found",
                    null, 0, 0));
                return new StringLiteralIR(strExpr.Value);

            case StormSequenceExpr seqExpr:
                var seqElements = new List<ExpressionIR>();
                foreach(var element in seqExpr.Elements)
                {
                    seqElements.Add(LowerRuleExpression(element, tokensByName, rulesByName, syntheticTokens, fieldLabels, diagnostics));
                }

                return new SequenceIR(seqElements);

            case StormAlternationExpr altExpr:
                var altElements = new List<ExpressionIR>();
                foreach(var alt in altExpr.Alternatives)
                {
                    altElements.Add(LowerRuleExpression(alt, tokensByName, rulesByName, syntheticTokens, fieldLabels, diagnostics));
                }

                return new AlternationIR(altElements);

            case StormOptionalExpr optExpr:
                return new OptionalIR(LowerRuleExpression(optExpr.Inner, tokensByName, rulesByName, syntheticTokens, fieldLabels, diagnostics));

            case StormZeroOrMoreExpr zomExpr:
                return new ZeroOrMoreIR(LowerRuleExpression(zomExpr.Inner, tokensByName, rulesByName, syntheticTokens, fieldLabels, diagnostics));

            case StormOneOrMoreExpr oomExpr:
                return new OneOrMoreIR(LowerRuleExpression(oomExpr.Inner, tokensByName, rulesByName, syntheticTokens, fieldLabels, diagnostics));

            case StormGroupExpr groupExpr:
                return LowerRuleExpression(groupExpr.Inner, tokensByName, rulesByName, syntheticTokens, fieldLabels, diagnostics);

            case StormLabeledExpr labeledExpr:
                var labeledInner = LowerRuleExpression(labeledExpr.Element, tokensByName, rulesByName, syntheticTokens, fieldLabels, diagnostics);
                fieldLabels.Add(new FieldLabelIR(labeledExpr.Label, labeledInner));
                return labeledInner;

            case StormNamedAlternative namedAlt:
                // Nested named alternatives shouldn't normally occur at this level,
                // but handle gracefully
                return LowerRuleExpression(namedAlt.Expression, tokensByName, rulesByName, syntheticTokens, fieldLabels, diagnostics);

            case StormCharClassExpr ccExpr:
                // Char classes shouldn't appear in rule expressions, but handle gracefully
                return new CharClassIR(ccExpr.Text);

            case StormRegexLiteralExpr regexExpr:
                return new RegexLiteralIR(regexExpr.Pattern);

            default:
                diagnostics.Add(new StormDiagnostic(
                    StormDiagnosticIds.STORM_UNSUPPORTED_CONSTRUCT,
                    StormDiagnosticSeverity.Error,
                    $"Unsupported expression type in rule expression: {expr.GetType().Name}",
                    null, 0, 0));
                return new StringLiteralIR("");
        }
    }
}
