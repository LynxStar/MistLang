// Tier 3 (Component): a newtype over a readonly intrinsic Standard type is that type's declaration cloned
// and renamed, an intrinsic contract of its own around one backing value (newtypes spec Requirements 3.2,
// 3.8 and 5.2), asserted on a model built over the real Standard sources, since a shell has no declaration to
// clone. What it emits is IntrinsicCloneEmissionTests'.

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Semantics;

public sealed class IntrinsicCloneTests
{
    private const string Newtypes = """
        public type Age = new int;
        public type Label = new string;
        public type Meters = new double;
        public type Feet = new double;

        public class Sink
        {
            public static int Take(int value) { return 1; }
            public static int Take(string value) { return 2; }

            public static int TakeAge(Age age) { return 1; }
            public static int TakeAge(string s) { return 2; }
        }
        """;

    private const string Locals = """
        var age = (Age)30;
        var label = (Label)"hello";
        var feet = (Feet)2.0;
        """;

    [Fact]
    public void ANewtypeOverAnIntrinsic_IsAnIntrinsicCloneHoldingTheBackingValue()
    {
        var model = Build("", out var diagnostics);
        Assert.Empty(Errors(diagnostics));

        var age = Assert.IsType<StructType>(model.TypesByFQN["Probe.Age"]);
        Assert.Equal("Standard.Int32", age.CloneSource?.FullyQualifiedName);
        Assert.True(age.IsIntrinsic);
        Assert.Equal("Standard.Int32", age.FieldsList.Single(field => field.Name == "Value").Type.FullyQualifiedName);
        _ = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Label"]);
    }

    [Theory]
    [InlineData("var older = age + age;", "Probe.Age")]
    [InlineData("var shout = label.ToUpper();", "Probe.Label")]
    [InlineData("var length = label.Length;", "Standard.Int32")]
    [InlineData("var even = age.IsEven();", "Standard.Boolean")]
    [InlineData("var sum = age + 1;", "Standard.Int32")]
    [InlineData("var raw = age.Value;", "Standard.Int32")]
    [InlineData("var entered = (Age)7;", "Probe.Age")]
    [InlineData("var left = (int)age;", "Standard.Int32")]
    public void AMemberOfTheIntrinsic_IsRenamedOnTheClone(string statement, string expected)
    {
        var model = Build(statement, out var diagnostics);
        Assert.Empty(Errors(diagnostics));

        var run = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]).MethodsList.Single(method => method.Name == "Run");
        var declared = run.Body!.Statements.OfType<LocalDeclarationStatement>().Last();

        Assert.Equal(expected, declared.Local.Type.FullyQualifiedName);
    }

    [Theory]
    [InlineData("long wide = age;")]
    [InlineData("string plain = label;")]
    [InlineData("Meters crossed = (Meters)feet;")]
    [InlineData("var distances = new Meters[3];")]
    public void AnIntrinsicCloneLeavesFreelyCrossesByCastAndStamps(string statement)
    {
        var model = Build(statement, out var buildDiagnostics);
        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        Assert.Empty(Errors(buildDiagnostics.AddRange(findings.ToImmutableArray())));
    }

    [Theory]
    [InlineData("var chosen = Sink.Take(age);", "Standard.Int32")]
    [InlineData("var chosen = Sink.TakeAge(age);", "Probe.Age")]
    public void ANewtypeArgument_ReachesItsSourceAndPrefersItself(string statement, string expected)
    {
        var model = Build(statement, out var diagnostics);
        Assert.Empty(Errors(diagnostics));

        var run = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]).MethodsList.Single(method => method.Name == "Run");
        var invocation = Assert.IsType<InvocationExpression>(
            run.Body!.Statements.OfType<LocalDeclarationStatement>().Last().Initializer);

        Assert.Equal(expected, Assert.Single(invocation.Method!.ParametersList).Type.FullyQualifiedName);
    }

    [Fact]
    public void AnIntrinsicClone_IsWithinItsSourceAndNeverAnotherNewtypeOverIt()
    {
        var model = Build("", out _);
        var age = model.TypesByFQN["Probe.Age"];
        var int32 = model.TypesByFQN["Standard.Int32"];

        Assert.True(ConversionRelation.IsWithin(age, int32));
        Assert.False(ConversionRelation.IsWithin(int32, age));
        Assert.False(ConversionRelation.IsWithin(model.TypesByFQN["Probe.Meters"], model.TypesByFQN["Probe.Feet"]));
    }

    private static ProgramModelClass Build(string statement, out ImmutableArray<Diagnostic> diagnostics)
    {
        return StandardModelProbe.Build($"{Locals}\n{statement}", out diagnostics, Newtypes);
    }

    private static IEnumerable<Diagnostic> Errors(IEnumerable<Diagnostic> diagnostics)
    {
        return diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}