// Feature: variadics — whole-pack constraints (Requirement 11)
// Unit tests for elementwise constraint checking on type-parameter packs.
// Requirements: 11.1, 11.2, 11.3, 11.4

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Variadics;

/// <summary>
/// Tests that a whole-pack constraint (<c>where TArgs : C</c>) is enforced
/// elementwise on each bound element type. Validates:
/// <list type="bullet">
///   <item>11.1 — every element type independently satisfies the constraint.</item>
///   <item>11.2 — constrained members permitted on each element.</item>
///   <item>11.3 — MIST_VAR_0009 emitted per failing element, error type so analysis continues.</item>
///   <item>11.4 — empty pack vacuously satisfies the constraint.</item>
/// </list>
/// All compilation runs entirely in-process (SemanticModelBuilder + TypeCheckerAnalyzer)
/// with zero external process spawns. No backend emit is invoked.
/// </summary>
public sealed class VariadicsWholePackConstraintTests
{
    private const string DiagnosticId = "MIST_VAR_0009";

    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    // ═══════════════════════════════════════════════════════════════════════
    // 11.4 — Empty pack vacuously satisfies the constraint (no MIST_VAR_0009)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void EmptyPack_VacuouslySatisfies_NoDiagnostic()
    {
        var source = """
            abstract IComparable { }

            public class Bundle<params TArgs> where TArgs : IComparable
            {
            }

            public class Program
            {
                public static void Main()
                {
                    var x = new Bundle<>();
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == DiagnosticId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 11.1 — Single element satisfying constraint (accepted)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SingleElement_SatisfiesConstraint_NoDiagnostic()
    {
        var source = """
            abstract IComparable { }

            struct MyInt : IComparable { }

            public class Bundle<params TArgs> where TArgs : IComparable
            {
            }

            public class Program
            {
                public static void Main()
                {
                    var x = new Bundle<MyInt>();
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == DiagnosticId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 11.3 — Single element violating constraint (MIST_VAR_0009)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SingleElement_ViolatesConstraint_EmitsDiagnostic()
    {
        var source = """
            abstract IComparable { }

            struct NotComparable { }

            public class Bundle<params TArgs> where TArgs : IComparable
            {
            }

            public class Program
            {
                public static void Main()
                {
                    var x = new Bundle<NotComparable>();
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.Contains(diagnostics, d => d.Id.Value == DiagnosticId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 11.3 — Multi-element mix: one MIST_VAR_0009 per failing element
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MultiElement_MixedSatisfaction_EmitsPerFailingElement()
    {
        var source = """
            abstract IComparable { }

            struct Good : IComparable { }
            struct Bad1 { }
            struct Bad2 { }

            public class Bundle<params TArgs> where TArgs : IComparable
            {
            }

            public class Program
            {
                public static void Main()
                {
                    var x = new Bundle<Good, Bad1, Bad2>();
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // Two failing elements (Bad1, Bad2) => two MIST_VAR_0009 diagnostics
        var count = diagnostics.Count(d => d.Id.Value == DiagnosticId);
        Assert.Equal(2, count);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 11.1 — Multi-element all satisfying (no diagnostic)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MultiElement_AllSatisfy_NoDiagnostic()
    {
        var source = """
            abstract IComparable { }

            struct A : IComparable { }
            struct B : IComparable { }
            struct C : IComparable { }

            public class Bundle<params TArgs> where TArgs : IComparable
            {
            }

            public class Program
            {
                public static void Main()
                {
                    var x = new Bundle<A, B, C>();
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == DiagnosticId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 11.1/11.3 — Struct constraint on pack elements
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void StructConstraint_ElementViolates_EmitsDiagnostic()
    {
        var source = """
            class RefType { }
            struct ValType { }

            public class Bundle<params TArgs> where TArgs : struct
            {
            }

            public class Program
            {
                public static void Main()
                {
                    var x = new Bundle<ValType, RefType>();
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // RefType violates struct constraint => 1 MIST_VAR_0009
        Assert.Contains(diagnostics, d => d.Id.Value == DiagnosticId);
        Assert.Equal(1, diagnostics.Count(d => d.Id.Value == DiagnosticId));
    }

    [Fact]
    public void StructConstraint_AllSatisfy_NoDiagnostic()
    {
        var source = """
            struct A { }
            struct B { }

            public class Bundle<params TArgs> where TArgs : struct
            {
            }

            public class Program
            {
                public static void Main()
                {
                    var x = new Bundle<A, B>();
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == DiagnosticId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);

        if(parseResult.Root is null)
        {
            return lexBag.ToImmutableArray().AddRange(parseBag.ToImmutableArray());
        }

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semBag = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        var tcBag = new DiagnosticBag();
        Analyzer.Analyze(model, tcBag);

        return semBag.ToImmutableArray().AddRange(tcBag.ToImmutableArray());
    }
}