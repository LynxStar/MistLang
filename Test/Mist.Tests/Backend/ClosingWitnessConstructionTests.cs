using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Backend.ValueRouteProbes;

namespace Mist.Tests.Backend;

/// <summary>
/// A witnessed type built where C# would build it without its witness (runtime-dependent-values design, <em>Identity by
/// value: a closing as a type argument</em>), over hand-built declarations: an instance initializer that reads the
/// witness runs in the constructor after the witness is set, a <c>new()</c> parameter filled with a witnessed type
/// builds through a factory that supplies the witness once, built where the type is written, and declares no C#
/// constructor constraint, and a static holder over such a parameter takes the factory from where it is found.
/// </summary>
[Trait("Tier", "2")]
public sealed class ClosingWitnessConstructionTests
{
    private const string Ring5 = "new MistClosing<Probe.Ring>([(int)(5)], static (__v, __s) => (__v is Probe.Ring __w1 && __w1.__Capacity == (int)__s[0]), null)";

    private readonly HandBuiltSlots slots = new();
    private readonly ClassType ring;
    private readonly TypeParameter capacity;
    private readonly ClassType tester;
    private readonly TypeParameter t;
    private readonly ClassType creator;
    private readonly TypeParameter c;

    public ClosingWitnessConstructionTests()
    {
        ring = Ring(slots, out capacity);
        tester = Generic("Tester<>");
        t = tester.TypeParametersList[0];
        creator = Generic("Creator<>");
        c = creator.TypeParametersList[0];
        c.ConstraintsList.Add(new ConstructorConstraint([]));
        creator.MethodsList.Add(new Method("Create", c, [], new MemberModifiers { Accessibility = Accessibility.Public })
        {
            DeclaringType = creator,
            Scope = creator.Scope,
            Body = HandBuiltModel.Block(new ReturnStatement(New(c), c)),
        });
    }

    [Fact]
    public void AnInstanceInitializerOfAWitnessedTypeRunsInTheConstructorAfterTheWitnessIsSet()
    {
        var wrap = Generic("Wrap<>");
        var w = wrap.TypeParametersList[0];
        var inner = slots.Closed(tester, w);
        wrap.FieldsList.Add(new Field("inner", inner, OwnershipKind.Unrestricted, new MemberModifiers { Accessibility = Accessibility.Public })
        {
            DeclaringType = wrap,
            Initializer = New(inner),
        });
        using var routes = SlotRoutes.Enter([capacity]);
        using var witnesses = ClosingWitnesses.Enter([t, w]);

        var lines = TypeBody(wrap);

        Assert.Contains("public Probe.Tester<T> inner;", lines);
        Assert.DoesNotContain(lines, line => line.StartsWith("public Probe.Tester<T> inner =", StringComparison.Ordinal));
        var set = lines.IndexOf("this.__closing_T = __closing_T;");
        var initialized = lines.IndexOf("this.inner = new Probe.Tester<T>(__closing_T);");
        Assert.True(set >= 0 && initialized > set, string.Join("\n", lines));
    }

