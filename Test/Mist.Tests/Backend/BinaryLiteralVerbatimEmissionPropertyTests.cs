// Feature: binary-literals, Property 6: Verbatim emission of numeric literals
// **Validates: Requirements 4.1, 4.2, 5.1, 8.11, 8.12**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Parsing;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for verbatim emission of numeric literals.
///
/// Property 6: Verbatim emission of numeric literals.
/// For any numeric literal AST node (binary, hex, integer, or float, with or without
/// digit separators and suffixes), both the pretty printer and the C# emitter SHALL
/// output the token lexeme unchanged.
///
/// **Validates: Requirements 4.1, 4.2, 5.1, 8.11, 8.12**
/// </summary>
public sealed class BinaryLiteralVerbatimEmissionPropertyTests : IDisposable
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private readonly SemanticModelBuilder _builder = new();
    private readonly string _outputDir;

    public BinaryLiteralVerbatimEmissionPropertyTests()
    {
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "MistVerbatimEmission_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    // ─── Suffix Constants ────────────────────────────────────────────────

    private static readonly string[] IntSuffixes =
        ["", "l", "L", "u", "U", "UL", "ul", "Ul", "uL", "Lu", "lU", "LU", "lu"];

    private static readonly string[] FloatSuffixes =
        ["f", "F", "d", "D", "m", "M"];

    private static readonly string[] FloatSuffixesWithEmpty =
        ["", "f", "F", "d", "D", "m", "M"];

    // ─── Shared Digit Group Generators ───────────────────────────────────

    private static Gen<string> GenDecimalDigitGroup()
    {
        return
            from length in Gen.Choose(1, 4)
            from digits in Gen.ArrayOf(Gen.Choose(0, 9), length)
            select string.Concat(digits);
    }

    private static Gen<string> GenHexDigitGroup()
    {
        return
            from length in Gen.Choose(1, 4)
            from digits in Gen.ArrayOf(Gen.Elements(
                '0', '1', '2', '3', '4', '5', '6', '7', '8', '9',
                'a', 'b', 'c', 'd', 'e', 'f',
                'A', 'B', 'C', 'D', 'E', 'F'), length)
            select new string(digits);
    }

    private static Gen<string> GenBinaryDigitGroup()
    {
        return
            from length in Gen.Choose(1, 4)
            from digits in Gen.ArrayOf(Gen.Elements('0', '1'), length)
            select new string(digits);
    }

    private static Gen<string> GenSeparatorGroups(Gen<string> digitGroupGen)
    {
        return
            from count in Gen.Choose(0, 3)
            from groups in Gen.ArrayOf(digitGroupGen, count)
            select string.Concat(groups.Select(g => "_" + g));
    }

    // ─── Literal Generators ─────────────────────────────────────────────

    private static Gen<string> GenBinaryLiteral()
    {
        return
            from prefix in Gen.Elements("0b", "0B")
            from firstGroup in GenBinaryDigitGroup()
            from separatorGroups in GenSeparatorGroups(GenBinaryDigitGroup())
            from suffix in Gen.Elements(IntSuffixes)
            select prefix + firstGroup + separatorGroups + suffix;
    }

    private static Gen<string> GenHexLiteral()
    {
        return
            from prefix in Gen.Elements("0x", "0X")
            from firstGroup in GenHexDigitGroup()
            from separatorGroups in GenSeparatorGroups(GenHexDigitGroup())
            from suffix in Gen.Elements(IntSuffixes)
            select prefix + firstGroup + separatorGroups + suffix;
    }

    private static Gen<string> GenIntegerLiteral()
    {
        return
            from firstDigit in Gen.Elements('1', '2', '3', '4', '5', '6', '7', '8', '9')
            from restLength in Gen.Choose(0, 3)
            from restDigits in Gen.ArrayOf(Gen.Choose(0, 9), restLength)
            from separatorGroups in GenSeparatorGroups(GenDecimalDigitGroup())
            from suffix in Gen.Elements(IntSuffixes)
            select firstDigit + string.Concat(restDigits) + separatorGroups + suffix;
    }

    private static Gen<string> GenFloatDotLiteral()
    {
        return
            from firstDigit in Gen.Elements('1', '2', '3', '4', '5', '6', '7', '8', '9')
            from restLength in Gen.Choose(0, 3)
            from restDigits in Gen.ArrayOf(Gen.Choose(0, 9), restLength)
            from intSepGroups in GenSeparatorGroups(GenDecimalDigitGroup())
            from fracFirstGroup in GenDecimalDigitGroup()
            from fracSepGroups in GenSeparatorGroups(GenDecimalDigitGroup())
            from suffix in Gen.Elements(FloatSuffixesWithEmpty)
            select firstDigit + string.Concat(restDigits) + intSepGroups
                   + "." + fracFirstGroup + fracSepGroups + suffix;
    }

    private static Gen<string> GenFloatSuffixLiteral()
    {
        return
            from firstDigit in Gen.Elements('1', '2', '3', '4', '5', '6', '7', '8', '9')
            from restLength in Gen.Choose(0, 3)
            from restDigits in Gen.ArrayOf(Gen.Choose(0, 9), restLength)
            from separatorGroups in GenSeparatorGroups(GenDecimalDigitGroup())
            from suffix in Gen.Elements(FloatSuffixes)
            select firstDigit + string.Concat(restDigits) + separatorGroups + suffix;
    }

    private static Gen<string> GenNumericLiteral()
    {
        return Gen.OneOf(
            GenBinaryLiteral(),
            GenHexLiteral(),
            GenIntegerLiteral(),
            GenFloatDotLiteral(),
            GenFloatSuffixLiteral());
    }

    // ─── Test Scenario ──────────────────────────────────────────────────

    /// <summary>
    /// A complete Mist program containing a numeric literal as a variable initializer,
    /// paired with the literal string for verification.
    /// </summary>
#pragma warning disable CA1034
    public record VerbatimEmissionScenario(string Source, string Literal)
    {
        public override string ToString() => Literal;
    }
#pragma warning restore CA1034

    public static Arbitrary<VerbatimEmissionScenario> ArbitraryVerbatimEmissionScenario()
    {
        var gen =
            from literal in GenNumericLiteral()
            let source = $"class C {{ public static void Main() {{ var x = {literal}; }} }}"
            select new VerbatimEmissionScenario(source, literal);

        return gen.ToArbitrary();
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static CompilationUnitNode? ParseSource(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        if (bag.HasErrors) return null;
        var result = Parser.Parse(tokens, bag);
        return result.Root as CompilationUnitNode;
    }

    private ISemanticModel BuildSemanticModel(Storm.Runtime.SyntaxNode ast, string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        return _builder.Build(
            compilation,
            ImmutableArray.Create(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
    }

    private string EmitAndGetCsContent(ISemanticModel model)
    {
        // Clean the output directory for each emission
        foreach (var file in Directory.GetFiles(_outputDir))
            File.Delete(file);

        var diagnostics = new DiagnosticBag();
        CSharpEmitter.Emit(model, _outputDir, "TestProject", diagnostics);

        var csFile = Directory.GetFiles(_outputDir, "*.cs")
            .FirstOrDefault(f => !f.EndsWith("MistVoid.g.cs", StringComparison.Ordinal));
        return csFile is not null ? File.ReadAllText(csFile) : "";
    }

    // ─── Property 6 ─────────────────────────────────────────────────────

    /// <summary>
    /// Property 6: Verbatim emission of numeric literals — Pretty Printer.
    ///
    /// For any numeric literal (binary, hex, integer, or float, with or without
    /// digit separators and suffixes), the pretty printer SHALL output the token
    /// lexeme unchanged.
    ///
    /// **Validates: Requirements 5.1, 8.12**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(BinaryLiteralVerbatimEmissionPropertyTests) })]
    public Property NumericLiteral_PrettyPrinter_EmitsLexemeVerbatim(
        VerbatimEmissionScenario scenario)
    {
        var root = ParseSource(scenario.Source);

        if (root is null)
            return false.ToProperty()
                .Label($"Parse failed for: {scenario.Source}");

        var printed = MistPrettyPrinter.Print(root, scenario.Source);

        return printed.Contains(scenario.Literal).ToProperty()
            .Label($"Pretty printer output does not contain literal '{scenario.Literal}'.\nPrinted:\n{printed}");
    }

    /// <summary>
    /// Property 6: Verbatim emission of numeric literals — C# Emitter.
    ///
    /// For any numeric literal (binary, hex, integer, or float, with or without
    /// digit separators and suffixes), the C# emitter SHALL output the token
    /// lexeme unchanged.
    ///
    /// **Validates: Requirements 4.1, 4.2, 8.11**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(BinaryLiteralVerbatimEmissionPropertyTests) })]
    public Property NumericLiteral_CSharpEmitter_EmitsLexemeVerbatim(
        VerbatimEmissionScenario scenario)
    {
        var root = ParseSource(scenario.Source);

        if (root is null)
            return false.ToProperty()
                .Label($"Parse failed for: {scenario.Source}");

        var model = BuildSemanticModel(root, scenario.Source);
        var csContent = EmitAndGetCsContent(model);

        return csContent.Contains(scenario.Literal).ToProperty()
            .Label($"C# emitter output does not contain literal '{scenario.Literal}'.\nEmitted:\n{csContent}");
    }
}
