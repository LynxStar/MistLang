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
/// The closing witness over hand-built declarations (runtime-dependent-values design, <em>Identity by value: a closing
/// as a type argument</em>): the hidden parameter a method takes and the hidden field a type keeps, the witness a writer
/// builds from a literal, a capture and a dependent parameter, the test a body asks of it, the stamp a creation copies,
/// the holder a static is found in by it, the witness a body passes on, a nested Mist generic compared by value, an
/// operator reading it through its operand, and the C# a generic no such closing reaches keeps.
/// </summary>
[Trait("Tier", "2")]
public sealed class ClosingWitnessEmissionTests
{
    private const string Ring5 = "new MistClosing<Probe.Ring>([(int)(5)], static (__v, __s) => (__v is Probe.Ring __w1 && __w1.__Capacity == (int)__s[0]), null)";

    private readonly HandBuiltSlots slots = new();
    private readonly ClassType ring;
    private readonly TypeParameter capacity;
    private readonly ClassType tester;
    private readonly TypeParameter t;

    public ClosingWitnessEmissionTests()
    {
        ring = Ring(slots, out capacity);
        tester = Generic("Tester<>");
        t = tester.TypeParametersList[0];
        tester.MethodsList.Add(new Method("Is", slots.Refinements.Bool, [new Parameter("x", UnknownType.Instance)], new MemberModifiers { Accessibility = Accessibility.Public })
        {
            DeclaringType = tester,
            Scope = tester.Scope,
            Body = HandBuiltModel.Block(new ReturnStatement(TestOf(t), slots.Refinements.Bool)),
        });
    }

    [Fact]
    public void AMethodTakesTheWitnessOfAWitnessedParameterAsAHiddenLeadingParameter()
    {
        var (method, u) = Owned("Same");
        using var routes = SlotRoutes.Enter([capacity]);
        using var witnesses = ClosingWitnesses.Enter([u]);

        Assert.Equal("bool Same<U>(MistClosing<U> __closing_U, object x)", MethodEmitter.EmitSignature(method, new DiagnosticBag()));
    }

    [Fact]
    public void ATypeKeepsTheWitnessInAFieldEveryConstructorSetsAndAStaticMemberTakesIt()
    {
        tester.MethodsList.Add(new Method("Check", slots.Refinements.Bool, [new Parameter("x", UnknownType.Instance)], new MemberModifiers { Accessibility = Accessibility.Public, IsStatic = true })
        {
            DeclaringType = tester,
            Scope = tester.Scope,
            Body = HandBuiltModel.Block(new ReturnStatement(TestOf(t), slots.Refinements.Bool)),
        });
        using var routes = SlotRoutes.Enter([capacity]);
        using var witnesses = ClosingWitnesses.Enter([t]);

        var lines = TypeBody(tester);

        Assert.Contains("public readonly MistClosing<T> __closing_T;", lines);
        Assert.Contains("public Tester(MistClosing<T> __closing_T)", lines);
        Assert.Contains("this.__closing_T = __closing_T;", lines);
        Assert.Contains("public static Tester<T> __CreateDefault(MistClosing<T> __closing_T)", lines);
        Assert.Contains("public static bool Check(MistClosing<T> __closing_T, object x)", lines);
    }

    [Fact]
    public void AWriterBuildsTheWitnessFromALiteralACaptureOrADependentParameterAndAPlainTypeTakesTheSharedOne()
    {
        var capture = slots.Capture(new LocalVariable("n", slots.Int));
        var k = slots.Dependent("K");
        using var routes = SlotRoutes.Enter([capacity, k]);
        using var witnesses = ClosingWitnesses.Enter([t]);

        Assert.Equal($"new Probe.Tester<Probe.Ring>({Ring5})", Emit(New(slots.Closed(tester, slots.Closed(ring, slots.Literal(5))))));
        Assert.Equal(
            "new Probe.Tester<Probe.Ring>(new MistClosing<Probe.Ring>([(int)(n)], static (__v, __s) => (__v is Probe.Ring __w1 && __w1.__Capacity == (int)__s[0]), null))",
            Emit(New(slots.Closed(tester, slots.Closed(ring, capture.Value)), capture)));
        Assert.Equal(
            "new Probe.Tester<Probe.Ring>(new MistClosing<Probe.Ring>([(int)(__K)], static (__v, __s) => (__v is Probe.Ring __w1 && __w1.__Capacity == (int)__s[0]), null))",
            Emit(New(slots.Closed(tester, slots.Closed(ring, k)))));
        Assert.Equal("new Probe.Tester<int>(MistClosing<int>.Plain)", Emit(New(slots.Closed(tester, slots.Int))));
    }

