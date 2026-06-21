// Task 7.6: Unit tests for enum emission edge cases
// Validates: Requirements 9.5, 10.3, 10.4

using System.Collections.Immutable;
using System.Text;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Driver.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests for enum emission edge cases:
/// - Union underlying type uses helper struct name (Requirement 9.5)
/// - Default visibility emits as internal (Requirement 10.3)
/// - Enum members always publicly accessible (Requirement 10.4)
///
/// Uses in-memory compilation via <see cref="CSharpEmitter.EmitToMemory"/>
/// (no disk I/O, no process spawning).
///
/// **Validates: Requirements 9.5, 10.3, 10.4**
/// </summary>
public sealed class EnumEmissionTests
{
    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder ModelBuilder = new();
    private static readonly TypeCheckerAnalyzer TypeChecker = new();

    // ─── In-memory compilation helper ────────────────────────────────────

    private static string CompileAndGetEmittedCSharp(string mistSource)
    {
        var sourceText = new SourceText("test.mist", mistSource);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));

        // Lex
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        Assert.False(lexBag.HasErrors, $"Lex errors: {FormatDiagnostics(lexBag)}");

        // Parse
        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        Assert.False(parseBag.HasErrors, $"Parse errors: {FormatDiagnostics(parseBag)}");
        Assert.NotNull(parseResult.Root);

        // Semantic model
        var semBag = new DiagnosticBag();
        var model = ModelBuilder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProgram",
            projectFolder: "/test",
            semBag);
        Assert.False(semBag.HasErrors, $"Semantic errors: {FormatDiagnostics(semBag)}");

        // Type checker
        var tcBag = new DiagnosticBag();
        TypeChecker.Analyze(model, tcBag);
        var tcErrors = tcBag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.Empty(tcErrors);

        // Emit in-memory
        var emitBag = new DiagnosticBag();
        var files = CSharpEmitter.EmitToMemory(model, "TestProgram", emitBag);
        Assert.False(emitBag.HasErrors, $"Emit errors: {FormatDiagnostics(emitBag)}");

        var sb = new StringBuilder();
        foreach (var (_, content) in files)
            sb.AppendLine(content);

        return sb.ToString();
    }

    private static string FormatDiagnostics(DiagnosticBag bag)
    {
        return string.Join("\n", bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}"));
    }

    // ─── Tests ───────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that when the underlying type is a union type, the emitted C#
    /// uses the union helper struct name (e.g., Union_Int32_String) as the
    /// Value parameter type.
    ///
    /// **Validates: Requirement 9.5**
    /// </summary>
    [Fact]
    public void UnionUnderlyingType_UsesHelperStructName()
    {
        var mistSource = """
            enum Result : int | string {
                Ok = 42,
                Error = "failed"
            }
            class Program {
                static void Main() {
                }
            }
            """;

        var emittedCSharp = CompileAndGetEmittedCSharp(mistSource);

        // Without stdlib, primitive types resolve with keyword names (int, string)
        // rather than CLR names (Int32, String). The union helper name uses the
        // type's DisplayName, which is the keyword name in this context.
        Assert.Contains("Union_int_string", emittedCSharp);

        // The record class should use the union helper as the Value type
        Assert.Contains("record class Result(Union_int_string Value)", emittedCSharp);

        // The implicit operator should convert to the union helper type
        Assert.Contains("public static implicit operator Union_int_string(Result e) => e.Value;", emittedCSharp);
    }

    /// <summary>
    /// Verifies that an enum declared without an access modifier emits as
    /// a record class without an explicit access modifier (C# defaults to internal
    /// for top-level types).
    ///
    /// **Validates: Requirement 10.3**
    /// </summary>
    [Fact]
    public void DefaultVisibility_EmitsWithoutExplicitModifier()
    {
        var mistSource = """
            enum Direction : int {
                North = 0,
                South = 1
            }
            class Program {
                static void Main() {
                }
            }
            """;

        var emittedCSharp = CompileAndGetEmittedCSharp(mistSource);

        // Should contain "record class Direction" without a preceding access modifier
        Assert.Contains("record class Direction(int Value)", emittedCSharp);

        // Should NOT contain "public record class Direction" or "private record class Direction"
        Assert.DoesNotContain("public record class Direction", emittedCSharp);
        Assert.DoesNotContain("private record class Direction", emittedCSharp);
        Assert.DoesNotContain("internal record class Direction", emittedCSharp);
    }

    /// <summary>
    /// Verifies that enum members are always emitted as <c>public static readonly</c>
    /// regardless of the enum's own visibility modifier.
    ///
    /// **Validates: Requirement 10.4**
    /// </summary>
    [Theory]
    [InlineData("public")]
    [InlineData("private")]
    [InlineData("")]
    public void EnumMembers_AlwaysPublicStaticReadonly(string modifier)
    {
        var modifierPrefix = string.IsNullOrEmpty(modifier) ? "" : $"{modifier} ";
        var mistSource = modifierPrefix + """
            enum Status : int {
                Active = 1,
                Inactive = 0
            }
            class Program {
                static void Main() {
                }
            }
            """;

        var emittedCSharp = CompileAndGetEmittedCSharp(mistSource);

        // Both members should be public static readonly regardless of enum visibility
        Assert.Contains("public static readonly Status Active = new Status(1);", emittedCSharp);
        Assert.Contains("public static readonly Status Inactive = new Status(0);", emittedCSharp);

        // Members should NOT have private or internal modifiers
        Assert.DoesNotContain("private static readonly Status", emittedCSharp);
        Assert.DoesNotContain("internal static readonly Status", emittedCSharp);
    }

    /// <summary>
    /// Verifies that a public enum emits with the public access modifier.
    ///
    /// **Validates: Requirement 10.1**
    /// </summary>
    [Fact]
    public void PublicEnum_EmitsWithPublicModifier()
    {
        var mistSource = """
            public enum Color : string {
                Red = "red",
                Blue = "blue"
            }
            class Program {
                static void Main() {
                }
            }
            """;

        var emittedCSharp = CompileAndGetEmittedCSharp(mistSource);

        Assert.Contains("public record class Color(string Value)", emittedCSharp);
    }

    /// <summary>
    /// Verifies that a private enum emits with the private access modifier.
    ///
    /// **Validates: Requirement 10.2**
    /// </summary>
    [Fact]
    public void PrivateEnum_EmitsWithPrivateModifier()
    {
        var mistSource = """
            private enum Priority : int {
                High = 1,
                Low = 0
            }
            class Program {
                static void Main() {
                }
            }
            """;

        var emittedCSharp = CompileAndGetEmittedCSharp(mistSource);

        Assert.Contains("private record class Priority(int Value)", emittedCSharp);
    }

    /// <summary>
    /// Verifies that a name-only enum (no explicit values) emits with byte
    /// as the underlying type and cast expressions for member values.
    ///
    /// **Validates: Requirements 9.1, 9.2**
    /// </summary>
    [Fact]
    public void NameOnlyEnum_EmitsWithByteUnderlyingType()
    {
        var mistSource = """
            public enum Mode {
                Fast,
                Slow
            }
            class Program {
                static void Main() {
                }
            }
            """;

        var emittedCSharp = CompileAndGetEmittedCSharp(mistSource);

        // Underlying type should be byte for name-only enums
        Assert.Contains("public record class Mode(byte Value)", emittedCSharp);

        // Members should use (byte) cast for their values
        Assert.Contains("public static readonly Mode Fast = new Mode((byte)0);", emittedCSharp);
        Assert.Contains("public static readonly Mode Slow = new Mode((byte)1);", emittedCSharp);

        // Implicit operator should convert to byte
        Assert.Contains("public static implicit operator byte(Mode e) => e.Value;", emittedCSharp);
    }
}
