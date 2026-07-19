// Feature: variadics - Variadic abstract type implementation checking (Task 17.4)
// Requirements: 22.2
//
// A variadic abstract type may be implemented at a concrete arity (where its variadic
// member slots monomorphize to that arity) or generically (where the implementer spreads
// its own pack into the base-type list). This suite verifies both paths.
//
// Concrete-arity implementation:
//   - implementing IApplier<int, int, int> (TArgs=[int,int], TResult=int) requires a method
//     int Apply(int a, int b) -- accepted when present (no MIST_SM_0022), rejected when
//     the method name is entirely missing (MIST_SM_0022 emitted).
//   - The existing `ValidateAbstractSatisfaction` suppresses signature-mismatch false positives
//     for generic abstracts (which includes variadic abstracts), so the check reduces to
//     "does a method with the required name exist."
//
// Generic implementation:
//   - class Boxed<params TArgs> : IApplier<TArgs..., int> is accepted at declaration (no
//     diagnostic), because the base-type-list spread is accepted (Section 13.2) and the
//     generic body checking defers to instantiation.
//
// Concrete-arity tests use VariadicsCompilerFixture.CompileDiagnosticIds (full pipeline,
// Library output type). Generic-implementation tests use AnalyzeClassic (semantic model +
// TypeCheckerAnalyzer only, no backend) because backend monomorphization (task 19) is not
// yet implemented. ZERO external process spawns.

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// Task 17.4 tests for variadic abstract type implementation checking (Requirement 22.2).
/// Asserts that a concrete-arity implementation of a variadic abstract type is accepted
/// when the method with the required name is present, rejected when the method name is
/// missing, and that a generic implementation (base-type-list spread) is accepted.
/// </summary>
public sealed class VariadicsAbstractImplementationTests
{
    private const string MissingAbstractOverloadId = "MIST_SM_0022";

    // =====================================================================
    // Requirement 22.2: concrete-arity implementation -- accepted when
    // the implementing type provides the required method name.
    // =====================================================================

    [Fact]
    public void ConcreteArity_MethodPresent_NoDiagnostic()
    {
        // IApplier<params TArgs, TResult> declares TResult Apply(params TArgs args).
        // Implementing IApplier<int, int, int> means TArgs=[int,int], TResult=int.
        // The implementing class provides int Apply(int a, int b) -- satisfied.
        var ids = VariadicsCompilerFixture.CompileDiagnosticIds(
            """
            public abstract IApplier<params TArgs, TResult>
            {
                TResult Apply(params TArgs args);
            }

            public class Adder : IApplier<int, int, int>
            {
                public int Apply(int a, int b)
                {
                    return a + b;
                }
            }
            """);

        Assert.DoesNotContain(MissingAbstractOverloadId, ids);
    }

    [Fact]
    public void ConcreteArity_SingleParam_MethodPresent_NoDiagnostic()
    {
        // Implementing IApplier<string, int> means TArgs=[string], TResult=int.
        // The implementing class provides int Apply(string s) -- satisfied.
        var ids = VariadicsCompilerFixture.CompileDiagnosticIds(
            """
            public abstract IApplier<params TArgs, TResult>
            {
                TResult Apply(params TArgs args);
            }

            public class Lengther : IApplier<string, int>
            {
                public int Apply(string s)
                {
                    return 0;
                }
            }
            """);

        Assert.DoesNotContain(MissingAbstractOverloadId, ids);
    }

    // =====================================================================
    // Requirement 22.2: concrete-arity implementation -- MIST_SM_0022 when
    // the required method name is entirely missing.
    // =====================================================================

    [Fact]
    public void ConcreteArity_MethodMissing_EmitsDiagnostic()
    {
        // The implementing class does NOT provide any method named Apply.
        var ids = VariadicsCompilerFixture.CompileDiagnosticIds(
            """
            public abstract IApplier<params TArgs, TResult>
            {
                TResult Apply(params TArgs args);
            }

            public class Empty : IApplier<int, int, int>
            {
            }
            """);

        Assert.Contains(MissingAbstractOverloadId, ids);
    }

    [Fact]
    public void ConcreteArity_WrongMethodName_EmitsDiagnostic()
    {
        // The implementing class provides a method, but not named "Apply".
        var ids = VariadicsCompilerFixture.CompileDiagnosticIds(
            """
            public abstract IApplier<params TArgs, TResult>
            {
                TResult Apply(params TArgs args);
            }

            public class Wrong : IApplier<int, int, int>
            {
                public int Run(int a, int b)
                {
                    return a + b;
                }
            }
            """);

        Assert.Contains(MissingAbstractOverloadId, ids);
    }

