// attributes-and-test-command Property 2, Requirement 1.10.

using System.Text;
using FsCheck;
using FsCheck.Fluent;
using Mist.Common.Diagnostics;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class DeclarationPositionDisambiguationPropertyTests
{
    private const int IterationCount = 100;

#pragma warning disable CA1034
    public record AttributeAnnotation(string Name, string? Argument)
    {
        public override string ToString()
        {
            return Argument is not null ? $"[{Name}({Argument})]" : $"[{Name}]";
        }
    }

    public enum BracketExprKind
    {
        ArrayLiteral,
        IndexAccess,
        NewArray,
    }

    public record BracketExpression(BracketExprKind Kind, string Code)
    {
        public override string ToString()
        {
            return $"[{Kind}] {Code}";
        }
    }

    public record DisambiguationScenario(
        IReadOnlyList<AttributeAnnotation> ClassAttributes,
        IReadOnlyList<AttributeAnnotation> MethodAttributes,
        IReadOnlyList<BracketExpression> BodyExpressions)
    {
        public override string ToString()
        {
            return $"ClassAttrs={ClassAttributes.Count}, " +
                   $"MethodAttrs={MethodAttributes.Count}, " +
                   $"BodyExprs={BodyExpressions.Count}";
        }
    }
#pragma warning restore CA1034

    private static readonly string[] AttrNames =
        ["Serializable", "Obsolete", "Fact", "Theory", "Required", "Display"];

    private static readonly string[] AttrStringArgs =
        ["\"deprecated\"", "\"use V2\"", "\"test\"", "\"info\""];

    private static readonly string[] AttrIntArgs =
        ["0", "1", "42", "100"];

    private static Gen<AttributeAnnotation> GenAttribute()
    {
        var noArg = Gen.Elements(AttrNames)
            .Select(n => new AttributeAnnotation(n, null));

        var stringArg = from name in Gen.Elements(AttrNames)
                        from arg in Gen.Elements(AttrStringArgs)
                        select new AttributeAnnotation(name, arg);

        var intArg = from name in Gen.Elements(AttrNames)
                     from arg in Gen.Elements(AttrIntArgs)
                     select new AttributeAnnotation(name, arg);

        return Gen.Frequency(
            (2, noArg),
            (2, stringArg),
            (1, intArg));
    }

    private static Gen<BracketExpression> GenBracketExpression()
    {
        var arrayLiteral = from count in Gen.Choose(1, 5)
                           from values in Gen.ArrayOf(Gen.Choose(0, 99), count)
                           let code = $"var arr{count} = [{string.Join(", ", values)}];"
                           select new BracketExpression(BracketExprKind.ArrayLiteral, code);

        var indexAccess = from idx in Gen.Choose(0, 4)
                          let code = $"var val = nums[{idx}];"
                          select new BracketExpression(BracketExprKind.IndexAccess, code);

        var newArray = from size in Gen.Choose(1, 10)
                       let code = $"var buf = new int[{size}];"
                       select new BracketExpression(BracketExprKind.NewArray, code);

        return Gen.Frequency(
            (3, arrayLiteral),
            (2, indexAccess),
            (2, newArray));
    }

    private static Gen<DisambiguationScenario> GenScenario()
    {
        return from classAttrCount in Gen.Choose(1, 3)
               from methodAttrCount in Gen.Choose(1, 3)
               from bodyExprCount in Gen.Choose(1, 4)
               from classAttrs in Gen.ArrayOf(GenAttribute(), classAttrCount)
               from methodAttrs in Gen.ArrayOf(GenAttribute(), methodAttrCount)
               from bodyExprs in Gen.ArrayOf(GenBracketExpression(), bodyExprCount)
               select new DisambiguationScenario(classAttrs, methodAttrs, bodyExprs);
    }

    private static string BuildProgram(DisambiguationScenario scenario, int index)
    {
        var sb = new StringBuilder();
        _ = sb.AppendLine($"namespace DisambigTest{index};");
        _ = sb.AppendLine();

        foreach(var attr in scenario.ClassAttributes)
        {
            _ = sb.AppendLine(attr.ToString());
        }

        _ = sb.AppendLine($"class Target{index}");
        _ = sb.AppendLine("{");

        foreach(var attr in scenario.MethodAttributes)
        {
            _ = sb.AppendLine($"    {attr}");
        }

        _ = sb.AppendLine($"    public void Run()");
        _ = sb.AppendLine("    {");

        // Always declare a nums array so index access expressions have something to index
        _ = sb.AppendLine("        var nums = new int[5];");

        foreach(var expr in scenario.BodyExpressions)
        {
            _ = sb.AppendLine($"        {expr.Code}");
        }

        _ = sb.AppendLine("    }");
        _ = sb.AppendLine("}");

        return sb.ToString();
    }

    private static IReadOnlyList<Diagnostic> ParseErrors(string mistSource)
    {
        return VariadicsCompilerFixture.Parse(mistSource).Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => d.Stage is PipelineStage.Parsing or PipelineStage.Lexing)
            .ToList();
    }

    [Fact]
    [Trait("Tier", "3")]
    [Trait("Category", "PropertyBased")]
    public void DeclarationPosition_Disambiguation_NoParseErrors()
    {
        var scenarios = GenScenario().Sample(IterationCount).ToList();

        var failures = new List<string>();

        for(var i = 0; i < scenarios.Count; i++)
        {
            var scenario = scenarios[i];
            var source = BuildProgram(scenario, i);

            var parseErrors = ParseErrors(source);

            if(parseErrors.Count > 0)
            {
                var errorDetails = string.Join("\n",
                    parseErrors.Select(d => $"  {d.Stage} {d.Number.Value}: {d.Message} at {d.Location}"));

                failures.Add(
                    $"Scenario {i} had parse errors (disambiguation failure):\n" +
                    $"  Scenario: {scenario}\n" +
                    $"  Errors:\n{errorDetails}\n" +
                    $"  Source:\n{source}");
            }
        }

        if(failures.Count > 0)
        {
            var firstFailure = failures[0];
            throw new Exception(
                $"Property failed: {failures.Count}/{scenarios.Count} scenarios had parse errors.\n\n" +
                $"First failure:\n{firstFailure}");
        }
    }
}