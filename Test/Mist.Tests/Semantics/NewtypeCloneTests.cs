// Tier 3 (Component): a newtype over a Mist class or struct is its source's declaration bound again under
// the newtype's name, with no nominal relationship to the source (newtypes spec Requirements 2.1-2.3 and
// 3.2-3.6), asserted on the model and on the analysis findings a clone adds.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class NewtypeCloneTests : ProgramModelProbe
{
    public NewtypeCloneTests()
        : base("MistNewtypeClone_")
    {
    }

    protected override string Fixtures { get; } = """
        public abstract IStriped
        {
            int Stripes();
        }

        public abstract INamed
        {
            string Label();
        }

        public class Cat
        {
            public string Name;

            public Cat(string name)
            {
                this.Name = name;
            }
        }

        public class Tiger : Cat, IStriped
        {
            public Stripe Pattern;

            public Tiger(string name) : base(name)
            {
                this.Pattern = new Stripe();
            }

            public int Stripes() { return 100; }

            public string Title() { return this.Name; }

            public Tiger Twin() { return new Tiger(this.Name); }

            public class Stripe
            {
                public Stripe() { }
            }
        }

        with Tiger : INamed
        {
            Title as Label;
        }

        public struct Point
        {
            public int X;

            public Point(int x)
            {
                this.X = x;
            }

            public Point Moved() { return new Point(this.X + 1); }
        }

        type Jaguar = new Tiger;
        type Position = new Point;
        """;

    protected override string Preamble { get; } = """
        var jaguar = new Jaguar("jag");
        var position = new Position(1);
        """;

    [Fact]
    public void ANewtypeOverAClass_IsAClassWithItsSourcesBases()
    {
        var jaguar = Assert.IsType<ClassType>(TypeNamed("Probe.Jaguar"));

        Assert.Equal("Probe.Tiger", jaguar.CloneSource?.FullyQualifiedName);
        Assert.Equal("Probe.Cat", jaguar.BaseType?.FullyQualifiedName);
        Assert.Contains(jaguar.BaseAbstractTypes, abstractBase => abstractBase.FullyQualifiedName == "Probe.IStriped");
    }

    [Fact]
    public void ANewtypeOverAStruct_IsAStruct()
    {
        var position = Assert.IsType<StructType>(TypeNamed("Probe.Position"));

        Assert.Equal("Probe.Point", position.CloneSource?.FullyQualifiedName);
    }

    [Fact]
    public void TheClone_IsWithinItsSourcesBasesAndNeverTheSource()
    {
        var model = BuildModel("int ok = 1;", "", "", out _);
        var jaguar = model.TypesByFQN["Probe.Jaguar"];
        var tiger = model.TypesByFQN["Probe.Tiger"];

        Assert.True(ConversionRelation.IsWithin(jaguar, model.TypesByFQN["Probe.Cat"]));
        Assert.True(ConversionRelation.IsWithin(jaguar, model.TypesByFQN["Probe.IStriped"]));
        Assert.False(ConversionRelation.IsWithin(jaguar, tiger));
        Assert.False(ConversionRelation.IsWithin(tiger, jaguar));
    }

    [Theory]
    [InlineData("var twin = jaguar.Twin();", "Probe.Jaguar")]
    [InlineData("var moved = position.Moved();", "Probe.Position")]
    public void AMemberAnsweringTheSource_AnswersTheClone(string statement, string expected)
    {
        Assert.Equal(expected, SingleLocal(statement).Type.FullyQualifiedName);
    }

    [Fact]
    public void TheClonesBody_ConstructsTheClone()
    {
        var jaguar = Assert.IsType<ClassType>(TypeNamed("Probe.Jaguar"));
        var twin = jaguar.MethodsList.Single(method => method.Name == "Twin");

        var returned = Assert.IsType<ReturnStatement>(Assert.Single(twin.Body!.Statements));
        Assert.Equal("Probe.Jaguar", Assert.IsType<NewExpression>(returned.Value).Type.FullyQualifiedName);
    }

    [Fact]
    public void ATypeNestedInTheSource_IsNotCloned()
    {
        var jaguar = Assert.IsType<ClassType>(TypeNamed("Probe.Jaguar"));

        Assert.Empty(jaguar.NestedTypes);
        Assert.Equal("Probe.Tiger.Stripe", jaguar.FieldsList.Single(field => field.Name == "Pattern").Type.FullyQualifiedName);
    }

    [Fact]
    public void AConformanceDeclaredOutsideTheSource_DoesNotReachTheClone()
    {
        var model = BuildModel("int ok = 1;", "", "", out _);
        var jaguar = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Jaguar"]);

        Assert.NotEmpty(Assert.IsType<ClassType>(model.TypesByFQN["Probe.Tiger"]).ConformancesList);
        Assert.Empty(jaguar.ConformancesList);
        Assert.False(ConversionRelation.IsWithin(jaguar, model.TypesByFQN["Probe.INamed"]));
    }

    [Fact]
    public void ASourceMemberThatFailsOnlyForTheClone_IsReportedAtTheNewtypeAndTheSourcesOwnMistakeOnce()
    {
        var findings = Analyse("""
            public class Zoo
            {
                public static void Admit(Lion lion) { }
            }

            public class Lion
            {
                public Lion() { }

                public void Enroll() { Zoo.Admit(this); }

                public void Broken() { int count = "many"; }
            }

            type Puma = new Lion;
            """);

        var cloneFailure = Assert.Single(findings, finding => finding.Id.Value == "MIST_PM_NEWTYPE_CLONE_INVALID");
        Assert.Contains("'Puma' cannot clone 'Lion', whose member 'Enroll'", cloneFailure.Message, StringComparison.Ordinal);
        _ = Assert.Single(findings, finding => finding.Id.Value == "MIST_PA_NO_IMPLICIT_CONVERSION");
    }

    private MistType TypeNamed(string fullyQualifiedName)
    {
        var model = BuildModel("int ok = 1;", "", "", out var diagnostics);
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        return Assert.IsAssignableFrom<MistType>(model.TypesByFQN[fullyQualifiedName]);
    }

    private List<Diagnostic> Analyse(string extraTypes)
    {
        var model = BuildModel("int ok = 1;", extraTypes, "", out var buildDiagnostics);
        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        return [.. buildDiagnostics, .. findings.ToImmutableArray()];
    }
}