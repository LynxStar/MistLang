// Feature: type-literals, Property 8: C# emitter wraps type literals in typeof()
// Updated: array type literals (e.g., int[]) in expression position now emit typeof(T[]),
// NOT Array.Empty<T>(). The empty array literal is now [] (bare brackets).

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for C# emitter typeof() wrapping of type literals.
///
/// Property 8: C# emitter wraps type literals in typeof()
/// For any TypeLiteralExpression (including array types like int[]),
/// the C# emitter SHALL produce typeof(&lt;type_reference&gt;).
/// Array type literals in expression position are type references (not empty arrays).
/// The empty array literal is now [] (bare brackets), handled separately.
///
/// **Validates: Requirements 5.1, 5.3, 5.4**
/// </summary>
public sealed class TypeLiteralEmitterPropertyTests : IDisposable
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private readonly SemanticModelBuilder _builder = new();
    private readonly string _outputDir;

    public TypeLiteralEmitterPropertyTests()
    {
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "MistTypeLiteralEmitterTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    // ─── Primitive type keywords and their expected C# typeof() forms ────

    private static readonly (string MistKeyword, string CSharpKeyword)[] PrimitiveTypes =
    [
        ("int", "int"),
        ("uint", "uint"),
        ("long", "long"),
        ("ulong", "ulong"),
        ("short", "short"),
        ("ushort", "ushort"),
        ("byte", "byte"),
        ("sbyte", "sbyte"),
        ("float", "float"),
        ("double", "double"),
        ("decimal", "decimal"),
        ("bool", "bool"),
        ("char", "char"),
        ("string", "string"),
    ];

    // ─── Test input model ────────────────────────────────────────────────

    /// <summary>
    /// Represents a test case: a type keyword used as a type literal in a Mist program,
    /// along with the expected emitted C# fragment.
    /// All type literals (including array types) emit typeof(T) or typeof(T[]).
    /// </summary>
#pragma warning disable CA1034
    public record TypeLiteralEmitterTestCase(
        string MistKeyword,
        string ExpectedFragment,
        bool IsArray,
        string Source)
    {
        public override string ToString() =>
            IsArray ? $"{MistKeyword}[] → {ExpectedFragment}" : $"{MistKeyword} → {ExpectedFragment}";
    }
#pragma warning restore CA1034

    // ─── Source builders ─────────────────────────────────────────────────

    private static string BuildVarInitSource(string keyword, bool isArray)
    {
        var typeExpr = isArray ? $"{keyword}[]" : keyword;
        return $"class Program {{ public static void Main() {{ var x = {typeExpr}; }} }}";
    }

    // ─── FsCheck Arbitrary ───────────────────────────────────────────────

    public static Arbitrary<TypeLiteralEmitterTestCase> ArbitraryTypeLiteralEmitterTestCase()
    {
        var gen =
            from typeIndex in Gen.Choose(0, PrimitiveTypes.Length - 1)
            from isArray in Gen.Elements(true, false)
            let mistKeyword = PrimitiveTypes[typeIndex].MistKeyword
            let csKeyword = PrimitiveTypes[typeIndex].CSharpKeyword
            // All type literals in expression position emit typeof(T) or typeof(T[])
            let expectedFragment = isArray ? $"typeof({csKeyword}[])" : $"typeof({csKeyword})"
            let source = BuildVarInitSource(mistKeyword, isArray)
            select new TypeLiteralEmitterTestCase(mistKeyword, expectedFragment, isArray, source);

        return gen.ToArbitrary();
    }

    // ─── Pipeline helpers ────────────────────────────────────────────────

    private string? CompileAndGetCSharpOutput(string mistSource)
    {
        // Clean output directory
        foreach(var file in Directory.GetFiles(_outputDir))
        {
            File.Delete(file);
        }

        // 1. Lex
        var sourceText = new SourceText("test.mist", mistSource);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        if(bag.HasErrors)
        {
            return null;
        }

        // 2. Parse
        var parseResult = Parser.Parse(tokens, bag);
        if(parseResult.Root is null || bag.HasErrors)
        {
            return null;
        }

        // 3. Build semantic model
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semanticBag = new DiagnosticBag();
        var model = _builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semanticBag);

        // 4. Emit C#
        var emitBag = new DiagnosticBag();
        CSharpEmitter.Emit(model, _outputDir, "TestProject", emitBag);

        // 5. Read the emitted .cs file
        var csFile = Directory.GetFiles(_outputDir, "*.cs").FirstOrDefault();
        return csFile is not null ? File.ReadAllText(csFile) : null;
    }

    // ═════════════════════════════════════════════════════════════════════
    // Property 8: C# emitter wraps ALL type literals in typeof()
    //
    // **Validates: Requirements 5.1, 5.3, 5.4**
    //
    // For any TypeLiteralExpression (including array type references like
    // int[], string[]), the C# emitter SHALL produce typeof(<type_reference>).
    // Array type literals in expression position are type references only —
    // they do NOT represent empty arrays. The empty array literal is now []
    // (bare brackets), which is a separate grammar alternative.
    // ═════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(TypeLiteralEmitterPropertyTests) })]
    public bool TypeLiteral_EmittedAs_Typeof(TypeLiteralEmitterTestCase testCase)
    {
        var csOutput = CompileAndGetCSharpOutput(testCase.Source);

        // Compilation must succeed and produce output
        if(csOutput is null)
        {
            return false;
        }

        // The emitted C# must contain the expected fragment
        return csOutput.Contains(testCase.ExpectedFragment);
    }
}