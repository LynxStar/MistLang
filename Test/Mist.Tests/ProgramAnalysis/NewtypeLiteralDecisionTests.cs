// Tier 3 (Component): where a literal enters a newtype without a cast (newtypes spec Requirement 9). Every
// slot Requirement 9.2 names is admitted and recorded as the cast it stands for; every other position is
// refused, a call argument above all. Built over the real Standard sources, the only sources a literal fits.

using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.ProgramAnalysis;

public sealed class NewtypeLiteralDecisionTests
{
    private const string Types = """
        public type Age = new int;
        public type Years = new Age;
        public type Meters = new double;
        public type Label = new string;
        public type Small = new byte;
        public type Id<T> = new int;

        public class Person
        {
            public Age Years = 18;
            public Label Name { get; set; } = "anon";
            public Meters Height => 1;

            public static Age Default() { return 21; }

            default {
                Years = 19
            }
        }

        public class Sink
        {
            public static void Take(Age age) { }
        }
        """;

    [Theory]
    [InlineData("Age declared = 30;")]
    [InlineData("Age assigned = (Age)1; assigned = 31;")]
    [InlineData("Age negative = -5;")]
    [InlineData("Age[] elements = [40, 41];")]
    [InlineData("Meters widened = 2;")]
    [InlineData("Label text = \"x\";")]
    [InlineData("var initialized = new Person { Name = \"Arthur\" };")]
    [InlineData("Id<string> generic = 5;")]
    [InlineData("Id<string>[] genericElements = [6, 7];")]
    public void ALiteralInASlotWhoseNewtypeIsInSight_IsAdmitted(string statement)
    {
        Assert.Empty(Errors(Analyse(statement)));
    }

    [Theory]
    [InlineData("Sink.Take(30);", "MIST_PA_CAST_REQUIRED")]
    [InlineData("Age operand = 30 + 1;", "MIST_PA_CAST_REQUIRED")]
    [InlineData("func<Age> result = () => 30;", "MIST_PA_CAST_REQUIRED")]
    [InlineData("func<Age> returned = () => { return 30; };", "MIST_PA_CAST_REQUIRED")]
    [InlineData("Small tooLarge = 300;", "MIST_PA_NO_IMPLICIT_CONVERSION")]
    public void ALiteralAnywhereElse_IsRefused(string statement, string expected)
    {
        Assert.Equal(expected, Assert.Single(Errors(Analyse(statement))).Id.Value);
    }

    [Fact]
    public void AnAdmittedLiteral_IsTheCastToTheNewtype()
    {
        var model = Build("Age declared = 30;", out _);
        var cast = Assert.IsType<CastExpression>(LastLocal(model).Initializer);

        Assert.True(cast.Operator?.CopiesClone);
        Assert.Equal("Probe.Age", cast.Type.FullyQualifiedName);
    }

    [Fact]
    public void ALiteralAdmittedIntoAGenericNewtype_IsTheCastToThatInstance()
    {
        var model = Build("Id<string> generic = 5;", out _);
        var cast = Assert.IsType<CastExpression>(LastLocal(model).Initializer);

        Assert.True(cast.Operator?.CopiesClone);
        Assert.Equal("Probe.Id<String>", cast.Type.FullyQualifiedName);
    }

    // A newtype over an intrinsic newtype is the intrinsic's contract renamed twice: it wraps the one backing
    // value, while its conversions answer to the newtype it names (newtypes spec Requirements 2.4 and 3.8).
    [Fact]
    [Trait("Tier", "3")]
    public void ANewtypeOverAnIntrinsicNewtype_WrapsTheBackingValueAndConvertsWithItsImmediateSource()
    {
        var model = Build("Years years = 30;", out var diagnostics);
        Assert.Empty(Errors(diagnostics));

        var years = Assert.IsAssignableFrom<ConcreteType>(model.TypesByFQN["Probe.Years"]);
        var admitted = Assert.IsType<CastExpression>(LastLocal(model).Initializer);

        Assert.Equal("Standard.Int32", years.FieldsList.Single(field => field.Name == "Value").Type.FullyQualifiedName);
        Assert.All(
            years.CastOperatorsList.Where(castOperator => castOperator.CopiesClone),
            castOperator => Assert.Contains("Probe.Age", new[] { castOperator.TargetType.FullyQualifiedName, castOperator.SourceParameter.Type.FullyQualifiedName }));
        Assert.Equal("Probe.Years", admitted.Type.FullyQualifiedName);
        Assert.Equal("Probe.Age", Assert.IsType<CastExpression>(admitted.Operand).Type.FullyQualifiedName);
    }

    [Fact]
    public void ANegativeLiteral_EmitsParenthesizedUnderItsCast()
    {
        var model = Build("Age negative = -5;", out _);
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        BodyEmitter.EmitExpression(LastLocal(model).Initializer!, writer, diagnostics);

        Assert.Equal("((Probe.Age)(-5))", writer.ToString());
    }

    private static List<Diagnostic> Analyse(string statement)
    {
        var model = Build(statement, out var buildDiagnostics);
        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        return [.. buildDiagnostics, .. findings.ToImmutableArray()];
    }

    private static ProgramModelClass Build(string statement, out ImmutableArray<Diagnostic> diagnostics)
    {
        return StandardModelProbe.Build(statement, out diagnostics, Types);
    }

    private static LocalDeclarationStatement LastLocal(ProgramModelClass model)
    {
        var run = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]).MethodsList.Single(method => method.Name == "Run");

        return run.Body!.Statements.OfType<LocalDeclarationStatement>().Last();
    }

    private static IEnumerable<Diagnostic> Errors(IEnumerable<Diagnostic> diagnostics)
    {
        return diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}