    [Fact]
    public void ACaptureThatRunsCodeIsEvaluatedOnceWhenTheWitnessIsBuilt()
    {
        var capture = Capture(slots, Count(slots));
        using var routes = SlotRoutes.Enter([capacity]);
        using var witnesses = ClosingWitnesses.Enter([t]);

        var built = Emit(New(slots.Closed(tester, slots.Closed(ring, capture.Value)), capture));

        Assert.Equal(1, built.Split("p.Count").Length - 1);
        Assert.Contains("[(int)(p.Count)]", built, StringComparison.Ordinal);
        Assert.Contains("__w1.__Capacity == (int)__s[0]", built, StringComparison.Ordinal);
    }

    [Fact]
    public void ATestAsksTheWitnessBesideTheClrTest()
    {
        using var routes = SlotRoutes.Enter([capacity]);
        using var witnesses = ClosingWitnesses.Enter([t]);

        Assert.Equal("(x is T __e1 && __closing_T.Is(__e1))", Emit(TestOf(t)));
        Assert.Equal("x is T", Emit(Is(slots, HandBuiltRefinements.Local(t, "x"), Tested(t, null))));
    }

    [Fact]
    public void ACreationOfTheParametersStorageCopiesTheWitnesssStamp()
    {
        using var routes = SlotRoutes.Enter([capacity]);
        using var witnesses = ClosingWitnesses.Enter([t]);

        Assert.Equal(
            "MistArray.Stamp(new T[2], __closing_T.Stamp())",
            Emit(new ArrayCreationExpression(HandBuiltModel.Node(), t, slots.Refinements.Literal(2L), slots.Closed(slots.Array, t, slots.Literal(2)))));
    }

    [Fact]
    public void AStaticOfAWitnessedTypeIsHeldPerWitnessAndFoundThroughTheClosingsWitness()
    {
        var counter = Generic("Counter<>");
        var k = counter.TypeParametersList[0];
        var hits = new Field("hits", slots.Int, OwnershipKind.Unrestricted, new MemberModifiers { Accessibility = Accessibility.Public, IsStatic = true }) { DeclaringType = counter };
        counter.FieldsList.Add(hits);
        using var routes = SlotRoutes.Enter([capacity]);
        using var witnesses = ClosingWitnesses.Enter([k]);
        var closing = slots.Closed(counter, slots.Closed(ring, slots.Literal(5)));
        var access = new MemberAccessExpression(
            HandBuiltModel.Node(), new TypeReferenceExpression(HandBuiltModel.Node(), closing), "hits", slots.Int, member: new FieldResult(hits, closing, []));

        var lines = TypeBody(counter);

        Assert.Contains("public readonly MistClosing<T> __closing_T;", lines);
        Assert.Contains("private static readonly MistStatics.Table<MistClosing<T>, __Statics> __statics = new MistStatics.Table<MistClosing<T>, __Statics>(static key => new __Statics(key));", lines);
        Assert.Contains("public static __Statics __StaticsOf(MistClosing<T> __closing_T)", lines);
        Assert.Contains("return __statics.Of(__closing_T);", lines);
        Assert.Equal($"Probe.Counter<Probe.Ring>.__StaticsOf({Ring5}).hits", Emit(access));
    }

    [Fact]
    public void ABodyPassesItsWitnessOnToASecondGenericAndToATypeItCreates()
    {
        var (inner, i) = Owned("Inner");
        var (outer, o) = Owned("Outer");
        using var routes = SlotRoutes.Enter([capacity]);
        using var witnesses = ClosingWitnesses.Enter([t, i, o]);

        Assert.Equal("Inner<U>(__closing_U, x)", Emit(Call(inner, [o], [HandBuiltRefinements.Local(UnknownType.Instance, "x")], [UnknownType.Instance])));
        Assert.Equal("new Probe.Tester<U>(__closing_U)", Emit(New(slots.Closed(tester, o))));
        Assert.Equal($"Outer<Probe.Ring>({Ring5}, x)", Emit(Call(outer, [slots.Closed(ring, slots.Literal(5))], [HandBuiltRefinements.Local(UnknownType.Instance, "x")], [UnknownType.Instance])));
    }

    [Fact]
    public void ATestNestingTheParameterInAMistGenericComparesThatGenericsWitnessByValue()
    {
        var pair = Generic("Pair<>");
        var a = pair.TypeParametersList[0];
        using var routes = SlotRoutes.Enter([capacity]);
        using var witnesses = ClosingWitnesses.Enter([t, a]);

        Assert.Equal("(x is Probe.Pair<T> __e1 && __e1.__closing_T.Equals(__closing_T))", Emit(TestOf(slots.Closed(pair, t))));
    }

