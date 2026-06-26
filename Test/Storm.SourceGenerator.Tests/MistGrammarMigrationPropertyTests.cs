using System.Collections.Immutable;
using System.Reflection;
using FsCheck;
using FsCheck.Fluent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Storm.SourceGenerator.Emit;
using Storm.SourceGenerator.IR;
using Storm.SourceGenerator.Resolve;

namespace Storm.SourceGenerator.Tests;

/// <summary>
/// Property-based tests for Mist grammar migration from character classes to regex literals.
/// Feature: regex-literals, Property 4: Mist grammar migration produces identical tokenization
/// </summary>
public class MistGrammarMigrationPropertyTests
{
    // ── Grammar definitions ──────────────────────────────────────────

    private const string KeywordsGrammar = @"
token NAMESPACE = ""namespace"";
token USING     = ""using"";
token CLASS     = ""class"";
token STRUCT    = ""struct"";
token PUBLIC    = ""public"";
token PRIVATE   = ""private"";
token STATIC    = ""static"";
token VOID      = ""void"";
token NEW       = ""new"";
token RETURN    = ""return"";
token IF        = ""if"";
token ELSE      = ""else"";
token WHILE     = ""while"";
token FOR       = ""for"";
token FOREACH   = ""foreach"";
token IN        = ""in"";
token BREAK     = ""break"";
token VAR       = ""var"";
token TRUE      = ""true"";
token FALSE     = ""false"";
token THIS      = ""this"";
token INT       = ""int"";
token UINT      = ""uint"";
token LONG      = ""long"";
token ULONG     = ""ulong"";
token SHORT   = ""short"";
token USHORT  = ""ushort"";
token BYTE    = ""byte"";
token SBYTE   = ""sbyte"";
token FLOAT   = ""float"";
token DOUBLE  = ""double"";
token DECIMAL = ""decimal"";
token BOOL    = ""bool"";
token CHAR    = ""char"";
token STRING  = ""string"";
";

    private const string SharedOperatorsAndRules = @"
token LOGICAL_AND           = ""&&"";
token LOGICAL_OR            = ""||"";
token EQUAL_EQUAL           = ""=="";
token NOT_EQUAL             = ""!="";
token LESS_THAN_OR_EQUAL    = ""<="";
token GREATER_THAN_OR_EQUAL = "">="";
token PLUS                  = ""+"";
token MINUS                 = ""-"";
token STAR                  = ""*"";
token SLASH                 = ""/"";
token MODULO                = ""%"";
token EQUAL                 = ""="";
token LESS_THAN             = ""<"";
token GREATER_THAN          = "">"";
token EXCLAMATION           = ""!"";
token DOT                   = ""."";
token COMMA                 = "","";
token SEMICOLON             = "";"";
token COLON                 = "":"";
token LEFT_PARENTHESIS      = ""("";
token RIGHT_PARENTHESIS     = "")"";
token LEFT_BRACE            = ""{"";
token RIGHT_BRACE           = ""}"";
token LEFT_BRACKET          = ""["";
token RIGHT_BRACKET         = ""]"";
";

