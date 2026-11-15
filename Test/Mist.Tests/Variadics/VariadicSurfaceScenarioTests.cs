using System.Text.RegularExpressions;
using FsCheck;
using FsCheck.Xunit;
using Mist.Tests.Generators;

namespace Mist.Tests.Variadics;

[Trait("Tier", "1")]
public sealed partial class VariadicSurfaceScenarioTests
{
    public static Arbitrary<VariadicSurfaceScenario> VariadicScenarios()
    {
        return MistVariadicsGenerator.ArbitraryVariadicSurfaceScenario();
    }

    [Property(Arbitrary = new[] { typeof(VariadicSurfaceScenarioTests) }, MaxTest = 200)]
    public bool FixedArity_IsTheDeclaredPackLengthOrNull(VariadicSurfaceScenario scenario)
    {
        var sized = SizedPack().Match(scenario.Source);
        int? declared = sized.Success ? int.Parse(sized.Groups[1].Value) : null;
        return scenario.FixedArity == declared;
    }

    [GeneratedRegex(@"Type\[(\d+)\]:")]
    private static partial Regex SizedPack();
}