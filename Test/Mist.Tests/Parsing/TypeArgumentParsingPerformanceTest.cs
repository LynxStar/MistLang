using System.Diagnostics;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Lexing;
using Mist.Parsing;
using Xunit.Abstractions;

namespace Mist.Tests.Parsing;

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

        // A healthy parser handles 20 type parameters in well under a second; the bail-out only
        // catches exponential growth.
        if(bailedAt >= 0)
        {
            Assert.Fail(
                $"Parser took >{timeoutMs}ms at N={bailedAt} type parameters. " +
                "This indicates exponential parsing complexity in TypeArgumentList/TypeParameterList.");
        }

        output.WriteLine("\nCompleted all 0..20 within timeout.");
    }

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

    private static string GenerateFuncActionSource(int n)
    {
        var sb = new System.Text.StringBuilder();
        _ = sb.AppendLine("namespace Standard;");
        _ = sb.AppendLine();

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