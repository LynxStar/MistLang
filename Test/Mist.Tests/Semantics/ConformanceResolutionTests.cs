// samples/AbstractTypes is a false green for conformance assignability, measured -- see its
// `untested` entry in backlog/emitter-v2-capability-progression.yaml.

using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class ConformanceResolutionTests : ProgramModelProbe
{
    private const string FixtureTypes = """
        public abstract INamed
        {
            string Name();
        }

        public class Logger
        {
            public string GetLabel() { return "label"; }
        }

        with Logger : INamed
        {
            GetLabel as Name;
        }

        public class Sink
        {
            public static int Take(INamed named) { return 1; }
            public static int Take(bool flag) { return 2; }
        }
        """;

    public ConformanceResolutionTests()
        : base("MistConformance_")
    {
    }

    protected override string Fixtures { get; } = FixtureTypes;

    protected override string Preamble { get; } = """
        var logger = new Logger();
        """;

    /// <summary>
    /// The bool overload is here to make TWO candidates. With one, OverloadResolution returns it
    /// without running applicability, so the test would pass with the conformance invisible.
    /// </summary>
    [Fact]
    public void ARetroactiveConformance_MakesAnAbstractTypedOverloadApplicable()
    {
        var invocation = SingleInvocation("var chosen = Sink.Take(logger);");

        Assert.NotNull(invocation.Method);
        var parameter = Assert.Single(invocation.Method.ParametersList);
        Assert.Equal("Probe.INamed", parameter.Type.FullyQualifiedName);
    }

    [Fact]
    public void AMappingResolvesBothEnds_ToTheDeclaredMembers()
    {
        var mapping = Assert.IsType<MethodMapping>(Assert.Single(Conformance("Probe.Logger").Mappings));

        Assert.Equal("GetLabel", mapping.Source.Name);
        Assert.Equal("Name", mapping.Target.Name);
    }

    [Fact]
    public void ARetroactiveConformance_StaysOutOfTheBaseAbstractTypes()
    {
        Assert.Empty(Conformer("Probe.Logger").BaseAbstractTypes);
    }

    [Fact]
    public void ASlotReachedOnlyByConformance_DoesNotResolveOnTheConcreteType()
    {
        var found = TypeMemberLookup.Find(
            Conformer("Probe.Logger"),
            LookupRequest.Member("Name", MemberVisibility.InstanceAndStatic, null));

        _ = Assert.IsType<NotFoundResult>(found);
    }

    [Fact]
    public void AnInheritedSlot_RecordsTheDeclaringAbstractRatherThanTheNamedOne()
    {
        var mapping = Assert.Single(
            Conformance("Probe.Joiner", InheritedSlotTypes).Mappings,
            candidate => candidate.TargetName == "Inherited");

        Assert.Equal("Probe.IBase", mapping.Declarer.FullyQualifiedName);
    }

    [Fact]
    public void ASlotDeclaredOnTheNamedAbstract_RecordsThatAbstract()
    {
        var mapping = Assert.Single(
            Conformance("Probe.Joiner", InheritedSlotTypes).Mappings,
            candidate => candidate.TargetName == "Join");

        Assert.Equal("Probe.IWithArgs", mapping.Declarer.FullyQualifiedName);
    }

    [Fact]
    public void AMemberInheritedFromABaseClass_CanAnswerASlot()
    {
        var mapping = Assert.IsType<MethodMapping>(
            Assert.Single(Conformance("Probe.Derived", InheritedSourceTypes).Mappings));

        Assert.Equal("Label", mapping.Source.Name);
    }

    // ── What construction refuses, and what it records anyway ────────────

    [Fact]
    public void AMappingNamingNoSlot_IsRefused()
    {
        _ = Assert.Single(Errors(
            BuildFixture("with Logger : INamed { GetLabel as Absent; }"),
            "MIST_PM_CONFORMANCE_UNRESOLVED_SLOT"));
    }

    [Fact]
    public void AMappingNamingNoSourceMember_IsRefused()
    {
        _ = Assert.Single(Errors(
            BuildFixture("with Logger : INamed { Absent as Name; }"),
            "MIST_PM_CONFORMANCE_MAPPING_UNSATISFIED"));
    }

    [Fact]
    public void AMappingAcrossMemberKinds_IsRefused()
    {
        _ = Assert.Single(Errors(
            BuildFixture(
                "with Holder : INamed { Caption as Name; }",
                "public class Holder { public string Caption { get; set; } }"),
            "MIST_PM_CONFORMANCE_MAPPING_UNSATISFIED"));
    }

    [Fact]
    public void AMappingToAMethodOfTheWrongArity_IsRefused()
    {
        _ = Assert.Single(Errors(
            BuildFixture(
                "with Wide : INamed { Wrong as Name; }",
                "public class Wide { public string Wrong(int extra) { return \"x\"; } }"),
            "MIST_PM_CONFORMANCE_MAPPING_UNSATISFIED"));
    }

    [Fact]
    public void AnAbstractDeclaredToConform_IsRefused()
    {
        _ = Assert.Single(Errors(
            BuildFixture("with INamed : IOther { Name as Other; }", "public abstract IOther { string Other(); }"),
            "MIST_PM_CONFORMANCE_NOT_CONCRETE"));
    }

    [Fact]
    public void AConformanceToAConcreteType_IsRefused()
    {
        _ = Assert.Single(Errors(
            BuildFixture("with Logger : Logger { GetLabel as GetLabel; }"),
            "MIST_PM_CONFORMANCE_NOT_ABSTRACT"));
    }

    /// <summary>
    /// Refused by the conformance rule of Stage 5, not here.
    /// </summary>
    [Fact]
    public void AConformanceRestatingTheBaseList_IsRecordedAndLeavesTheAbstractReachableTwice()
    {
        var conformer = Conformer("Probe.Nominal", RedundantTypes);

        _ = Assert.Single(conformer.ConformancesList);
        Assert.Equal(
            ["Probe.INamed"],
            conformer.BaseAbstractTypes.Select(abstractBase => abstractBase.FullyQualifiedName));
    }

    [Fact]
    public void TwoBlocksNamingOneAbstract_AreBothRecorded()
    {
        Assert.Equal(2, Conformer("Probe.Twice", DuplicateBlockTypes).ConformancesList.Count);
    }

    [Fact]
    public void AnUnresolvableTarget_IsReportedOnlyAsUnresolved()
    {
        var diagnostics = BuildFixture("with Absent : INamed { Whatever as Name; }");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_UNRESOLVED_TYPE"));
        Assert.Empty(Errors(diagnostics, "MIST_PM_CONFORMANCE_NOT_CONCRETE"));
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private const string InheritedSlotTypes = """
        public abstract IBase
        {
            string Inherited();
        }

        public abstract IWithArgs : IBase
        {
            string Join(string left, int count);
        }

        public class Joiner
        {
            public string DoJoin(string left, int count) { return "j"; }
            public string FromBase() { return "b"; }
        }

        with Joiner : IWithArgs
        {
            DoJoin as Join;
            FromBase as Inherited;
        }
        """;

    private const string RedundantTypes = """
        public class Nominal : INamed
        {
            public string Name() { return "n"; }
        }

        with Nominal : INamed
        {
            Name as Name;
        }
        """;

    private const string DuplicateBlockTypes = """
        public class Twice
        {
            public string Give() { return "t"; }
        }

        with Twice : INamed
        {
            Give as Name;
        }

        with Twice : INamed
        {
            Give as Name;
        }
        """;

    private const string InheritedSourceTypes = """
        public class Base
        {
            public string Label() { return "base"; }
        }

        public class Derived : Base
        {
        }

        with Derived : INamed
        {
            Label as Name;
        }
        """;

    private InvocationExpression SingleInvocation(string statement, string extraTypes = "")
    {
        return InitializerOf<InvocationExpression>(statement, extraTypes);
    }

    /// <summary>
    /// Asserts on the TYPE's list, not the program model's flat one: both hold the same instance,
    /// so the flat one would pass without proving what emission consults.
    /// </summary>
    private ConformanceDeclaration Conformance(string fullyQualifiedName, string extraTypes = "")
    {
        return Assert.Single(Conformer(fullyQualifiedName, extraTypes).ConformancesList);
    }

    private ConcreteType Conformer(string fullyQualifiedName, string extraTypes = "")
    {
        var model = BuildModel("var unused = 1;", extraTypes, extraPreamble: "", out _);
        return Assert.IsAssignableFrom<ConcreteType>(model.TypesByFQN[fullyQualifiedName]);
    }

    private System.Collections.Immutable.ImmutableArray<Diagnostic> BuildFixture(
        string withBlock, string extraTypes = "")
    {
        var source = $$"""
            namespace Probe;

            public abstract INamed
            {
                string Name();
            }

            public class Logger
            {
                public string GetLabel() { return "label"; }
            }

            {{extraTypes}}

            {{withBlock}}
            """;

        _ = Build(source, out var diagnostics);
        return diagnostics;
    }
}