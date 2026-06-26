using System.Diagnostics;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Lexing;
using Mist.Parsing;
using Xunit.Abstractions;

namespace Mist.Tests.Parsing;

/// <summary>
/// Measures lexer + parser time for types with increasing numbers of type parameters.
/// Detects exponential parsing behavior in TypeArgumentList / TypeParameterList handling.
/// </summary>
public sealed class TypeArgumentParsingPerformanceTest
{
    private readonly ITestOutputHelper output;

    public TypeArgumentParsingPerformanceTest(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void TypeParameterParsing_ShouldNotGrowExponentially()
    {
        var lexer = new MistLexer();
        var parser = new MistParser();
        var maxN = 20;
        var timeoutMs = 10_000; // 10 seconds bail-out

        output.WriteLine($"{"N",-4} {"Time (ms)",-12} {"Ratio",8}");
        output.WriteLine(new string('-', 30));

        double? previousMs = null;
        var bailedAt = -1;

        for(var n = 0; n <= maxN; n++)
        {
            var source = GenerateClassWithNTypeParams(n);
            var sourceText = new SourceText("perf_test.mist", source);
            var diagnostics = new DiagnosticBag();

            var sw = Stopwatch.StartNew();
            var tokens = lexer.Lex(sourceText, diagnostics);
            _ = parser.Parse(tokens, diagnostics);
            sw.Stop();

            var elapsedMs = sw.Elapsed.TotalMilliseconds;
            var ratio = previousMs.HasValue && previousMs.Value > 0.01
                ? $"{elapsedMs / previousMs.Value:F2}x"
                : "—";

            output.WriteLine($"{n,-4} {elapsedMs,-12:F2} {ratio,8}");

            if(elapsedMs > timeoutMs)
            {
                bailedAt = n;
                output.WriteLine($"\nBailed out at N={n} (exceeded {timeoutMs}ms)");
                break;
            }

            previousMs = elapsedMs;
        }

        // If we bailed before N=16, there's a serious exponential issue.
        // A healthy parser should handle 20 type params in well under 1 second.
        if(bailedAt >= 0)
        {
            Assert.Fail(
                $"Parser took >{timeoutMs}ms at N={bailedAt} type parameters. " +
                "This indicates exponential parsing complexity in TypeArgumentList/TypeParameterList.");
        }

        output.WriteLine("\nCompleted all 0..20 within timeout.");
    }

    /// <summary>
    /// Tests parsing performance with arity-overloaded Func types and type aliases
    /// referencing them (the Action pattern). This reproduces the scenario where
    /// defining 16 Func variants + 16 Action type aliases caused exponential slowdown.
    ///
    /// ROOT CAUSE: TypeReference tries UnionTypeRef first, which parses a full
    /// SingleTypeRef (including nested TypeArgumentLists), then fails when no PIPE
    /// follows and backtracks the entire parse. The non-union alternatives then
    /// re-parse the same content from scratch. This doubles the work per type argument,
    /// compounding to ~3.5x exponential growth per additional type parameter.
    /// </summary>
    [Fact]
    public void FuncActionArityOverloads_ShouldNotGrowExponentially()
    {
        var lexer = new MistLexer();
        var parser = new MistParser();
        var maxN = 16;
        var timeoutMs = 10_000;

        output.WriteLine($"{"N",-4} {"Time (ms)",-12} {"Ratio",8}");
        output.WriteLine(new string('-', 30));

        double? previousMs = null;
        var bailedAt = -1;

        for(var n = 0; n <= maxN; n++)
        {
            var source = GenerateFuncActionSource(n);
            var sourceText = new SourceText("perf_test.mist", source);
            var diagnostics = new DiagnosticBag();

            var sw = Stopwatch.StartNew();
            var tokens = lexer.Lex(sourceText, diagnostics);
            _ = parser.Parse(tokens, diagnostics);
            sw.Stop();

            var elapsedMs = sw.Elapsed.TotalMilliseconds;
            var ratio = previousMs.HasValue && previousMs.Value > 0.01
                ? $"{elapsedMs / previousMs.Value:F2}x"
                : "—";

            output.WriteLine($"{n,-4} {elapsedMs,-12:F2} {ratio,8}");

            if(elapsedMs > timeoutMs)
            {
                bailedAt = n;
                output.WriteLine($"\nBailed out at N={n} (exceeded {timeoutMs}ms)");
                break;
            }

            previousMs = elapsedMs;
        }

        if(bailedAt >= 0)
        {
            Assert.Fail(
                $"Parser took >{timeoutMs}ms at N={bailedAt} Func/Action arity overloads. " +
                "This indicates exponential parsing complexity with arity-overloaded type aliases.");
        }

        output.WriteLine("\nCompleted all 0..16 within timeout.");
    }

    /// <summary>
    /// Generates a minimal Mist program with a class that has N type parameters
    /// and a field using that class with N type arguments.
    /// </summary>
    private static string GenerateClassWithNTypeParams(int n)
    {
        if(n == 0)
        {
            return "namespace Test;\nclass Foo {\n    public int Value;\n}\nclass Program {\n    public Foo field;\n}\n";
        }

        var typeParams = string.Join(", ", Enumerable.Range(1, n).Select(i => $"T{i}"));
        var typeArgs = string.Join(", ", Enumerable.Range(1, n).Select(_ => "int"));

        return $"namespace Test;\nclass Foo<{typeParams}> {{\n    public int Value;\n}}\nclass Program {{\n    public Foo<{typeArgs}> field;\n}}\n";
    }

    /// <summary>
    /// Generates a source file with N Func arity overloads (intrinsic classes)
    /// and N corresponding Action type aliases. This mirrors the first-class
    /// functions pattern:
    ///   intrinsic class Func&lt;TResult&gt; { }
    ///   intrinsic class Func&lt;T1, TResult&gt; { }
    ///   ...
    ///   type Action = Func&lt;void&gt;;
    ///   type Action&lt;T1&gt; = Func&lt;T1, void&gt;;
    ///   ...
    /// At N=0, generates just the base Func&lt;TResult&gt; and Action alias.
    /// At N=k, generates Func with 0..k input params and matching Action aliases.
    /// </summary>
    private static string GenerateFuncActionSource(int n)
    {
        var sb = new System.Text.StringBuilder();
        _ = sb.AppendLine("namespace Standard;");
        _ = sb.AppendLine();

        // Generate Func<TResult>, Func<T1, TResult>, ..., Func<T1..Tn, TResult>
        for(var i = 0; i <= n; i++)
        {
            if(i == 0)
            {
                _ = sb.AppendLine("intrinsic class Func<TResult> { }");
            }
            else
            {
                var inputParams = string.Join(", ", Enumerable.Range(1, i).Select(j => $"T{j}"));
                _ = sb.AppendLine($"intrinsic class Func<{inputParams}, TResult> {{ }}");
            }
        }

        _ = sb.AppendLine();

        // Generate type Action = Func<void>, type Action<T1> = Func<T1, void>, ...
        for(var i = 0; i <= n; i++)
        {
            if(i == 0)
            {
                _ = sb.AppendLine("type Action = Func<void>;");
            }
            else
            {
                var aliasParams = string.Join(", ", Enumerable.Range(1, i).Select(j => $"T{j}"));
                var funcArgs = string.Join(", ", Enumerable.Range(1, i).Select(j => $"T{j}"));
                _ = sb.AppendLine($"type Action<{aliasParams}> = Func<{funcArgs}, void>;");
            }
        }

        return sb.ToString();
    }
}