using System.Collections.Generic;

namespace Storm.SourceGenerator.IR
{
    /// <summary>
    /// Base type for all lowered grammar expression nodes.
    /// </summary>
    public abstract class ExpressionIR
    {
    }

    /// <summary>
    /// A sequence of two or more sub-expressions.
    /// </summary>
    public sealed class SequenceIR : ExpressionIR
    {
        public IReadOnlyList<ExpressionIR> Elements { get; }

        public SequenceIR(IReadOnlyList<ExpressionIR> elements)
        {
            Elements = elements;
        }
    }

    /// <summary>
    /// An alternation of two or more sub-expressions.
    /// </summary>
    public sealed class AlternationIR : ExpressionIR
    {
        public IReadOnlyList<ExpressionIR> Alternatives { get; }

        public AlternationIR(IReadOnlyList<ExpressionIR> alternatives)
        {
            Alternatives = alternatives;
        }
    }

    /// <summary>
    /// An optional expression: <c>E?</c>
    /// </summary>
    public sealed class OptionalIR : ExpressionIR
    {
        public ExpressionIR Inner { get; }

        public OptionalIR(ExpressionIR inner)
        {
            Inner = inner;
        }
    }

    /// <summary>
    /// A zero-or-more repetition: <c>E*</c>
    /// </summary>
    public sealed class ZeroOrMoreIR : ExpressionIR
    {
        public ExpressionIR Inner { get; }

        public ZeroOrMoreIR(ExpressionIR inner)
        {
            Inner = inner;
        }
    }

    /// <summary>
    /// A one-or-more repetition: <c>E+</c>
    /// </summary>
    public sealed class OneOrMoreIR : ExpressionIR
    {
        public ExpressionIR Inner { get; }

        public OneOrMoreIR(ExpressionIR inner)
        {
            Inner = inner;
        }
    }

    /// <summary>
    /// A string literal: <c>"fn"</c>
    /// </summary>
    public sealed class StringLiteralIR : ExpressionIR
    {
        public string Value { get; }

        public StringLiteralIR(string value)
        {
            Value = value;
        }
    }

    /// <summary>
    /// A character class: <c>[a-zA-Z_]</c>
    /// </summary>
    public sealed class CharClassIR : ExpressionIR
    {
        public string Text { get; }

        public CharClassIR(string text)
        {
            Text = text;
        }
    }

    /// <summary>
    /// A reference to a <see cref="TokenIR"/> in the lowered grammar.
    /// </summary>
    public sealed class TokenRefIR : ExpressionIR
    {
        public TokenIR Token { get; }

        public TokenRefIR(TokenIR token)
        {
            Token = token;
        }
    }

    /// <summary>
    /// A reference to a <see cref="RuleIR"/> in the lowered grammar.
    /// </summary>
    public sealed class RuleRefIR : ExpressionIR
    {
        public RuleIR Rule { get; }

        public RuleRefIR(RuleIR rule)
        {
            Rule = rule;
        }
    }

    /// <summary>
    /// A fragment expression that has been inlined into a token expression.
    /// Wraps the fragment's body expression after inlining.
    /// </summary>
    public sealed class InlineFragmentIR : ExpressionIR
    {
        /// <summary>
        /// The name of the original fragment that was inlined.
        /// </summary>
        public string FragmentName { get; }

        /// <summary>
        /// The inlined body expression of the fragment.
        /// </summary>
        public ExpressionIR Body { get; }

        public InlineFragmentIR(string fragmentName, ExpressionIR body)
        {
            FragmentName = fragmentName;
            Body = body;
        }
    }
}
