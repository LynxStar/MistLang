using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class StampableConstraintTests : ProgramModelProbe
{
    public StampableConstraintTests()
        : base("MistStampableConstraints_")
    {
    }

    protected override string Fixtures { get; } = "";

    protected override string Preamble { get; } = "";

    [Fact]
    public void ProgramModel_RecordsTheStampableKeywordConstraint()
    {
        var model = Build("namespace Probe; public class Gate<T> where T : stampable { }", out var diagnostics);

        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var gate = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Gate<>"]);
        Assert.Equal("Stampable", Assert.Single(Assert.Single(gate.TypeParametersList).ConstraintsList).Kind.ToString());
    }

    [Theory]
    [InlineData("int", true)]
    [InlineData("Good", true)]
    [InlineData("Box<int>", true)]
    [InlineData("string", false)]
    [InlineData("Bad", false)]
    [InlineData("Box<string>", false)]
    public void ProgramModelToAnalysis_ConcreteClosingsUseStampabilityRatherThanValueKind(string argument, bool accepted)
    {
        var findings = Findings($$"""
            public class Gate<T> where T : stampable { }
            public struct Good { public int Item; }
            public struct Bad { public string Item; }
            public struct Box<T> { public T Item; }
            public class Use { public static void Accept(Gate<{{argument}}> value) { } }
            """);

        Assert.Equal(accepted ? 0 : 1, findings.Count);

        if(!accepted)
        {
            Assert.Contains("stampable", findings[0].Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("where T : struct", false)]
    [InlineData("where T : default", false)]
    [InlineData("where T : stampable", true)]
    public void ProgramModelToAnalysis_OpenTypeForwardingRequiresAStampableGuarantee(string constraints, bool accepted)
    {
        var findings = Findings($$"""
            public class Gate<T> where T : stampable { }
            public class Forward<T> {{constraints}}
            {
                public static void Accept(Gate<T> value) { }
            }
            """);

        Assert.Equal(accepted ? 0 : 1, findings.Count);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("where T : struct", false)]
    [InlineData("where T : stampable", true)]
    public void ProgramModelToAnalysis_OpenMethodForwardingRecordsItsConstraintClosing(string constraints, bool accepted)
    {
        var findings = Findings($$"""
            public class Use
            {
                public static void Need<T>() where T : stampable { }
                public static void Forward<T>() {{constraints}} { Need<T>(); }
            }
            """);

        Assert.Equal(accepted ? 0 : 1, findings.Count);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("\"x\"", false)]
    public void ProgramModelToAnalysis_InferredMethodArgumentsAreChecked(string argument, bool accepted)
    {
        var findings = Findings($$"""
            public class Use
            {
                public static void Need<T>(T value) where T : stampable { }
                public static void Call() { Need({{argument}}); }
            }
            """);

        Assert.Equal(accepted ? 0 : 1, findings.Count);
    }

    [Fact]
    public void ProgramModelToAnalysis_OpenMethodForwardingIsCheckedWhenTheCalleeIsDeclaredLater()
    {
        var findings = Findings("""
            public class Use
            {
                public static void Forward<T>() { Need<T>(); }
                public static void Need<T>() where T : stampable { }
            }
            """);

        _ = Assert.Single(findings);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("where T : stampable", true)]
    public void ProgramModelToAnalysis_OpenAliasesMustProveTheTargetsStampableConstraint(string constraints, bool accepted)
    {
        var findings = Findings($$"""
            type Alias<T> = Gate<T> {{constraints}};
            public class Gate<T> where T : stampable { }
            """);

        Assert.Equal(accepted ? 0 : 1, findings.Count);
    }

    [Theory]
    [InlineData("int", true)]
    [InlineData("string", false)]
    public void ProgramModelToAnalysis_ConstrainedAliasArgumentsAreChecked(string argument, bool accepted)
    {
        var findings = Findings($$"""
            type Alias<T> = Box<T> where T : stampable;
            public struct Box<T> { public T Item; }
            public class Use { public static void Accept(Alias<{{argument}}> value) { } }
            """);

        Assert.Equal(accepted ? 0 : 1, findings.Count);
    }

    [Theory]
    [InlineData("new Alias<int>(1)", true)]
    [InlineData("new Alias<string>(\"x\")", false)]
    [InlineData("Alias<int>.Count()", true)]
    [InlineData("Alias<string>.Count()", false)]
    [InlineData("Alias<int>", true)]
    [InlineData("Alias<string>", false)]
    public void ProgramModelToAnalysis_AliasConstructionStaticAccessAndTypeLiteralsRecordConstraints(string expression, bool accepted)
    {
        var findings = Findings($$"""
            type Alias<T> = Box<T> where T : stampable;
            public struct Box<T>
            {
                public T Item;
                public Box(T item) { Item = item; }
                public static int Count() { return 1; }
            }
            public class Use { public static void Call() { var actual = {{expression}}; } }
            """);

        Assert.Equal(accepted ? 0 : 1, findings.Count);
    }

    [Theory]
    [InlineData("Gate<int>", true)]
    [InlineData("Gate<string>", false)]
    [InlineData("Gate<int>.Count()", true)]
    [InlineData("Gate<string>.Count()", false)]
    public void ProgramModelToAnalysis_NominalTypeLiteralsAndStaticAccessRecordConstraints(string expression, bool accepted)
    {
        var findings = Findings($$"""
            public class Gate<T> where T : stampable { public static int Count() { return 1; } }
            public class Use { public static void Call() { var actual = {{expression}}; } }
            """);

        Assert.Equal(accepted ? 0 : 1, findings.Count);
    }

    [Theory]
    [InlineData("public struct Item { public int Value; }", true)]
    [InlineData("public class Item { }", false)]
    public void ProgramModelToAnalysis_AssociatedTypeSlotsCheckStampability(string declaration, bool accepted)
    {
        var model = Build($$"""
            namespace Probe;
            public abstract Storage { type Item : stampable; }
            public struct Concrete : Storage { {{declaration}} }
            """, out var diagnostics);
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        var errors = findings.ToImmutableArray().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Equal(accepted ? 0 : 1, errors.Length);

        if(!accepted)
        {
            Assert.Equal("MIST_PA_TYPE_SLOT_CONSTRAINT_NOT_SATISFIED", errors[0].Id.Value);
        }
    }

    [Fact]
    public void ProgramModelToAnalysis_OpenForwardingIsCheckedWhenTheConstrainedTypeIsDeclaredLater()
    {
        var findings = Findings("""
            public class Forward<T> { public static void Accept(Gate<T> value) { } }
            public class Gate<T> where T : stampable { }
            """);

        _ = Assert.Single(findings);
    }

    [Theory]
    [InlineData("int, int", true)]
    [InlineData("int, string", false)]
    [InlineData("[]", true)]
    public void ProgramModelToAnalysis_PackConstraintsCheckEachClosedElement(string arguments, bool accepted)
    {
        var findings = Findings($$"""
            public class Gate<params TArgs> where TArgs : stampable { }
            public class Use { public static void Accept(Gate<{{arguments}}> value) { } }
            """);

        Assert.Equal(accepted ? 0 : 1, findings.Count);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("where TArgs : struct", false)]
    [InlineData("where TArgs : stampable", true)]
    public void ProgramModelToAnalysis_PackForwardingRequiresAStampableGuarantee(string constraints, bool accepted)
    {
        var findings = Findings($$"""
            public class Gate<params TArgs> where TArgs : stampable { }
            public class Forward<params TArgs> {{constraints}}
            {
                public static void Accept(Gate<TArgs...> value) { }
            }
            """);

        Assert.Equal(accepted ? 0 : 1, findings.Count);
    }

    [Fact]
    public void ProgramModel_RejectsClassAndStampableAsConflictingConstraints()
    {
        _ = Build("namespace Probe; public class Gate<T> where T : class, stampable { }", out var diagnostics);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("class", StringComparison.Ordinal)
            && diagnostic.Message.Contains("stampable", StringComparison.Ordinal));
    }

    private List<Diagnostic> Findings(string declarations)
    {
        var model = Build($"namespace Probe; {declarations}", out var diagnostics);
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        return findings.ToImmutableArray().Where(diagnostic => diagnostic.Id.Value == "MIST_PA_CONSTRAINT_NOT_SATISFIED").ToList();
    }
}