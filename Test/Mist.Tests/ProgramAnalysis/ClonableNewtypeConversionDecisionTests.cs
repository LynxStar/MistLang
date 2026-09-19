// Tier 3 (Component): the conversions between a newtype and a mutable class source that defines its own
// copy (newtypes spec Requirement 10). Each runs a Clone body with the operand as this, so a cast holds
// exactly when that body types with the operand in place, and a private member of either type never stops
// it. Built over the real Standard sources, the only place IClonable is declared.

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.ProgramAnalysis;

public sealed class ClonableNewtypeConversionDecisionTests
{
    private const string Types = """
        public class Cat
        {
            protected int lives;

            public Cat() { this.lives = 9; }
        }

        public class Tiger : Cat, IClonable<Tiger>
        {
            private static int made = 0;
            private int stripes;

            public string Name { get; set; }

            public Tiger(string name, int stripes) : base()
            {
                this.Name = name;
                this.stripes = stripes;
            }

            public owned Tiger Clone()
            {
                made = made + 1;
                var copy = new Tiger(Name, lives);
                copy.stripes = this.stripes;
                return copy;
            }
        }

        public class Lion : IClonable<Lion>
        {
            public Lion? Mate;

            public Lion() { }

            public owned Lion Clone()
            {
                return new Lion { Mate = Mate };
            }
        }

        public class Feline : IClonable<Feline>
        {
            public Feline() { }

            public owned Feline Clone() { return new Feline(); }
        }

        public class Ocelot : Feline
        {
            public Ocelot() : base() { }
        }

        public class Entity
        {
            public Entity() { }
        }

        public class Cell<T> : IClonable<Cell<T>>
        {
            private static int made = 0;
            private T content;

            public Cell(T content) { this.content = content; }

            public owned Cell<T> Clone()
            {
                made = made + 1;
                return new Cell<T>(content);
            }
        }

        public type IntCell = new Cell<int>;
        public type Celled<C> = new Cell<C>;
        public type Jaguar = new Tiger;
        public type Puma = new Lion;
        public type Margay = new Ocelot;
        public type Actor = new Entity;
        """;

    private const string Locals = """
        var tiger = new Tiger("Shere", 3);
        var jaguar = new Jaguar("Bagheera", 4);
        var lion = new Lion();
        var puma = new Puma();
        var ocelot = new Ocelot();
        var entity = new Entity();
        var cell = new Cell<int>(1);
        var intCell = new IntCell(2);
        var celled = new Celled<string>("c");
        """;

    [Theory]
    [InlineData("var entered = (Jaguar)tiger;")]
    [InlineData("var left = (Tiger)jaguar;")]
    [InlineData("var enteredFromAConstruction = (IntCell)cell;")]
    [InlineData("var leftForAConstruction = (Cell<int>)intCell;")]
    [InlineData("var enteredAGenericNewtype = (Celled<int>)cell;")]
    [InlineData("var enteredAtAnotherInstance = (Celled<string>)new Cell<string>(\"s\");")]
    [InlineData("var leftAGenericNewtype = (Cell<string>)celled;")]
    public void ACastRunningAClonableSourcesClone_IsAdmittedEitherWay(string statement)
    {
        Assert.Empty(Errors(Analyse(statement)));
    }

    [Theory]
    [InlineData("Jaguar withoutACast = tiger;", "MIST_PA_CAST_REQUIRED")]
    [InlineData("var noCopy = (Actor)entity;", "MIST_PA_NEWTYPE_SOURCE_DEFINES_NO_COPY")]
    [InlineData("var noCopyBack = (Entity)new Actor();", "MIST_PA_NEWTYPE_SOURCE_DEFINES_NO_COPY")]
    [InlineData("var inheritedCopyOnly = (Margay)ocelot;", "MIST_PA_NEWTYPE_SOURCE_DEFINES_NO_COPY")]
    [InlineData("Celled<int> genericWithoutACast = cell;", "MIST_PA_CAST_REQUIRED")]
    [InlineData("var fromAnotherInstance = (Celled<string>)cell;", "MIST_PA_CAST_ARGUMENT_MISMATCH")]
    [InlineData("var toAnotherInstance = (Cell<int>)celled;", "MIST_PA_CAST_HAS_NO_CONVERSION")]
    [InlineData("var renamedField = (Puma)lion;", "MIST_PA_CLONE_CONVERSION_DOES_NOT_HOLD")]
    [InlineData("var renamedFieldBack = (Lion)puma;", "MIST_PA_CLONE_CONVERSION_DOES_NOT_HOLD")]
    public void ACastWithNoCopyThatHolds_IsRefused(string statement, string expected)
    {
        Assert.Equal(expected, Assert.Single(Errors(Analyse(statement))).Id.Value);
    }

