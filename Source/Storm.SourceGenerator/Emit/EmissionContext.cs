using Storm.SourceGenerator.IR;

namespace Storm.SourceGenerator.Emit
{
    /// <summary>
    /// Holds shared state for all emitters: the consumer namespace, grammar name,
    /// and the lowered <see cref="GrammarIR"/>.
    /// </summary>
    public sealed class EmissionContext
    {
        /// <summary>
        /// The namespace under which generated code is placed.
        /// For example, "Mist.Generated" for the Mist compiler.
        /// </summary>
        public string ConsumerNamespace { get; }

        /// <summary>
        /// The grammar name, typically derived from the root Storm file name.
        /// Used to name the generated lexer and parser classes (e.g., "Mist" → "MistLexer", "MistParser").
        /// </summary>
        public string GrammarName { get; }

        /// <summary>
        /// The lowered grammar IR that all emitters read from.
        /// </summary>
        public GrammarIR Grammar { get; }

        public EmissionContext(string consumerNamespace, string grammarName, GrammarIR grammar)
        {
            ConsumerNamespace = consumerNamespace;
            GrammarName = grammarName;
            Grammar = grammar;
        }
    }
}
