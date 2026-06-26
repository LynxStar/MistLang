// Task 8.2: Unit tests for carrier struct generation and registry
// Validates: Requirements 13.3, 13.6

using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests for the <see cref="CarrierStructRegistry"/> verifying that carrier structs
/// are generated for each unique (type, value) pair and deduplicated across the program.
/// Tests use inheritance syntax (e.g., <c>class SmallBuffer : FixedBuffer&lt;5&gt;</c>) to
/// create ConstructedType instances with DependentArguments during Pass 1, avoiding the
/// need for full method body resolution or the emitter's type argument emission (Task 8.5).
///
/// **Validates: Requirements 13.3, 13.6**
/// </summary>
public sealed class CarrierStructRegistryTests
{
    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder ModelBuilder = new();

    // ─── In-memory compilation helper ────────────────────────────────────

    /// <summary>
    /// Compiles a Mist source and returns the generated DependentTypeHelpers.g.cs content.
    /// Uses the semantic model directly (no disk I/O) to collect carrier structs.
    /// </summary>
    private static string? CompileAndGetHelpersContent(string mistSource)
    {
        var sourceText = new SourceText("test.mist", mistSource);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));

        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        Assert.False(lexBag.HasErrors, $"Lex errors: {FormatDiagnostics(lexBag)}");

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        Assert.False(parseBag.HasErrors, $"Parse errors: {FormatDiagnostics(parseBag)}");
        Assert.NotNull(parseResult.Root);

        var semBag = new DiagnosticBag();
        var model = ModelBuilder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProgram",
            projectFolder: "/test",
            semBag);
        Assert.False(semBag.HasErrors, $"Semantic errors: {FormatDiagnostics(semBag)}");

        // Collect interfaces and carrier structs
        var usedKeywords = DependentTypeHelpersGenerator.CollectUsedDependentValueTypes(model);
        var registry = CarrierStructRegistry.CollectFromModel(model);

        if(usedKeywords.Count == 0 && registry.Entries.Count == 0)
            return null;

        return DependentTypeHelpersGenerator.GenerateHelpersFile(usedKeywords, registry);
    }

    private static string FormatDiagnostics(DiagnosticBag bag)
    {
        return string.Join("\n", bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}"));
    }

    // ─── Carrier Struct Generation Tests ─────────────────────────────────

    [Fact]
    public void EmitsCarrierStructForIntValue()
    {
        // Use inheritance to create a ConstructedType with DependentArguments in Pass 1
        var source = "class FixedBuffer<Size:int> { }\nclass SmallBuffer : FixedBuffer<5> { }";
        var content = CompileAndGetHelpersContent(source);

        Assert.NotNull(content);
        Assert.Contains("public struct Const_Int_5 : IConstInt", content);
        Assert.Contains("public static int Value => 5;", content);
    }

    [Fact]
    public void EmitsCarrierStructForNegativeInt()
    {
        var source = "class Offset<N:int> { }\nclass NegOffset : Offset<-1> { }";
        var content = CompileAndGetHelpersContent(source);

        Assert.NotNull(content);
        Assert.Contains("public struct Const_Int_Neg1 : IConstInt", content);
        Assert.Contains("public static int Value => -1;", content);
    }

    [Fact]
    public void EmitsCarrierStructForBoolTrue()
    {
        var source = "class Flag<Enabled:bool> { }\nclass TrueFlag : Flag<true> { }";
        var content = CompileAndGetHelpersContent(source);

        Assert.NotNull(content);
        Assert.Contains("public struct Const_Bool_True : IConstBool", content);
        Assert.Contains("public static bool Value => true;", content);
    }

    [Fact]
    public void EmitsCarrierStructForBoolFalse()
    {
        var source = "class Flag<Enabled:bool> { }\nclass FalseFlag : Flag<false> { }";
        var content = CompileAndGetHelpersContent(source);

        Assert.NotNull(content);
        Assert.Contains("public struct Const_Bool_False : IConstBool", content);
        Assert.Contains("public static bool Value => false;", content);
    }

    [Fact]
    public void EmitsCarrierStructForAlphanumericChar()
    {
        var source = "class CharHolder<C:char> { }\nclass XHolder : CharHolder<'X'> { }";
        var content = CompileAndGetHelpersContent(source);

        Assert.NotNull(content);
        Assert.Contains("public struct Const_Char_X : IConstChar", content);
        Assert.Contains("public static char Value => 'X';", content);
    }

    [Fact]
    public void EmitsCarrierStructForNonAlphanumericCharWithHexCode()
    {
        var source = "class CharHolder<C:char> { }\nclass SpaceHolder : CharHolder<' '> { }";
        var content = CompileAndGetHelpersContent(source);

        Assert.NotNull(content);
        Assert.Contains("public struct Const_Char_0x20 : IConstChar", content);
        Assert.Contains("public static char Value => ' ';", content);
    }

    [Fact]
    public void DeduplicatesCarrierStructsForSameValueAndType()
    {
        var source = "class Buffer<Size:int> { }\nclass Matrix<Rows:int, Cols:int> { }\nclass A : Buffer<5> { }\nclass B : Matrix<5, 5> { }";
        var content = CompileAndGetHelpersContent(source);

        Assert.NotNull(content);
        // Const_Int_5 should appear exactly once as a struct declaration
        var count = content.Split("public struct Const_Int_5 : IConstInt").Length - 1;
        Assert.Equal(1, count);
    }

    [Fact]
    public void EmitsDistinctCarrierStructsForDifferentValues()
    {
        var source = "class Buffer<Size:int> { }\nclass A : Buffer<5> { }\nclass B : Buffer<10> { }";
        var content = CompileAndGetHelpersContent(source);

        Assert.NotNull(content);
        Assert.Contains("public struct Const_Int_5 : IConstInt", content);
        Assert.Contains("public struct Const_Int_10 : IConstInt", content);
    }

    [Fact]
    public void DoesNotEmitCarrierStructForOpenDependentParameter()
    {
        var source = "class Buffer<Size:int> { }\nclass Wrapper<N:int> : Buffer<N> { }";
        var content = CompileAndGetHelpersContent(source);

        Assert.NotNull(content);
        // Should have the interface but no carrier struct for the open parameter N
        Assert.Contains("public interface IConstInt", content);
        Assert.DoesNotContain("Const_Int_N", content);
    }

    [Fact]
    public void EmitsCarrierStructsAlongsideInterfaces()
    {
        var source = "class Buffer<Size:int> { }\nclass Fixed : Buffer<42> { }";
        var content = CompileAndGetHelpersContent(source);

        Assert.NotNull(content);
        // Both interface and carrier struct should be in the same file
        Assert.Contains("public interface IConstInt", content);
        Assert.Contains("public struct Const_Int_42 : IConstInt", content);
    }

    [Fact]
    public void EmitsCarrierStructForZero()
    {
        var source = "class Buffer<Size:int> { }\nclass Empty : Buffer<0> { }";
        var content = CompileAndGetHelpersContent(source);

        Assert.NotNull(content);
        Assert.Contains("public struct Const_Int_0 : IConstInt", content);
        Assert.Contains("public static int Value => 0;", content);
    }

    [Fact]
    public void NamingConventionUsesCapitalizedType()
    {
        var source = "class A<V:long> { }\nclass B : A<100> { }";
        var content = CompileAndGetHelpersContent(source);

        Assert.NotNull(content);
        Assert.Contains("public struct Const_Long_100 : IConstLong", content);
        Assert.Contains("public static long Value => 100L;", content);
    }
}