    [Fact]
    public void ARefusedConversion_NamesTheMemberThatBlocksIt()
    {
        var refusal = Assert.Single(Errors(Analyse("var renamedField = (Puma)lion;")));

        Assert.Contains("'Puma.Clone' with a 'Lion' as this", refusal.Message);
        Assert.Contains("at 'Mate'", refusal.Message);
    }

    [Fact]
    public void ACastToTheNewtype_RunsTheNewtypesCloneWithTheSourceAsThis()
    {
        var cast = Assert.IsType<CastExpression>(LastLocal(Build("var entered = (Jaguar)tiger;", out _)).Initializer);
        var conversion = Assert.IsType<CastOperator>(cast.Operator);

        Assert.Equal("Probe.Jaguar", conversion.ClonesThrough?.DeclaringType?.FullyQualifiedName);
        Assert.Equal("Probe.Tiger", conversion.SourceParameter.Type.FullyQualifiedName);
        Assert.True(conversion.IsCalled);
    }

    [Fact]
    public void ACastToAConstructedSource_RunsItsCloneWithTheArgumentsInPlace()
    {
        var cast = Assert.IsType<CastExpression>(LastLocal(Build("var left = (Cell<int>)intCell;", out _)).Initializer);
        var conversion = Assert.IsType<CastOperator>(cast.Operator);
        var copy = Assert.IsType<ReturnStatement>(conversion.Body!.Statements[^1]).Value!;

        Assert.Equal("Probe.Cell<Int32>", conversion.TargetType.FullyQualifiedName);
        Assert.Equal("Probe.Cell<Int32>", copy.Type.FullyQualifiedName);
    }

    [Fact]
    public void ACastToAnInstanceOfAGenericNewtype_RunsTheNewtypesCloneTypedAtThatInstance()
    {
        var cast = Assert.IsType<CastExpression>(LastLocal(Build("var entered = (Celled<int>)cell;", out _)).Initializer);
        var conversion = Assert.IsType<CastOperator>(cast.Operator);

        Assert.Equal("Probe.Celled<>", conversion.ClonesThrough?.DeclaringType?.FullyQualifiedName);
        Assert.Equal("Probe.Celled<C>", conversion.TargetType.FullyQualifiedName);
        Assert.Equal("Probe.Cell<C>", conversion.SourceParameter.Type.FullyQualifiedName);
        Assert.Equal("Probe.Celled<Int32>", cast.Type.FullyQualifiedName);
        Assert.True(conversion.IsCalled);
    }

    [Fact]
    public void AStaticTheCloneReads_IsTheBodysOwnTypesNotTheOperands()
    {
        var model = Build("", out _);
        var jaguar = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Jaguar"]);
        var toTiger = jaguar.CastOperatorsList.Single(conversion => conversion.TargetType.FullyQualifiedName == "Probe.Tiger");
        var counter = Assert.IsType<ExpressionStatement>(toTiger.Body!.Statements[0]).Expression;
        var assignment = Assert.IsType<AssignmentExpression>(counter);

        Assert.Equal("Probe.Tiger", Assert.IsType<FieldReferenceExpression>(assignment.Target).Field.DeclaringType?.FullyQualifiedName);
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
        return StandardModelProbe.Build(Locals + statement, out diagnostics, Types);
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