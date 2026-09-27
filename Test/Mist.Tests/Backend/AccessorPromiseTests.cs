// Tier 3 (Component): purity Requirement 1.5 in the model and at the judge -- an accessor's own promise
// is recorded on the accessor (PropertyAccessor.OwnModifiers), replaces its declaration's wherever a
// promise is read, and is what its body is held to; so a `local` indexer whose getter says `local
// readonly` is readable on a frozen input from a pure scope, where a `local` indexer whose getter keeps
// only `local` is not. Inference strengthens a declaration whose body proves more (PurityInference.Answer
// joins the two), so the contrast getter must write. Every fixture type is under analysis in every row.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class AccessorPromiseTests : ProgramModelProbe
{
    private const string Writes = "MIST_PA_PURE_SCOPE_WRITES";
    private const string ReadsMutable = "MIST_PA_PURE_SCOPE_READS_MUTABLE";
    private const string CallsImpure = "MIST_PA_PURE_SCOPE_CALLS_IMPURE";
    private const string SetsProperty = "MIST_PA_READONLY_CONTEXT_SETS_PROPERTY";

    private static readonly string[] Ids =
    [
        Writes, ReadsMutable, CallsImpure, "MIST_PA_LOCAL_SCOPE_READS_ARGUMENT", "MIST_PA_PURE_SCOPE_STORES_UNOWNED",
        "MIST_PA_PURE_FUNCTION_REQUIRED", "MIST_PA_DEFAULT_NOT_CONSISTENT", SetsProperty,
        "MIST_PA_READONLY_CONTEXT_CALLS_MUTABLE", "MIST_PA_READONLY_CONTEXT_ALLOCATES_MUTABLE",
        "MIST_PA_READONLY_CONTEXT_WRITES_THROUGH_INDEX", "MIST_PA_FIELD_ASSIGNED_IN_READONLY_MEMBER",
    ];

    public AccessorPromiseTests()
        : base("MistAccessorPromise_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Globals
        {
            public static int Counter;
        }

        public class Bag
        {
            public int cell;

            public pure Bag() { }

            public local int this[int i]
            {
                local readonly get { return cell; }
                set { cell = value; }
            }
        }

        public class Plain
        {
            public int cell;

            public pure Plain() { }

            public local int this[int i]
            {
                get { cell = cell + 1; return cell; }
                set { cell = value; }
            }
        }

        public class Jar
        {
            public int n;

            public pure Jar() { }

            public int Level
            {
                local readonly get { return n; }
                local set { n = value; }
            }
        }
        """;

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("public class Box { public int n; public int P { local readonly get { return n; } local set { n = value; } } }")]
    [InlineData("public class Box { public int n; public int P { local readonly get; local set; } }")]
    public void ProgramModel_AnAccessorsOwnPromiseIsRecordedOnTheAccessor(string declaration)
    {
        var property = TypeIn(declaration).PropertiesList.Single();

        Assert.False(property.Modifiers.IsLocal);
        Assert.False(property.Modifiers.IsReadonly);
        AssertLocalReadonly(property.Getter);
        AssertLocal(property.Setter);
    }

    [Theory]
    [InlineData("public class Box { public int n; public int this[int i] { local readonly get { return n; } local set { n = value; } } }")]
    [InlineData("public class Box { public int n; public int this[int i] { local readonly get => n; local set => n = value; } }")]
    public void ProgramModel_AnIndexerAccessorsOwnPromiseIsRecordedOnTheAccessor(string declaration)
    {
        var indexer = TypeIn(declaration).IndexersList.Single();

        Assert.False(indexer.Modifiers.IsLocal);
        AssertLocalReadonly(indexer.Getter);
        AssertLocal(indexer.Setter);
    }

    [Fact]
    public void ProgramModel_APureAccessorIsPureAndReadonly()
    {
        var getter = TypeIn("public class Box { public int P { pure get { return 1; } } }").PropertiesList.Single().Getter;

        Assert.NotNull(getter);
        var own = Assert.NotNull(getter.OwnModifiers);
        Assert.True(own.IsPure);
        Assert.True(own.IsReadonly);
        Assert.False(own.IsLocal);
    }

    [Fact]
    public void ProgramModel_AnAccessorStatingNothingRecordsNoOwnPromise()
    {
        var property = TypeIn("public class Box { public int n; public int P { get { return n; } set { n = value; } } }").PropertiesList.Single();

        Assert.NotNull(property.Getter);
        Assert.NotNull(property.Setter);
        Assert.Null(property.Getter.OwnModifiers);
        Assert.Null(property.Setter.OwnModifiers);
    }

    [Fact]
    public void ProgramModel_APromiseStandsAheadOfTheAccessorsAccessibility()
    {
        var setter = TypeIn("public class Box { public int n; public int P { local readonly get { return n; } local private set { n = value; } } }").PropertiesList.Single().Setter;

        Assert.NotNull(setter);
        Assert.Equal(Accessibility.Private, setter.Accessibility);
        AssertLocal(setter);
    }

    [Fact]
    public void ProgramModel_ADeclarationsPromiseStaysOnTheDeclarationBesideAnAccessorsOwn()
    {
        var property = TypeIn("public class Box { public int n; public local int P { local readonly get { return n; } set { n = value; } } }").PropertiesList.Single();

        Assert.True(property.Modifiers.IsLocal);
        Assert.NotNull(property.Setter);
        AssertLocalReadonly(property.Getter);
        Assert.Null(property.Setter.OwnModifiers);
    }

    [Theory]
    [InlineData("public static pure int M(frozen Bag b) { return b[0]; }", null)]
    [InlineData("public static pure int M(frozen Bag b) { b[0] = 1; return 0; }", Writes)]
    [InlineData("public static pure int M() { owned var b = new Bag(); b[0] = 1; return b[0]; }", null)]
    [InlineData("public static pure int M(Bag b) { return b[0]; }", CallsImpure)]
    [InlineData("public static pure int M(frozen Plain p) { return p[0]; }", CallsImpure)]
    [InlineData("public static pure int M() { owned var p = new Plain(); return p[0]; }", null)]
    [InlineData("public static pure int M(frozen Jar j) { return j.Level; }", null)]
    [InlineData("public static pure int M(Jar j) { return j.Level; }", CallsImpure)]
    [InlineData("public static pure int M() { var j = new Jar(); j.Level = 2; return j.Level; }", null)]
    [InlineData("public static pure int M(frozen Jar j) { j.Level = 2; return 0; }", Writes)]
    [InlineData("public static readonly int M(Jar j) { return j.Level; }", null)]
    [InlineData("public static readonly int M(Jar j) { j.Level = 2; return 0; }", SetsProperty)]
    [InlineData("public static readonly int M() { var j = new Jar(); j.Level = 2; return j.Level; }", null)]
    public void AnAccessorsOwnPromise_IsWhatACallerReads(string member, string? expected)
    {
        var reported = Analyse("", "public class Host { " + member + " }");

        if(expected is null)
        {
            Assert.Empty(reported);
            return;
        }

        var finding = Assert.Single(reported);
        Assert.Equal(expected, finding.Id.Value);
        Assert.Contains("'M'", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public class Bad { public int n; public int P { pure get { return n; } } }", ReadsMutable, "pure property 'P'")]
    [InlineData("public class Bad { public int n; public int P { pure get { return Globals.Counter; } } }", ReadsMutable, "pure property 'P'")]
    [InlineData("public class Bad { public int n; public int P { local readonly get { n = 1; return n; } } }", Writes, "local readonly property 'P'")]
    [InlineData("public class Bad { public int n; public local int P { local readonly get { n = 1; return n; } set { n = value; } } }", Writes, "local readonly property 'P'")]
    [InlineData("public class Bad { public int n; public int this[int i] { local readonly get { n = 1; return n; } } }", Writes, "local readonly indexer")]
    [InlineData("public class Bad { public int n; public int P { get { return n; } local set { Globals.Counter = value; } } }", Writes, "local property 'P'")]
    public void AnAccessorsBody_IsHeldToItsOwnPromise(string declaration, string expected, string scope)
    {
        var finding = Assert.Single(Analyse("", declaration));

        Assert.Equal(expected, finding.Id.Value);
        Assert.Contains(scope, finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public class Good { public int n; public int P { local readonly get { return n; } local set { n = value; } } }")]
    [InlineData("public class Good { public int n; public local int P { local readonly get { return n; } set { n = value; } } }")]
    [InlineData("public class Good { public readonly int n; public pure Good(int k) { n = k; } public int P { pure get { return n * 2; } } }")]
    [InlineData("public class Good { public int n; public int this[int i] { local readonly get { return n; } local set { n = value; } } }")]
    public void AnAccessorsBody_KeepingItsOwnPromise_IsAdmitted(string declaration)
    {
        Assert.Empty(Analyse("", declaration));
    }

    private static void AssertLocalReadonly(PropertyAccessor? getter)
    {
        Assert.NotNull(getter);
        var own = Assert.NotNull(getter.OwnModifiers);
        Assert.True(own.IsLocal);
        Assert.True(own.IsReadonly);
        Assert.False(own.IsPure);
    }

    private static void AssertLocal(PropertyAccessor? setter)
    {
        Assert.NotNull(setter);
        var own = Assert.NotNull(setter.OwnModifiers);
        Assert.True(own.IsLocal);
        Assert.False(own.IsReadonly);
        Assert.False(own.IsPure);
    }

    private ConcreteType TypeIn(string declaration)
    {
        var model = BuildModel("", declaration, "", out var diagnostics);
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        return Assert.IsAssignableFrom<ConcreteType>(model.TypesByFQN["Probe.Box"]);
    }

    private List<Diagnostic> Analyse(string statement, string extraTypes)
    {
        var model = BuildModel(statement, extraTypes, "", out var diagnostics);
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        return [.. findings.ToImmutableArray().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && Ids.Contains(diagnostic.Id.Value))];
    }
}