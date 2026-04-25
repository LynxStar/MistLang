using System.Collections.Generic;

namespace Storm.SourceGenerator.IR
{
    /// <summary>
    /// The complete lowered grammar intermediate representation.
    /// Holds ordered lists of <see cref="TokenIR"/> and <see cref="RuleIR"/>
    /// with all fragment references inlined, anonymous string literals
    /// materialized as synthetic tokens, and all references resolved to
    /// IR-level pointers.
    /// </summary>
    public sealed class GrammarIR
    {
        /// <summary>
        /// All tokens in declaration order, including synthetic tokens
        /// created for anonymous string literals in rule expressions.
        /// </summary>
        public IReadOnlyList<TokenIR> Tokens { get; }

        /// <summary>
        /// All rules in declaration order.
        /// </summary>
        public IReadOnlyList<RuleIR> Rules { get; }

        public GrammarIR(IReadOnlyList<TokenIR> tokens, IReadOnlyList<RuleIR> rules)
        {
            Tokens = tokens;
            Rules = rules;
        }
    }
}