    private const string SharedEscapeSequence = @"
fragment ESCAPE_SEQUENCE   = ""\\\\"" | ""\\\"""" | ""\\n"" | ""\\r"" | ""\\t"" | ""\\0"" | ""\\'"";
";

    /// <summary>Original Mist grammar using character classes (pre-migration).</summary>
    private const string OriginalGrammar = KeywordsGrammar + @"
hidden token WHITESPACE   = [ \t\r\n]+;
hidden token LINE_COMMENT = ""//"" [^\n]*;
" + SharedOperatorsAndRules + @"
fragment HEX_DIGIT        = [0-9a-fA-F];
fragment INT_SUFFIX        = [lLuU] | ""UL"" | ""ul"" | ""Ul"" | ""uL"" | ""Lu"" | ""lU"" | ""LU"" | ""lu"";
fragment FLOAT_SUFFIX      = [fFdDmM];
" + SharedEscapeSequence + @"
token HEX_LITERAL     = ""0"" [xX] HEX_DIGIT+ INT_SUFFIX?;
token INTEGER_LITERAL  = [0-9]+ INT_SUFFIX?;
token FLOAT_LITERAL    = [0-9]+ ""."" [0-9]+ FLOAT_SUFFIX?
                       | [0-9]+ FLOAT_SUFFIX;
token CHAR_LITERAL     = ""'"" (ESCAPE_SEQUENCE | [^'\\]) ""'"";
token STRING_LITERAL   = ""\"""" (ESCAPE_SEQUENCE | [^""\\])* ""\"""";
token INTERPOLATED_STRING_START = ""$\"""";
token INTERPOLATED_STRING_TEXT  = ([^""\\{] | ESCAPE_SEQUENCE)+;
token INTERPOLATED_STRING_END   = ""\"""";
token IDENTIFIER = [a-zA-Z_][a-zA-Z0-9_]*;
rule Start = IDENTIFIER;
";

    /// <summary>Migrated Mist grammar using regex literals (post-migration).</summary>
    private const string MigratedGrammar = KeywordsGrammar + @"
hidden token WHITESPACE   = `\s+`;
hidden token LINE_COMMENT = ""//"" `[^\n]*`;
" + SharedOperatorsAndRules + @"
fragment HEX_DIGIT        = `[0-9a-fA-F]`;
fragment INT_SUFFIX        = `[lLuU]` | ""UL"" | ""ul"" | ""Ul"" | ""uL"" | ""Lu"" | ""lU"" | ""LU"" | ""lu"";
fragment FLOAT_SUFFIX      = `[fFdDmM]`;
" + SharedEscapeSequence + @"
token HEX_LITERAL     = ""0"" `[xX]` HEX_DIGIT+ INT_SUFFIX?;
token INTEGER_LITERAL  = `[0-9]+` INT_SUFFIX?;
token FLOAT_LITERAL    = `[0-9]+` ""."" `[0-9]+` FLOAT_SUFFIX?
                       | `[0-9]+` FLOAT_SUFFIX;
token CHAR_LITERAL     = ""'"" (ESCAPE_SEQUENCE | `[^'\\]`) ""'"";
token STRING_LITERAL   = ""\"""" (ESCAPE_SEQUENCE | `[^""\\]`)* ""\"""";
token INTERPOLATED_STRING_START = ""$\"""";
token INTERPOLATED_STRING_TEXT  = (`[^""\\{]` | ESCAPE_SEQUENCE)+;
token INTERPOLATED_STRING_END   = ""\"""";
token IDENTIFIER = `[a-zA-Z_][a-zA-Z0-9_]*`;
rule Start = IDENTIFIER;
";

    // ── Helpers ──────────────────────────────────────────────────────

    private static Func<string, string?> InMemoryFileReader(Dictionary<string, string> files)
    {
        return path =>
        {
            var normalized = StormImportResolver.CanonicalizePath(path);
            if(files.TryGetValue(normalized, out var contents))
            {
                return contents;
            }

            return null;
        };
    }

    private static readonly object emitLock = new();

    private static (string runtimeSource, string lexerSource) GenerateLexerCode(
        string grammarBody, string grammarName, string ns)
    {
        var fullGrammar = $"grammar {grammarName};\n{grammarBody}";
        var files = new Dictionary<string, string>
        {
            ["root/main.storm"] = fullGrammar
        };

        var resolver = new StormImportResolver(InMemoryFileReader(files));
        var resolution = resolver.Resolve("root/main.storm");
        var stormGrammar = StormGrammar.Build(resolution);
        var loweringResult = GrammarIRLowering.Lower(stormGrammar);

        Assert.True(loweringResult.Succeeded,
            "Grammar lowering failed: " +
            string.Join(", ", loweringResult.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(loweringResult.Grammar);

        var context = new EmissionContext(ns, grammarName, loweringResult.Grammar!);
        string runtimeSource, lexerSource;
        lock(emitLock)
        {
            runtimeSource = AstEmitter.EmitRuntimeTypes();
            lexerSource = LexerEmitter.Emit(context);
        }

        return (runtimeSource, lexerSource);
    }

    /// <summary>
    /// Minimal stub for Mist.Common.Text.NodeOrigin so the emitted SyntaxNode compiles
    /// without requiring a reference to the full Mist assembly.
    /// </summary>
    private const string NodeOriginStub = @"
namespace Mist.Common.Text
{
    public abstract record NodeOrigin;
}
";

    private static Assembly CompileLexer(string runtimeSource, string lexerSource)
    {
        var syntaxTrees = new[]
        {
            CSharpSyntaxTree.ParseText(runtimeSource),
            CSharpSyntaxTree.ParseText(lexerSource),
            CSharpSyntaxTree.ParseText(NodeOriginStub),
        };

        var references = new MetadataReference[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ImmutableArray).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Text.RegularExpressions.Regex).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Collections").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Collections.Immutable").Location),
        };

        var compilation = CSharpCompilation.Create(
            "DynamicLexer_" + Guid.NewGuid().ToString("N"),
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);

        Assert.True(result.Success,
            "Compilation failed:\n" +
            string.Join("\n", result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.GetMessage())));

        ms.Seek(0, SeekOrigin.Begin);
        return Assembly.Load(ms.ToArray());
    }

    private static List<(int kind, string lexeme)> Tokenize(
        Assembly assembly, string ns, string grammarName, string input)
    {
        var sourceTextType = assembly.GetType("Storm.Runtime.SourceText")!;
        var diagnosticBagType = assembly.GetType("Storm.Runtime.DiagnosticBag")!;
        var lexerType = assembly.GetType($"{ns}.{grammarName}Lexer")!;

        var source = Activator.CreateInstance(sourceTextType, "test.mist", input)!;
        var diagnostics = Activator.CreateInstance(diagnosticBagType)!;

        var lexMethod = lexerType.GetMethod("Lex", BindingFlags.Public | BindingFlags.Static)!;
        var tokenStream = lexMethod.Invoke(null, new[] { source, diagnostics })!;

        var allTokensProp = tokenStream.GetType().GetProperty("AllTokens")!;
        var allTokens = allTokensProp.GetValue(tokenStream)!;

        var enumerator = ((System.Collections.IEnumerable)allTokens).GetEnumerator();
        var tokens = new List<(int kind, string lexeme)>();

        while(enumerator.MoveNext())
        {
            var token = enumerator.Current!;
            var tokenType = token.GetType();
            var kind = (int)tokenType.GetProperty("Kind")!.GetValue(token)!;
            var lexeme = (string)tokenType.GetProperty("Lexeme")!.GetValue(token)!;
            tokens.Add((kind, lexeme));
        }

        return tokens;
    }

    // ── FsCheck generators ──────────────────────────────────────────

    private static Gen<string> GenMistSourceSnippet()
    {
        var identifiers = new[] { "x", "foo", "Bar", "_test", "myVar123", "a_b_c", "_", "A" };
        var keywords = new[] { "if", "else", "while", "for", "return", "class", "int", "bool", "true", "false", "new", "this", "var" };
        var integers = new[] { "0", "1", "42", "100", "999", "0L", "0u", "123l", "456U" };
        var hexLiterals = new[] { "0xFF", "0x0", "0XAB", "0x1a2B" };
        var floats = new[] { "1.0", "3.14", "0.5f", "2.0d", "1.0m", "100.0F" };
        var operators = new[] { "+", "-", "*", "/", "%", "=", "==", "!=", "<", ">", "<=", ">=", "&&", "||", "!", ".", ",", ";", ":", "(", ")", "{", "}", "[", "]" };
        var whitespace = new[] { " ", "  ", "\t", "\n", " \n ", "\r\n" };
        var strings = new[] { "\"hello\"", "\"\"", "\"abc def\"", "\"test\\n\"", "\"a\\tb\"" };
        var chars = new[] { "'a'", "'z'", "'0'", "'\\n'", "'\\t'", "'\\\\'" };
        var comments = new[] { "// comment\n", "// test\n" };

        var tokenGen = Gen.Frequency(
            (3, Gen.Elements(identifiers)),
            (2, Gen.Elements(keywords)),
            (2, Gen.Elements(integers)),
            (1, Gen.Elements(hexLiterals)),
            (1, Gen.Elements(floats)),
            (3, Gen.Elements(operators)),
            (3, Gen.Elements(whitespace)),
            (1, Gen.Elements(strings)),
            (1, Gen.Elements(chars)),
            (1, Gen.Elements(comments))
        );

        return Gen.Choose(1, 15).SelectMany(count =>
            tokenGen.ArrayOf(count).Select(tokens =>
                string.Join("", tokens)));
    }

    // ── Property test ───────────────────────────────────────────────

    /// <summary>
    /// **Validates: Requirements 8.4**
    ///
    /// Property 4: Mist grammar migration produces identical tokenization.
    /// For any valid Mist source text, tokenizing with the migrated Mist.storm
    /// (regex literals) SHALL produce a token sequence identical in token kinds
    /// and lexemes to the original grammar (character classes).
    /// </summary>
    [Fact]
    public void MigrationProducesIdenticalTokenization()
    {
        // Generate assemblies fresh to avoid thread-static interference
        var (origRuntime, origLexer) = GenerateLexerCode(OriginalGrammar, "Original", "Original.Generated");
        var (migrRuntime, migrLexer) = GenerateLexerCode(MigratedGrammar, "Migrated", "Migrated.Generated");
        var origAsm = CompileLexer(origRuntime, origLexer);
        var migrAsm = CompileLexer(migrRuntime, migrLexer);

        var samples = GenMistSourceSnippet().Sample(100);
        foreach(var input in samples)
        {
            var originalTokens = Tokenize(origAsm, "Original.Generated", "Original", input);
            var migratedTokens = Tokenize(migrAsm, "Migrated.Generated", "Migrated", input);

            Assert.True(originalTokens.Count == migratedTokens.Count,
                $"Token count mismatch for input: [{Escape(input)}]\n" +
                $"Original ({originalTokens.Count}): {FormatTokens(originalTokens)}\n" +
                $"Migrated ({migratedTokens.Count}): {FormatTokens(migratedTokens)}");

            for(int i = 0; i < originalTokens.Count; i++)
            {
                Assert.True(originalTokens[i].kind == migratedTokens[i].kind,
                    $"Token kind mismatch at index {i} for input: [{Escape(input)}]\n" +
                    $"Original[{i}]: kind={originalTokens[i].kind} lexeme=[{Escape(originalTokens[i].lexeme)}]\n" +
                    $"Migrated[{i}]: kind={migratedTokens[i].kind} lexeme=[{Escape(migratedTokens[i].lexeme)}]");
                Assert.True(originalTokens[i].lexeme == migratedTokens[i].lexeme,
                    $"Token lexeme mismatch at index {i} for input: [{Escape(input)}]\n" +
                    $"Original[{i}]: [{Escape(originalTokens[i].lexeme)}]\n" +
                    $"Migrated[{i}]: [{Escape(migratedTokens[i].lexeme)}]");
            }
        }
    }

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    private static string FormatTokens(List<(int kind, string lexeme)> tokens) =>
        string.Join(", ", tokens.Select(t => $"({t.kind}:[{Escape(t.lexeme)}])"));
}