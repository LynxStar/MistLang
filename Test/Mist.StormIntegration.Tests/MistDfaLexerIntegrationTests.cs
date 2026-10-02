using Storm.SourceGenerator.Emit;
using Storm.SourceGenerator.IR;
using Storm.SourceGenerator.Resolve;

namespace Mist.StormIntegration.Tests;

[Trait("Tier", "4")]
public class MistDfaLexerIntegrationTests
{
    //  Task 11.4 — Integration test: Mist grammar DFA lexer
    // ---------------------------------------------------------------

    /// <summary>
    /// Locates the Mist grammar directory by walking up from the test assembly's
    /// output directory until we find the repository root (contains Mist.slnx).
    /// </summary>
    private static string FindGrammarDirectory()
    {
        var dir = AppContext.BaseDirectory;
        while(dir != null)
        {
            var slnxPath = Path.Combine(dir, "Mist.slnx");
            if(File.Exists(slnxPath))
            {
                var grammarDir = Path.Combine(dir, "Source", "Mist", "Grammar");
                if(Directory.Exists(grammarDir))
                {
                    return grammarDir;
                }
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            "Could not locate the Mist grammar directory. " +
            "Ensure the test is run from within the repository tree.");
    }

    private static Func<string, string?> FileSystemReader(string grammarDir)
    {
        return path =>
        {
            var fsPath = path.Replace('/', Path.DirectorySeparatorChar);

            if(Path.IsPathRooted(fsPath) && File.Exists(fsPath))
            {
                return File.ReadAllText(fsPath);
            }

            var fullPath = Path.GetFullPath(Path.Combine(grammarDir, fsPath));
            if(File.Exists(fullPath))
            {
                return File.ReadAllText(fullPath);
            }

            return null;
        };
    }

    private static EmissionContext BuildMistEmissionContext()
    {
        var grammarDir = FindGrammarDirectory();
        var mainGrammarPath = Path.Combine(grammarDir, "Mist.storm")
            .Replace(Path.DirectorySeparatorChar, '/');

        var reader = FileSystemReader(grammarDir);
        var resolver = new StormImportResolver(reader);
        var resolution = resolver.Resolve(mainGrammarPath);

        Assert.True(resolution.Succeeded,
            "Mist grammar import resolution failed: " +
            string.Join(", ", resolution.Diagnostics.Select(d => d.Message)));

        var stormGrammar = StormGrammar.Build(resolution);
        var loweringResult = GrammarIRLowering.Lower(stormGrammar);

        Assert.True(loweringResult.Succeeded,
            "Mist grammar lowering failed: " +
            string.Join(", ", loweringResult.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(loweringResult.Grammar);

        return new EmissionContext("Mist.Generated", "Mist", loweringResult.Grammar!);
    }

    private static readonly Lazy<EmissionContext> MistContext = new(BuildMistEmissionContext);
    private static readonly Lazy<string> MistLexerOutput = new(() => LexerEmitter.Emit(MistContext.Value));

    [Fact]
    public void MistGrammar_DfaLexer_EmitsValidLexerClass()
    {
        var output = MistLexerOutput.Value;

        Assert.Contains("public static class MistLexer", output);
        Assert.Contains("public static TokenStream Lex(SourceText source, DiagnosticBag diagnostics)", output);
    }

    [Fact]
    public void MistGrammar_DfaLexer_ContainsAllExpectedTokenKinds()
    {
        var output = MistLexerOutput.Value;

        // Key token kinds from the Mist grammar
        Assert.Contains("WHITESPACE,", output);
        Assert.Contains("LINE_COMMENT,", output);
        Assert.Contains("IDENTIFIER,", output);
        Assert.Contains("INTEGER_LITERAL,", output);
        Assert.Contains("FLOAT_LITERAL,", output);
        Assert.Contains("STRING_LITERAL,", output);
        Assert.Contains("CHAR_LITERAL,", output);
        Assert.Contains("HEX_LITERAL,", output);
        Assert.Contains("INTERPOLATED_STRING_START,", output);
        Assert.Contains("INTERPOLATED_STRING_TEXT,", output);
        Assert.Contains("INTERPOLATION_OPEN,", output);
        Assert.Contains("INTERPOLATED_STRING_END,", output);

        // Operators
        Assert.Contains("PLUS,", output);
        Assert.Contains("MINUS,", output);
        Assert.Contains("STAR,", output);
        Assert.Contains("LOGICAL_AND,", output);
        Assert.Contains("LOGICAL_OR,", output);
        Assert.Contains("EQUAL_EQUAL,", output);

        // Keywords (from keywords.storm import)
        Assert.Contains("CLASS,", output);
        Assert.Contains("IF,", output);
        Assert.Contains("RETURN,", output);

        // EndOfFile and Error
        Assert.Contains("EndOfFile,", output);
        Assert.Contains("Error,", output);
    }

    [Fact]
    public void MistGrammar_DfaLexer_HasNoRegexUsage()
    {
        var output = MistLexerOutput.Value;

        Assert.DoesNotContain("System.Text.RegularExpressions", output);
        Assert.DoesNotContain("new Regex(", output);
        Assert.DoesNotContain("TryMatch_", output);
    }

    [Fact]
    public void MistGrammar_DfaLexer_HasDfaTransitionTables()
    {
        var output = MistLexerOutput.Value;

        Assert.Contains("_charClass", output);
        Assert.Contains("_trans_default", output);
        Assert.Contains("_accept_default", output);
    }

    [Fact]
    public void MistGrammar_DfaLexer_HasInterpolationModeTables()
    {
        var output = MistLexerOutput.Value;

        // Mist grammar has an Interpolation mode
        Assert.Contains("_trans_Interpolation", output);
        Assert.Contains("_accept_Interpolation", output);
        Assert.Contains("Mode_Interpolation", output);
    }

    [Fact]
    public void MistGrammar_DfaLexer_HasHiddenTokenSupport()
    {
        var output = MistLexerOutput.Value;

        // WHITESPACE and LINE_COMMENT are hidden
        Assert.Contains("(int)TokenKind.WHITESPACE", output);
        Assert.Contains("(int)TokenKind.LINE_COMMENT", output);
        Assert.Contains("IsHidden", output);
    }

    [Fact]
    public void MistGrammar_DfaLexer_HasModeStackLogic()
    {
        var output = MistLexerOutput.Value;

        Assert.Contains("modeStack", output);
        Assert.Contains("modeStack.Push(Mode_default)", output);
        Assert.Contains("modeStack.Peek()", output);
    }

    [Fact]
    public void MistGrammar_DfaLexer_HasCorrectPublicApi()
    {
        var output = MistLexerOutput.Value;

        Assert.Contains("public static TokenStream Lex(SourceText source, DiagnosticBag diagnostics)", output);
        Assert.Contains("public static bool IsHidden(int tokenKind)", output);
        Assert.Contains("public static int GetTokenMode(int tokenKind)", output);
    }

    // ---------------------------------------------------------------
    [Fact]
    public void InterpolatedString_MistGrammar_HasCorrectModeActions()
    {
        // Verify the full Mist grammar emits the correct mode actions
        // for interpolated string tokens
        var output = MistLexerOutput.Value;

        // INTERPOLATED_STRING_START ($") pushes Interpolation mode
        Assert.Contains("modeStack.Push(Mode_Interpolation)", output);

        // INTERPOLATION_OPEN ({) pushes default mode
        // LEFT_BRACE ({) also pushes default mode
        Assert.Contains("modeStack.Push(Mode_default)", output);

        // RIGHT_BRACE (}) and INTERPOLATED_STRING_END (") pop mode
        Assert.Contains("modeStack.Pop()", output);
        Assert.Contains("modeStack.Count > 1", output);
    }

    [Fact]
    public void InterpolatedString_InterpolationModeTokens_AreInCorrectMode()
    {
        var output = MistLexerOutput.Value;

        // The TokenModes array should map interpolation tokens to mode 1
        Assert.Contains("TokenModes", output);

        // Verify mode-specific tokens exist in the enum
        Assert.Contains("INTERPOLATED_STRING_TEXT,", output);
        Assert.Contains("INTERPOLATION_OPEN,", output);
        Assert.Contains("INTERPOLATED_STRING_END,", output);
    }
}