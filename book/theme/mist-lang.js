// Mist language definition for highlight.js 11.x
hljs.registerLanguage("mist", function(hljs) {
  var MODIFIERS = 'public private static readonly mutable owned frozen borrowed ref implicit explicit intrinsic async linear affine relevant';
  var RESERVED_TYPES = 'int uint long ulong short ushort byte sbyte float double decimal bool char string regex void unknown var func';
  var KEYWORDS = 'namespace using class struct abstract enum extension type where ' +
    'if else while for foreach in return new break is and or not ' +
    'switch when default with as this base null true false ' +
    'operator get set init field await';

  return {
    name: 'Mist',
    aliases: ['mist'],
    case_insensitive: false,
    keywords: {
      keyword: KEYWORDS,
      type: RESERVED_TYPES,
      'variable.language': MODIFIERS
    },
    contains: [
      // Attributes: [Name], [Name(args)], [Name<T>]
      {
        className: 'meta',
        begin: /^\s*\[/,
        end: /\]/,
        contains: [
          // Attribute name (possibly dotted)
          { className: 'keyword', begin: /[A-Za-z_][A-Za-z0-9_.]*/, relevance: 0 },
          // Type arguments <T>
          {
            begin: /</,
            end: />/,
            contains: [
              { className: 'type', begin: /[A-Za-z_][A-Za-z0-9_.]*/, relevance: 0 }
            ]
          },
          // Argument list
          {
            begin: /\(/,
            end: /\)/,
            contains: [
              // Named arguments: Name = value
              { className: 'attr', begin: /[A-Za-z_][A-Za-z0-9_]*(?=\s*=)/, relevance: 0 },
              // String arguments
              hljs.QUOTE_STRING_MODE,
              // Boolean arguments
              { className: 'literal', begin: /\b(?:true|false)\b/ },
              // Numeric arguments
              {
                className: 'number',
                variants: [
                  { begin: '\\b0x[0-9a-fA-F_]+\\b' },
                  { begin: '\\b0b[01_]+\\b' },
                  { begin: '-?\\b[0-9][0-9_]*(\\.[0-9_]+)?\\b' }
                ],
                relevance: 0
              },
              // Type arguments (PascalCase identifiers in arg position)
              { className: 'type', begin: /[A-Z][A-Za-z0-9_]*/, relevance: 0 }
            ]
          }
        ]
      },

      // Comments
      hljs.C_LINE_COMMENT_MODE,
      hljs.C_BLOCK_COMMENT_MODE,

      // Interpolated strings
      {
        className: 'string',
        begin: '\\$"',
        end: '"',
        contains: [
          hljs.BACKSLASH_ESCAPE,
          { begin: '\\{', end: '\\}', className: 'subst', contains: [hljs.C_NUMBER_MODE] }
        ]
      },

      // Regular strings
      hljs.QUOTE_STRING_MODE,

      // Char literals
      { className: 'string', begin: "'", end: "'", contains: [hljs.BACKSLASH_ESCAPE] },

      // Regex literals (backtick)
      { className: 'string', begin: '`', end: '`' },

      // Numbers
      {
        className: 'number',
        variants: [
          { begin: '\\b0x[0-9a-fA-F_]+\\b' },
          { begin: '\\b0b[01_]+\\b' },
          { begin: '\\b[0-9][0-9_]*(\\.[0-9_]+)?\\b' }
        ],
        relevance: 0
      },

      // Operators (yellow)
      {
        className: 'operator',
        match: /=>|&&|\|\||[!=<>]=?|[+\-*/%&|^~]/
      },

      // Type/class declarations — PascalCase after class/struct/abstract/enum/namespace/type (aqua)
      {
        begin: /\b(?:class|struct|abstract|enum|namespace|type)\s+/,
        keywords: 'class struct abstract enum namespace type',
        contains: [
          { className: 'title.class', begin: /[A-Za-z_][A-Za-z0-9_]*/ }
        ]
      },

      // Type after colon (inheritance / type annotations)
      {
        begin: /:\s*/,
        contains: [
          { className: 'title.class', begin: /[A-Z][A-Za-z0-9_]*/, relevance: 0 }
        ]
      },

      // Constructor: new TypeName
      {
        begin: /\bnew\s+/,
        keywords: 'new',
        contains: [
          { className: 'title.class', begin: /[A-Z][A-Za-z0-9_]*/ }
        ]
      },

      // PascalCase followed by ( or < — method/type usage
      {
        className: 'title.function',
        begin: /\b[A-Z][A-Za-z0-9_]*(?=\s*[(<])/,
        relevance: 0
      },

      // PascalCase followed by a lowercase identifier — type annotation (e.g. AdultAge known)
      {
        className: 'title.class',
        begin: /\b[A-Z][A-Za-z0-9_]*(?=\s+[a-z_])/,
        relevance: 0
      },

      // Method calls: .Name(
      {
        className: 'title.function',
        begin: /\.[A-Z][A-Za-z0-9_]*(?=\s*[(<])/,
        relevance: 0
      }
    ]
  };
});