    // =====================================================================
    // Requirement 22.2: generic implementation -- the implementer spreads
    // its own pack into the base-type list. Accepted at declaration.
    // Uses AnalyzeClassic (semantic model + TypeCheckerAnalyzer only)
    // because backend monomorphization for symbolic packs is task 19.
    // =====================================================================

    [Fact]
    public void GenericImplementation_SpreadIntoBaseList_Accepted()
    {
        // class Boxed<params TArgs> : IApplier<TArgs..., int> is accepted.
        // The generic body defers checking to instantiation (provable forwarding).
        var (buildDiags, analyzerDiags) = AnalyzeClassic(
            """
            public abstract IApplier<params TArgs, TResult>
            {
                TResult Apply(params TArgs args);
            }

            public class Boxed<params TArgs> : IApplier<TArgs..., int>
            {
                public int Apply(params TArgs args)
                {
                    return 0;
                }
            }
            """);

        Assert.DoesNotContain(buildDiags, d => d.Id.Value == MissingAbstractOverloadId);
        Assert.DoesNotContain(analyzerDiags, d => d.Id.Value == MissingAbstractOverloadId);
    }

    [Fact]
    public void GenericImplementation_MethodMissing_EmitsDiagnostic()
    {
        // Even for a generic implementation, if the method name is entirely
        // missing, MIST_SM_0022 should fire.
        var (buildDiags, _) = AnalyzeClassic(
            """
            public abstract IApplier<params TArgs, TResult>
            {
                TResult Apply(params TArgs args);
            }

            public class BadBoxed<params TArgs> : IApplier<TArgs..., int>
            {
            }
            """);

        Assert.Contains(buildDiags, d => d.Id.Value == MissingAbstractOverloadId);
    }

    // =====================================================================
    // Multiple abstract methods: all must be satisfied at the method-name level.
    // =====================================================================

    [Fact]
    public void ConcreteArity_MultipleMethodsAllPresent_NoDiagnostic()
    {
        var ids = VariadicsCompilerFixture.CompileDiagnosticIds(
            """
            public abstract IProcessor<params TArgs, TResult>
            {
                TResult Process(params TArgs args);
                void Reset();
            }

            public class Proc : IProcessor<int, string, bool>
            {
                public bool Process(int a, string b)
                {
                    return true;
                }

                public void Reset() { }
            }
            """);

        Assert.DoesNotContain(MissingAbstractOverloadId, ids);
    }

    [Fact]
    public void ConcreteArity_OneMethodMissing_EmitsDiagnosticForMissing()
    {
        var ids = VariadicsCompilerFixture.CompileDiagnosticIds(
            """
            public abstract IProcessor<params TArgs, TResult>
            {
                TResult Process(params TArgs args);
                void Reset();
            }

            public class PartialProc : IProcessor<int, string, bool>
            {
                public bool Process(int a, string b)
                {
                    return true;
                }
            }
            """);

        // Only one method is missing (Reset), so the diagnostic should appear.
        Assert.Contains(MissingAbstractOverloadId, ids);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    /// <summary>
    /// Lexes and parses <paramref name="source"/>, builds a classic semantic model with the stdlib
    /// (<see cref="StdlibTestHelper.BuildModelWithStdlib"/>), runs <see cref="TypeCheckerAnalyzer"/>
    /// against it, and returns both the build diagnostics and analyzer diagnostics separately.
    /// No backend runs and no external process is spawned.
    /// </summary>
    private static (ImmutableArray<Diagnostic> BuildDiags, ImmutableArray<Diagnostic> AnalyzerDiags) AnalyzeClassic(string source)
    {
        var lexer = new MistLexer();
        var parser = new MistParser();
        var parseDiagnostics = new DiagnosticBag();

        var sourceText = new SourceText("test.mist", source);
        var tokens = lexer.Lex(sourceText, parseDiagnostics);
        var parseResult = parser.Parse(tokens, parseDiagnostics);

        Assert.NotNull(parseResult.Root);

        var parseErrors = parseDiagnostics.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(
            parseErrors.Count == 0,
            $"Unexpected parse errors: {string.Join("; ", parseErrors.Select(d => $"{d.Id.Value}: {d.Message}"))}");

        var (model, buildDiags) = StdlibTestHelper.BuildModelWithStdlib(parseResult.Root!, source: source);
        var analyzerDiagnostics = new DiagnosticBag();
        new TypeCheckerAnalyzer().Analyze(model, analyzerDiagnostics);

        return (buildDiags, analyzerDiagnostics.ToImmutableArray());
    }
}