    [Fact]
    public void ANewConstrainedParameterAWitnessedTypeFillsDeclaresNoCSharpConstraintAndBuildsOnlyThroughItsFactory()
    {
        using var routes = SlotRoutes.Enter([capacity]);
        using var witnesses = ClosingWitnesses.Enter([t], [c]);

        var lines = TypeBody(creator);

        Assert.DoesNotContain(lines, line => line.StartsWith("where T", StringComparison.Ordinal));
        Assert.Contains("return __construct_T_0();", lines);
        Assert.Contains(lines, line => line.Contains("() => throw new System.InvalidOperationException(\"T is built only with its closing witness\")", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("new T()", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnfactoredNewConstrainedParameterKeepsItsCSharpConstraint()
    {
        using var routes = SlotRoutes.Enter([capacity]);
        using var witnesses = ClosingWitnesses.Enter([t]);

        var lines = TypeBody(creator);

        Assert.Contains("where T : new()", lines);
        Assert.Contains(lines, line => line.Contains("() => new T()", StringComparison.Ordinal));
    }

    [Fact]
    public void TheFactoryForAWitnessedTypeBuildsTheWitnessOnceWhereTheTypeIsWritten()
    {
        using var routes = SlotRoutes.Enter([capacity]);
        using var witnesses = ClosingWitnesses.Enter([t], [c]);

        Assert.Equal(
            $"new Probe.Creator<Probe.Tester<Probe.Ring>>((({Ring5}) switch {{ var __closing0_0 => () => new Probe.Tester<Probe.Ring>(__closing0_0) }}))",
            Emit(New(slots.Closed(creator, slots.Closed(tester, slots.Closed(ring, slots.Literal(5)))))));
        Assert.Equal(
            "new Probe.Creator<Probe.Tester<int>>(() => new Probe.Tester<int>(MistClosing<int>.Plain))",
            Emit(New(slots.Closed(creator, slots.Closed(tester, slots.Int)))));
    }

    [Fact]
    public void ACaptureTheFactorysWitnessReadsIsEvaluatedOnceAndNotAgainAsADiscardedRead()
    {
        var capture = Capture(slots, Count(slots));
        using var routes = SlotRoutes.Enter([capacity]);
        using var witnesses = ClosingWitnesses.Enter([t], [c]);

        var built = Emit(New(slots.Closed(creator, slots.Closed(tester, slots.Closed(ring, capture.Value))), capture));

        Assert.Equal(1, built.Split("p.Count").Length - 1);
        Assert.Contains("switch { var __closing0_0 => () => new Probe.Tester<Probe.Ring>(__closing0_0) }", built, StringComparison.Ordinal);
    }

    [Fact]
    public void AStaticHolderOverAFactoredParameterTakesTheFactoryFromWhereItIsFound()
    {
        var kept = Generic("Kept<>");
        var k = kept.TypeParametersList[0];
        k.ConstraintsList.Add(new ConstructorConstraint([]));
        var made = new Field("made", k, OwnershipKind.Unrestricted, new MemberModifiers { Accessibility = Accessibility.Public, IsStatic = true })
        {
            DeclaringType = kept,
            Initializer = New(k),
        };
        kept.FieldsList.Add(made);
        using var routes = SlotRoutes.Enter([capacity]);
        using var witnesses = ClosingWitnesses.Enter([t, k], [k]);
        var closing = slots.Closed(kept, slots.Closed(tester, slots.Closed(ring, slots.Literal(5))));
        var access = new MemberAccessExpression(
            HandBuiltModel.Node(), new TypeReferenceExpression(HandBuiltModel.Node(), closing), "made", k, member: new FieldResult(made, closing, []));

        var lines = TypeBody(kept);

        Assert.Contains("private readonly System.Func<T> __construct_T_0;", lines);
        Assert.Contains("this.made = __construct_T_0();", lines);
        Assert.Contains("public static __Statics __StaticsOf(MistClosing<T> __closing_T, System.Func<T> __construct_T_0)", lines);
        Assert.Contains("return __statics.Of(__closing_T, key => new __Statics(key, __construct_T_0));", lines);
        Assert.StartsWith("Probe.Kept<Probe.Tester<Probe.Ring>>.__StaticsOf(new MistClosing<Probe.Tester<Probe.Ring>>(", Emit(access), StringComparison.Ordinal);
        Assert.Contains($"(({Ring5}) switch {{ var __closing0_0 => () => new Probe.Tester<Probe.Ring>(__closing0_0) }})).made", Emit(access), StringComparison.Ordinal);
    }

    private static NewExpression New(IMistType type, params SlotCapture[] captures)
    {
        return new NewExpression(HandBuiltModel.Node(), type, [], null, [], null) { Captures = captures };
    }

    private List<string> TypeBody(MemberType type)
    {
        var diagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();
        BodyEmitter.ResetTemporaries();
        TypeEmitter.EmitTypeBody(type, ConstraintClosureMap.Build(slots.Refinements.Built.Model, diagnostics), slots.Refinements.Built.Model.VariadicUses, writer, diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.ToImmutableArray().Select(diagnostic => diagnostic.Message)));
        return [.. writer.ToString().Split('\n').Select(line => line.Trim())];
    }
}