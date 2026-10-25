// Tier 3 (Component): a conversion chain through Standard's intrinsic Regex to String, which is lossless by
// the cast-operators glossary and so never the one user-declared conversion a value may pass through. Built
// over the real Standard sources, since the operator is Standard's own declaration.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class RegexChainSelectionTests
{
    private const string Types = """
        public enum Priority : int { Low = 1, High = 6 }

        public class Label
        {
            public Label() { }

            public static implicit operator Label(string text) { return new Label(); }
        }

        public class Meters
        {
            public Meters() { }

            public static implicit operator Meters(string text) { return new Meters(); }
        }

        public class Feet
        {
            public Feet() { }

            public static explicit operator Feet(Meters m) { return new Feet(); }

            public static explicit operator Feet(int count) { return new Feet(); }
        }

        public class Level
        {
            public Level() { }

            public static implicit operator Level(int value) { return new Level(); }
        }

        public class Grade
        {
            public Grade() { }

            public static explicit operator Grade(Level l) { return new Grade(); }
        }

        public class Pattern
        {
            public Pattern() { }

            public static implicit operator regex(Pattern p) { return `x+`; }
        }

        public class Sink
        {
            public static int Show(Label label) { return 1; }

            public static int Show(int number) { return 2; }
        }
        """;

    [Fact]
    public void ARegexReachesADeclaredOperatorOnString_ThroughTheIntrinsicStep()
    {
        var conversion = Assert.IsType<ImplicitConversionExpression>(Initializer("Label label = pattern;"));

        Assert.Equal(WellKnownTypes.String.FullyQualifiedName, conversion.ParameterType?.FullyQualifiedName);
        Assert.Equal(WellKnownTypes.Regex.FullyQualifiedName, conversion.Operand.Type.FullyQualifiedName);
    }

    [Fact]
    public void ADeclaredOperatorsRegexResultReachesAString_ThroughTheIntrinsicStep()
    {
        var conversion = Assert.IsType<ImplicitConversionExpression>(Initializer("string text = new Pattern();"));

        Assert.Equal(WellKnownTypes.Regex.FullyQualifiedName, conversion.Type.FullyQualifiedName);
        Assert.Equal(WellKnownTypes.String.FullyQualifiedName, conversion.TargetType.FullyQualifiedName);
    }

    [Fact]
    public void AnOverloadTheRegexReachesThroughTheIntrinsicStep_IsApplicable()
    {
        var invocation = Assert.IsType<InvocationExpression>(Initializer("var shown = Sink.Show(pattern);"));

        Assert.Equal("Probe.Label", Assert.Single(invocation.Method!.ParametersList).Type.FullyQualifiedName);
        _ = Assert.IsType<ImplicitConversionExpression>(Assert.Single(invocation.Arguments));
    }

    [Theory]
    [InlineData("var feet = (Feet)pattern;", "Probe.Meters")]
    [InlineData("var grade = (Grade)Priority.High;", "Probe.Level")]
    public void ACastsOperandTakingItsOneDeclaredImplicit_IsRecordedAsThatConversion(string statement, string parameter)
    {
        var cast = Assert.IsType<CastExpression>(Initializer(statement));

        Assert.Equal(parameter, Assert.IsType<ImplicitConversionExpression>(cast.Operand).Type.FullyQualifiedName);
    }

    [Theory]
    [InlineData("Label label = pattern;")]
    [InlineData("string text = new Pattern();")]
    [InlineData("var shown = Sink.Show(pattern);")]
    [InlineData("var feet = (Feet)pattern;")]
    [InlineData("var grade = (Grade)Priority.High;")]
    [InlineData("var plain = (string)pattern;")]
    public void AChainWithOneDeclaredConversionBesideTheIntrinsicStep_IsAdmitted(string statement)
    {
        Assert.Empty(Errors(statement));
    }

    [Fact]
    public void AnImplicitSiteACastWouldReachThroughTheIntrinsicStep_SaysACastExists()
    {
        Assert.Equal("MIST0158", Assert.Single(Errors("Feet needsCast = pattern;")).Number.Value);
    }

    [Fact]
    public void AChainNeedingTwoDeclaredConversions_IsStillRefused()
    {
        Assert.NotEmpty(Errors("Label twice = new Pattern();"));
    }

    private static Expression? Initializer(string statement)
    {
        var model = StandardModelProbe.Build("var pattern = `ab+`; " + statement, out var diagnostics, Types);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var run = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]).MethodsList.Single(method => method.Name == "Run");

        return run.Body!.Statements.OfType<LocalDeclarationStatement>().Last().Initializer;
    }

    private static List<Diagnostic> Errors(string statement)
    {
        var model = StandardModelProbe.Build("var pattern = `ab+`; " + statement, out var diagnostics, Types);
        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        return [.. diagnostics.AddRange(findings.ToImmutableArray()).Where(d => d.Severity == DiagnosticSeverity.Error)];
    }
}