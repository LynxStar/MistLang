// Tier 3: what a with block resolves to, asserted on a real ProgramModelBuilder run. See
// ProgramModelProbe for the harness.
//
// WHY THESE CANNOT BE SAMPLES, and this was measured rather than assumed. samples/AbstractTypes goes
// green with the conformance consult in ConversionRelation.Inherits DELETED: its only use of a
// conformance is a plain assignment, and ConversionRelation's contract forbids refusing a program, so
// the assignment binds whether or not the conformance is visible and the emitted C# compiles on the
// strength of the base-list append alone. Applicability only runs when a call has TWO candidates,
// which the sample has none of.
//
// The rest assert placements no output can distinguish: which abstract an explicit interface
// implementation is qualified by, and that a conformance stays OUT of BaseAbstractTypes.

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
    /// The bool overload is declared SECOND deliberately, so the fixture would still pass if nothing
    /// were selected -- what makes it bite is that two candidates force applicability to run.
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

    /// <summary>
    /// The decision the whole design rests on. A conformance is answered by an explicit interface
    /// implementation, which C# does not admit on a concrete-typed receiver, so folding it into the
    /// base list would let a name resolve here that the emitted C# gives no way to call. The next test
    /// is the other half of it.
    /// </summary>
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

    /// <summary>
    /// An inherited slot is qualified by the abstract that DECLARES it. C# rejects an explicit
    /// interface implementation naming the inheriting interface outright rather than resolving through
    /// it, and nothing in the emitted output distinguishes the two -- the program simply does not
    /// compile.
    /// </summary>
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

    // ── Refusals. Each stops a conformance edge being BUILT. ──────────────

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

    /// <summary>
    /// A property cannot answer a method slot. Refused here rather than left to C#, because the
    /// forwarding body the emitter would write names a real member and so fails as a syntax error on
    /// generated code.
    /// </summary>
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
    /// Both spellings of reaching one abstract twice are recorded on the model, because refusing them
    /// is emission's job today and not construction's. What the model holds is what
    /// TypeEmitter and ConformanceEmitter then see; see BackendDiagnostics.RedundantConformance for
    /// why the refusal lives where it does.
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

    /// <summary>
    /// A target that does not resolve is reported ONCE, as the unresolved name it is. Naming it again
    /// as the wrong kind of type would describe a consequence of that failure as a second fault.
    /// </summary>
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
    /// The conformance recorded on the named type, read from the TYPE rather than from the program
    /// model's flat list, since the type-keyed one is what assignability and emission consult.
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

    /// <summary>
    /// Builds a source file whose with block is stated in full, for the cases whose whole content is a
    /// malformed declaration the shared fixture cannot carry.
    /// </summary>
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