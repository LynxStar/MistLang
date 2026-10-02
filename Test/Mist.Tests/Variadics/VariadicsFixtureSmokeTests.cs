using FsCheck;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Tests.Generators;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// Smoke tests validating the task 1.3 infrastructure itself: the shared parse-only
/// <see cref="VariadicsCompilerFixture"/> and the <see cref="MistVariadicsGenerator"/>
/// surface-form generators. These confirm that every representative and generated surface
/// form is grammar-valid. Assertions about specific AST node kinds and print/parse
/// round-trips belong to the dedicated parsing and pretty-printer tasks.
/// </summary>
[Trait("Tier", "3")]
public sealed class VariadicsFixtureSmokeTests
{
    /// <summary>Arbitrary registration for the FsCheck property below.</summary>
    public static Arbitrary<VariadicSurfaceScenario> VariadicScenarios()
    {
        return MistVariadicsGenerator.ArbitraryVariadicSurfaceScenario();
    }

    [Fact]
    public void Fixture_ParsesRepresentativeVariadicForms()
    {
        // Representative forms spanning declaration, value, spread, and use-site axes.
        var forms = new[]
        {
            "public class Bundle<params TArgs> { }",
            "public struct ShapePair<Type[2]:TShapes> { }",
            "public class Box<Type[]:A> { }",
            "type Action<params TArgs> = Func<TArgs..., void>;",
            "public class Holder { public void Take<params TArgs>(params TArgs args) { } }",
            "public class Logger { public void Log(params string[] parts) { } }",
            "public class Bundle<params TArgs> { public TArgs... Item; }",
            "public class Tup<params TArgs> : ICountable<TArgs...> { }",
            "public class C { public Box<[int, string]> value; }",
            "public class C { public Box<int, string> value; }",
        };

        foreach(var form in forms)
        {
            var outcome = VariadicsCompilerFixture.Parse(form);
            Assert.True(
                VariadicsCompilerFixture.ParsedCleanly(outcome),
                $"Expected a clean parse for: {form}\nDiagnostics: {DescribeDiagnostics(outcome.Diagnostics)}");
        }
    }

    /// <summary>
    /// Every generated variadic surface form parses without lex/parse errors. This
    /// validates that the generators emit grammar-valid Mist source across all
    /// categories, so downstream property tests can rely on them.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(VariadicsFixtureSmokeTests) }, MaxTest = 200)]
    public bool Generators_ProduceParseableSource(VariadicSurfaceScenario scenario)
    {
        var outcome = VariadicsCompilerFixture.Parse(scenario.Source);
        return VariadicsCompilerFixture.ParsedCleanly(outcome);
    }

    private static string DescribeDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        var lines = diagnostics.Select(d => $"{d.Id.Value} [{d.Stage}] {d.Message}").ToList();
        return lines.Count == 0 ? "(none)" : string.Join("; ", lines);
    }
}