    [Fact]
    public void AStructHoldingTheParameterByValueStartsFromAStampCarryingTheWitness()
    {
        var holder = GenericStruct("Holder<>");
        using var routes = SlotRoutes.Enter([capacity]);
        using var witnesses = ClosingWitnesses.Enter([t]);
        var element = slots.Closed(holder, t);

        Assert.Equal(
            "MistArray.Stamp(new Probe.Holder<T>[2], Probe.Holder<T>.__FromParts(__closing_T.Stamp(), default(Mist.Emission.CSharp.__StructInit)))",
            Emit(new ArrayCreationExpression(HandBuiltModel.Node(), element, slots.Refinements.Literal(2L), slots.Closed(slots.Array, element, slots.Literal(2)))));
    }

    [Fact]
    public void AnOperatorReadsTheWitnessThroughTheOperandThatCarriesIt()
    {
        var less = new Operator(OperatorKind.LessThan, slots.Refinements.Bool, [new Parameter("left", slots.Closed(tester, t)), new Parameter("other", UnknownType.Instance)], new MemberModifiers { Accessibility = Accessibility.Public, IsStatic = true })
        {
            DeclaringType = tester,
            Scope = tester.Scope,
            Body = HandBuiltModel.Block(new ReturnStatement(TestOf(t, "other"), slots.Refinements.Bool)),
        };
        tester.OperatorsList.Add(less);
        using var routes = SlotRoutes.Enter([capacity]);
        using var witnesses = ClosingWitnesses.Enter([t]);

        Assert.Contains(TypeBody(tester), line => line.Contains("left.__closing_T.Is(", StringComparison.Ordinal));
    }

    [Fact]
    public void AGenericNoClosingOfAValueRoutedTypeFillsKeepsItsCSharp()
    {
        using var routes = SlotRoutes.Enter([capacity]);
        using var witnesses = ClosingWitnesses.Enter([]);

        var lines = TypeBody(tester);

        Assert.Contains("public Tester()", lines);
        Assert.Contains("return x is T;", lines);
        Assert.DoesNotContain(lines, line => line.Contains("__closing", StringComparison.Ordinal));
        Assert.Equal("new Probe.Tester<Probe.Ring>()", Emit(New(slots.Closed(tester, slots.Closed(ring, slots.Literal(5))))));
    }

    [Fact]
    public void TheRuntimeComparesTwoWitnessesByTheirValuesAndIsWrittenOnlyWhenAWitnessIs()
    {
        ClosingWitnesses.Reset();
        Assert.False(ClosingWitnesses.Used());

        using(SlotRoutes.Enter([capacity]))
        using(ClosingWitnesses.Enter([t]))
        {
            _ = TypeBody(tester);
        }

        var writer = new CSharpWriter();
        ClosingWitnesses.Emit(writer);
        var lines = writer.ToString().Split('\n').Select(line => line.Trim()).ToList();

        Assert.True(ClosingWitnesses.Used());
        Assert.Contains("public sealed class MistClosing<T> : System.IEquatable<MistClosing<T>>", lines);
        Assert.Contains("public static readonly MistClosing<T> Plain = new MistClosing<T>(System.Array.Empty<object>(), static (value, values) => value is T, null);", lines);
        Assert.Contains("return test(value, Values);", lines);
        Assert.Contains("return stamp == null ? default(T) : stamp(Values);", lines);
        Assert.Contains("if (!object.Equals(Values[i], values[i]))", lines);
        Assert.Contains("return other != null && Same(other.Values);", lines);
        Assert.Contains("hash.Add(value);", lines);
        ClosingWitnesses.Reset();
    }

    private (Method Method, TypeParameter Parameter) Owned(string name)
    {
        var owner = HandBuiltModel.Class("Api");
        var u = new TypeParameter("U", 0, new Scope(null), null);
        var method = new Method(name, slots.Refinements.Bool, [new Parameter("x", UnknownType.Instance)], new MemberModifiers { Accessibility = Accessibility.Public, IsStatic = true })
        {
            DeclaringType = owner,
            Scope = owner.Scope,
        };
        method.TypeParametersList.Add(u);
        u.DeclaringMethod = method;
        owner.MethodsList.Add(method);
        return (method, u);
    }

    private IsPatternExpression TestOf(IMistType type, string scrutinee = "x")
    {
        return Is(slots, HandBuiltRefinements.Local(UnknownType.Instance, scrutinee), Tested(type, null));
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