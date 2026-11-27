using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.ProgramAnalysis;

public sealed class ClonableConversionPrograms
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
            public Ocelot? Pet;

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

        public class Link : IClonable<Link>
        {
            public int Item;
            public Link? Next;

            public Link(int item, Link? next)
            {
                this.Item = item;
                this.Next = next;
            }

            public owned Link Clone()
            {
                return new Link(this.Item, this.Next?.Clone());
            }
        }

        public class Branch<T> : IClonable<Branch<T>>
        {
            public T Item;
            public Branch<T>? Next;

            public Branch(T item, Branch<T>? next)
            {
                this.Item = item;
                this.Next = next;
            }

            public owned Branch<T> Clone()
            {
                if(this.Next is Branch<T> next)
                {
                    return new Branch<T>(this.Item, next.Clone());
                }

                Branch<T>? end = void;
                return new Branch<T>(this.Item, end);
            }
        }

        public readonly class Rung : IClonable<Rung>
        {
            public int Item { get; }
            public Rung? Next { get; }

            public Rung(int item, Rung? next)
            {
                this.Item = item;
                this.Next = next;
            }

            public owned Rung Clone()
            {
                return new Rung(this.Item, this.Next?.Clone());
            }
        }

        public type IntCell = new Cell<int>;
        public type Celled<C> = new Cell<C>;
        public type Jaguar = new Tiger;
        public type Lynx = new Jaguar;
        public type Puma = new Lion;
        public type Margay = new Ocelot;
        public type Actor = new Entity;
        public type Chain = new Link;
        public type Twig<T> = new Branch<T>;
        public type Step = new Rung;
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
        Link? noLink = void;
        var link = new Link(1, noLink);
        Branch<int>? noBranch = void;
        var branch = new Branch<int>(1, noBranch);
        Rung? noRung = void;
        var rung = new Rung(1, noRung);
        var step = new Step(1, void);
        """;

    internal const string LeaveIntCell = "var leftForAConstruction = (Cell<int>)intCell;";
    internal const string EnterChain = "var aSelfTypedSlotClonedConditionally = (Chain)link;";
    internal const string RefusedPuma = "var renamedField = (Puma)lion;";

    private static readonly string[] Statements =
    [
        "var entered = (Jaguar)tiger;",
        "var left = (Tiger)jaguar;",
        "var enteredFromAConstruction = (IntCell)cell;",
        LeaveIntCell,
        "var enteredAGenericNewtype = (Celled<int>)cell;",
        "var enteredAtAnotherInstance = (Celled<string>)new Cell<string>(\"s\");",
        "var leftAGenericNewtype = (Cell<string>)celled;",
        EnterChain,
        "var aSelfTypedSlotClonedConditionallyBack = (Link)new Chain(1, void);",
        "var aSelfTypedSlotClonedUnderAPattern = (Twig<int>)branch;",
        "var aSelfTypedSlotClonedUnderAPatternBack = (Branch<int>)new Twig<int>(1, void);",
        "var aReadonlySourceWhoseCopyDoesNotHold = (Step)rung;",
        "var aReadonlySourceWhoseCopyDoesNotHoldBack = (Rung)step;",
    ];

    public static TheoryData<string> AdmittedCasts => new(Statements);

    private readonly Lazy<(Dictionary<string, CastExpression> Casts, ClassType Chain)> accepted = new(BindAccepted);
    private readonly Lazy<ProgramModelClass> declarations = new(() =>
    {
        var model = Build("", out var diagnostics);
        Assert.Empty(Errors(diagnostics));
        return model;
    });
    private readonly Lazy<Diagnostic[]> refusedPuma = new(() => AnalyzeErrors(RefusedPuma));

    internal ProgramModelClass Declarations => declarations.Value;
    internal ClassType Chain => accepted.Value.Chain;

    internal CastExpression Accepted(string statement)
    {
        return accepted.Value.Casts[statement];
    }

    internal Diagnostic[] Refusals(string statement)
    {
        return statement == RefusedPuma ? refusedPuma.Value : AnalyzeErrors(statement);
    }

    internal static ProgramModelClass Build(string statement, out ImmutableArray<Diagnostic> diagnostics)
    {
        return StandardModelProbe.Build(Locals + statement, out diagnostics, Types);
    }

    private static (Dictionary<string, CastExpression> Casts, ClassType Chain) BindAccepted()
    {
        var cases = Statements.Select((statement, i) => (Statement: statement, Method: $"Cast{i}")).ToArray();
        var methods = string.Join("\n", cases.Select(c => $"public static void {c.Method}() {{\n{Locals}\n{c.Statement}\n}}"));
        var model = StandardModelProbe.Build("", out var built, $$"""
            {{Types}}
            public class AcceptedCasts
            {
                {{methods}}
            }
            """);
        Assert.Empty(Errors(built));
        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);
        Assert.Empty(Errors(findings.ToImmutableArray()));

        var owner = Assert.IsType<ClassType>(model.TypesByFQN["Probe.AcceptedCasts"]);
        var bound = owner.MethodsList.ToDictionary(method => method.Name, StringComparer.Ordinal);
        var casts = cases.ToDictionary(c => c.Statement, c =>
            Assert.IsType<CastExpression>(bound[c.Method].Body!.Statements.OfType<LocalDeclarationStatement>().Last().Initializer),
            StringComparer.Ordinal);
        return (casts, Assert.IsType<ClassType>(model.TypesByFQN["Probe.Chain"]));
    }

    private static Diagnostic[] AnalyzeErrors(string statement)
    {
        var model = Build(statement, out var built);
        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);
        return [.. Errors(built), .. Errors(findings.ToImmutableArray())];
    }

    private static IEnumerable<Diagnostic> Errors(IEnumerable<Diagnostic> diagnostics)
    {
        return diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}

public sealed class ClonableNewtypeConversionDecisionTests(ClonableConversionPrograms programs) : IClassFixture<ClonableConversionPrograms>
{
    [Theory]
    [Trait("Tier", "4")]
    [MemberData(nameof(ClonableConversionPrograms.AdmittedCasts), MemberType = typeof(ClonableConversionPrograms))]
    public void ACastRunningAClonableSourcesClone_IsAdmittedEitherWay(string statement)
    {
        Assert.NotNull(programs.Accepted(statement).Operator);
    }

    [Theory]
    [Trait("Tier", "4")]
    [InlineData("Jaguar withoutACast = tiger;", "MIST0158")]
    [InlineData("var noCopy = (Actor)entity;", "MIST0152")]
    [InlineData("var noCopyBack = (Entity)new Actor();", "MIST0152")]
    [InlineData("var inheritedCopyOnly = (Margay)ocelot;", "MIST0152")]
    [InlineData("Celled<int> genericWithoutACast = cell;", "MIST0158")]
    [InlineData("var fromAnotherInstance = (Celled<string>)cell;", "MIST0151")]
    [InlineData("var toAnotherInstance = (Cell<int>)celled;", "MIST0149")]
    [InlineData(ClonableConversionPrograms.RefusedPuma, "MIST0154")]
    [InlineData("var renamedFieldBack = (Lion)puma;", "MIST0154")]
    [InlineData("Rung aReadonlySourceNoLongerLeavesImplicitly = step;", "MIST0158")]
    public void ACastWithNoCopyThatHolds_IsRefused(string statement, string expected)
    {
        Assert.Equal(expected, Assert.Single(programs.Refusals(statement)).Number.Value);
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("is", "MIST0193")]
    [InlineData("as", "MIST0403")]
    public void ATypeTestBetweenACloneAndASourceThatDefinesItsOwnCopy_IsRefused(string operation, string expected)
    {
        var (_, tiger, jaguar) = TigerFamily();
        var finding = operation switch
        {
            "is" => PatternAnalyzer.TypeFinding(new TypeTestPattern(HandBuiltModel.Node(), tiger, null), jaguar, new HandBuiltModel().Interner),
            "as" => OperatorBinder.AsFinding(jaguar, tiger),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        Assert.Equal(expected, finding?.Number.Value);
    }

    [Fact]
    [Trait("Tier", "1")]
    public void ACloneTestedAgainstItsRenamedBase_IsAdmitted()
    {
        var (cat, _, jaguar) = TigerFamily();
        var pattern = new TypeTestPattern(HandBuiltModel.Node(), cat, null);
        Assert.Null(PatternAnalyzer.TypeFinding(pattern, jaguar, new HandBuiltModel().Interner));
    }

    [Fact]
    [Trait("Tier", "4")]
    public void ARefusedConversion_NamesTheMemberThatBlocksIt()
    {
        var refusal = Assert.Single(programs.Refusals(ClonableConversionPrograms.RefusedPuma));

        Assert.Contains("'Puma.Clone' with a 'Lion' as this", refusal.Message);
        Assert.Contains("at 'Mate'", refusal.Message);
    }

    // IsCalled belongs to the operator definition, so each marking assertion binds its cast in isolation.
    [Fact]
    [Trait("Tier", "3")]
    public void ACastToTheNewtype_RunsTheNewtypesCloneWithTheSourceAsThis()
    {
        var cast = Assert.IsType<CastExpression>(LastLocal(ClonableConversionPrograms.Build("var entered = (Jaguar)tiger;", out _)).Initializer);
        var conversion = Assert.IsType<CastOperator>(cast.Operator);

        Assert.Equal("Probe.Jaguar", conversion.ClonesThrough?.DeclaringType?.FullyQualifiedName);
        Assert.Equal("Probe.Tiger", conversion.SourceParameter.Type.FullyQualifiedName);
        Assert.True(conversion.IsCalled);
    }

    [Fact]
    [Trait("Tier", "4")]
    public void ACastToAConstructedSource_RunsItsCloneWithTheArgumentsInPlace()
    {
        var cast = programs.Accepted(ClonableConversionPrograms.LeaveIntCell);
        var conversion = Assert.IsType<CastOperator>(cast.Operator);
        var copy = Assert.IsType<ReturnStatement>(conversion.Body!.Statements[^1]).Value!;

        Assert.Equal("Probe.Cell<Int32>", conversion.TargetType.FullyQualifiedName);
        Assert.Equal("Probe.Cell<Int32>", copy.Type.FullyQualifiedName);
    }

    [Fact]
    [Trait("Tier", "3")]
    public void ACastToAnInstanceOfAGenericNewtype_RunsTheNewtypesCloneTypedAtThatInstance()
    {
        var cast = Assert.IsType<CastExpression>(LastLocal(ClonableConversionPrograms.Build("var entered = (Celled<int>)cell;", out _)).Initializer);
        var conversion = Assert.IsType<CastOperator>(cast.Operator);

        Assert.Equal("Probe.Celled<>", conversion.ClonesThrough?.DeclaringType?.FullyQualifiedName);
        Assert.Equal("Probe.Celled<C>", conversion.TargetType.FullyQualifiedName);
        Assert.Equal("Probe.Cell<C>", conversion.SourceParameter.Type.FullyQualifiedName);
        Assert.Equal("Probe.Celled<Int32>", cast.Type.FullyQualifiedName);
        Assert.True(conversion.IsCalled);
    }

    [Fact]
    [Trait("Tier", "3")]
    public void ACloneOfAClone_ConvertsWithItsImmediateSourceByEachSidesOwnClone()
    {
        var model = programs.Declarations;

        var lynx = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Lynx"]);
        var into = lynx.CastOperatorsList.Single(conversion => conversion.TargetType.FullyQualifiedName == "Probe.Lynx");
        var outOf = lynx.CastOperatorsList.Single(conversion => conversion.TargetType.FullyQualifiedName == "Probe.Jaguar");

        Assert.Equal("Probe.Jaguar", lynx.CloneSourceType?.FullyQualifiedName);
        Assert.Equal("Probe.Tiger", lynx.CloneSource?.FullyQualifiedName);
        Assert.Equal(2, lynx.CastOperatorsList.Count);
        Assert.Equal("Probe.Jaguar", into.SourceParameter.Type.FullyQualifiedName);
        Assert.Equal("Probe.Lynx", into.ClonesThrough?.DeclaringType?.FullyQualifiedName);
        Assert.Equal("Probe.Jaguar", outOf.ClonesThrough?.DeclaringType?.FullyQualifiedName);
        Assert.Equal("Probe.Jaguar", Assert.IsType<ReturnStatement>(outOf.Body!.Statements[^1]).Value!.Type.FullyQualifiedName);
    }

    [Fact]
    [Trait("Tier", "4")]
    public void ACloneOfTheOperandWithinAConversion_IsThatConversionAndIsNotJudgedAgainstItself()
    {
        var into = programs.Chain.CastOperatorsList.Single(conversion => conversion.TargetType.FullyQualifiedName == "Probe.Chain");
        Assert.Same(into, programs.Accepted(ClonableConversionPrograms.EnterChain).Operator);
        var creation = Assert.IsType<NewExpression>(Assert.IsType<ReturnStatement>(into.Body!.Statements[^1]).Value);
        var converted = Assert.IsType<CastExpression>(creation.Arguments[1]);

        Assert.Same(into, converted.Operator);
        Assert.True(converted.IsConditional);
        Assert.Equal("Probe.Chain", converted.TargetType.FullyQualifiedName);
    }

    [Fact]
    [Trait("Tier", "3")]
    public void AStaticTheCloneReads_IsTheBodysOwnTypesNotTheOperands()
    {
        var model = programs.Declarations;
        var jaguar = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Jaguar"]);
        var toTiger = jaguar.CastOperatorsList.Single(conversion => conversion.TargetType.FullyQualifiedName == "Probe.Tiger");
        var counter = Assert.IsType<ExpressionStatement>(toTiger.Body!.Statements[0]).Expression;
        var assignment = Assert.IsType<AssignmentExpression>(counter);

        Assert.Equal("Probe.Tiger", Assert.IsType<FieldReferenceExpression>(assignment.Target).Field.DeclaringType?.FullyQualifiedName);
    }

    private static (ClassType Cat, ClassType Tiger, ClassType Jaguar) TigerFamily()
    {
        var cat = HandBuiltModel.Class("Cat");
        var tiger = HandBuiltModel.Class("Tiger", cat);
        var jaguar = HandBuiltModel.Class("Jaguar", cat);
        jaguar.CloneSource = tiger;
        jaguar.CloneSourceType = tiger;
        foreach(var (owner, source, target) in new[] { (jaguar, tiger, jaguar), (tiger, jaguar, tiger) })
        {
            var clone = new Method("Clone", owner, [], DeclarationFragments.Public with { IsOwned = true }) { DeclaringType = owner };
            owner.MethodsList.Add(clone);
            jaguar.CastOperatorsList.Add(new CastOperator(target, new Parameter("value", source), false, DeclarationFragments.Public)
            { DeclaringType = jaguar, ClonesThrough = clone });
        }

        return (cat, tiger, jaguar);
    }

    private static LocalDeclarationStatement LastLocal(ProgramModelClass model)
    {
        var run = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]).MethodsList.Single(method => method.Name == "Run");

        return run.Body!.Statements.OfType<LocalDeclarationStatement>().Last();
    }
}