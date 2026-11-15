using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Backend.ValueRouteProbes;

namespace Mist.Tests.Backend;

/// <summary>
/// A type nested in a value-routed type over hand-built declarations (runtime-dependent-values design, <em>Identity by
/// value: conversions and nested types</em>): the enclosing slot carried as the nested type's own hidden field, set
/// from a hidden constructor parameter that every creation fills, from the enclosing instance's field or from the
/// closing written, at one and at two levels; a test against a nested closing; the enclosing type's static storage
/// reached through the carried value; and, on the carrier route, the nested type spelled through the enclosing C#
/// type parameter.
/// </summary>
[Trait("Tier", "2")]
public sealed class ValueRouteNestedEmissionTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly ClassType ring;
    private readonly TypeParameter capacity;
    private readonly ClassType node;

    public ValueRouteNestedEmissionTests()
    {
        ring = Ring(slots, out capacity);
        node = Nested(ring, "Node");
        node.MethodsList.Add(new Method("Limit", slots.Int, [], new MemberModifiers { Accessibility = Accessibility.Public })
        {
            DeclaringType = node,
            Scope = node.Scope,
            Body = HandBuiltModel.Block(new ReturnStatement(new TypeReferenceExpression(HandBuiltModel.Node(), capacity), slots.Int)),
        });
    }

    [Fact]
    public void ANestedTypeCarriesTheEnclosingSlotInAHiddenFieldItsConstructorSetsAndReadsIt()
    {
        using var routes = SlotRoutes.Enter([capacity]);

        var lines = TypeBody(node);

        Assert.Equal([capacity], SlotRoutes.ValueRouted(node));
        Assert.Contains("public class Node", lines);
        Assert.Contains("public readonly int __Capacity;", lines);
        Assert.Contains("public Node(int __Capacity)", lines);
        Assert.Contains("this.__Capacity = __Capacity;", lines);
        Assert.Contains("return __Capacity;", lines);
        Assert.Equal("Probe.Ring.Node", TypeNameEmitter.Emit(Named()));
        Assert.Equal(["__Capacity"], BodyEmitter.SlotValues(node, diagnostics: null));
    }

    [Fact]
    public void ACreationInsideTheEnclosingTypeFillsTheParameterFromItsFieldOrItsHiddenParameter()
    {
        ring.MethodsList.Add(Making("Make", isStatic: false));
        ring.MethodsList.Add(Making("MakeStatic", isStatic: true));
        using var routes = SlotRoutes.Enter([capacity]);

        var lines = TypeBody(ring);

        Assert.Contains("public Probe.Ring.Node Make()", lines);
        Assert.Contains("public static Probe.Ring.Node MakeStatic(int __Capacity)", lines);
        Assert.Equal(2, lines.Count(line => line == "return new Probe.Ring.Node(__Capacity);"));
    }

    [Fact]
    public void ACreationOutsideFillsTheParameterFromTheClosingWrittenAtOneAndAtTwoLevels()
    {
        var leaf = Nested(node, "Leaf");
        var k = slots.Dependent("K");
        var n = slots.Capture(new LocalVariable("n", slots.Int));
        using var routes = SlotRoutes.Enter([capacity, k]);

        Assert.Equal("new Probe.Ring.Node(5)", Emit(Create(Through(slots.Literal(5)))));
        Assert.Equal("new Probe.Ring.Node(n)", Emit(Create(Through(n.Value), n)));
        Assert.Equal("new Probe.Ring.Node(__K)", Emit(Create(Through(k))));
        Assert.Equal("new Probe.Ring.Node.Leaf(n)", Emit(Create(new ConstructedType(leaf, [], Through(n.Value)), n)));
        Assert.Equal([capacity], SlotRoutes.ValueRouted(leaf));
    }

    [Fact]
    public void ATestAgainstANestedClosingComparesTheCarriedValue()
    {
        var n = slots.Capture(new LocalVariable("n", slots.Int));
        var value = HandBuiltRefinements.Local(UnknownType.Instance, "x");
        using var routes = SlotRoutes.Enter([capacity]);

        Assert.Equal("(x is Probe.Ring.Node t && t.__Capacity == n)", Emit(Is(slots, value, Tested(Through(n.Value), "t"))));
        Assert.Equal(
            "(x is var __e1 && !((__e1 is Probe.Ring.Node __e2 && __e2.__Capacity == n)))",
            EmitFresh(Is(slots, value, new NegatedPattern(HandBuiltModel.Node(), UnknownType.Instance, Tested(Through(n.Value), null)))));
    }

    [Fact]
    public void ANestedTypeReachesTheEnclosingTypesStaticStorageThroughItsCarriedValue()
    {
        var hits = new Field("hits", slots.Int, OwnershipKind.Unrestricted, new MemberModifiers { Accessibility = Accessibility.Public, IsStatic = true }) { DeclaringType = ring };
        ring.FieldsList.Add(hits);
        node.MethodsList.Add(new Method("Hits", slots.Int, [], new MemberModifiers { Accessibility = Accessibility.Public })
        {
            DeclaringType = node,
            Scope = node.Scope,
            Body = HandBuiltModel.Block(new ReturnStatement(new FieldReferenceExpression(HandBuiltModel.Node(), hits, slots.Int), slots.Int)),
        });
        using var routes = SlotRoutes.Enter([capacity]);

        Assert.Contains("return Probe.Ring.__StaticsOf(__Capacity).hits;", TypeBody(node));
    }

    [Fact]
    public void ANestedAbstractDeclaresTheCarriedGetterAndANestedTypeImplementingItAnswersWithItsOwnValue()
    {
        var sized = new AbstractType("ISized", "Probe", new Scope(ring.Scope), ring, new BodyTypeTypeDeclarationNode())
        {
            Modifiers = new MemberModifiers { Accessibility = Accessibility.Public },
        };
        node.BaseAbstractTypes.Add(sized);
        using var routes = SlotRoutes.Enter([capacity]);

        Assert.Contains("int __Capacity { get; }", TypeBody(sized));
        Assert.Contains("int Probe.Ring.ISized.__Capacity => __Capacity;", TypeBody(node));
    }

    [Fact]
    public void OnTheCarrierRouteTheNestedTypeIsSpelledThroughTheEnclosingCSharpParameterAndCarriesNothing()
    {
        ring.MethodsList.Add(Making("Make", isStatic: false));
        using var routes = SlotRoutes.Enter([]);

        var lines = TypeBody(ring);

        Assert.Contains("public Probe.Ring<TCapacity>.Node Make()", lines);
        Assert.Contains("return new Probe.Ring<TCapacity>.Node();", lines);
        Assert.Contains("return ((int)TCapacity.Value);", lines);
        Assert.DoesNotContain(lines, line => line.Contains("__Capacity", StringComparison.Ordinal));
        Assert.Empty(SlotRoutes.ValueRouted(node));
    }

    private static ClassType Nested(MemberType enclosing, string name)
    {
        var nested = new ClassType(name, "Probe", new Scope(enclosing.Scope), enclosing, new BodyTypeTypeDeclarationNode())
        {
            Modifiers = new MemberModifiers { Accessibility = Accessibility.Public },
        };
        enclosing.NestedTypes.Add(nested);
        return nested;
    }

    private Method Making(string name, bool isStatic)
    {
        return new Method(name, Named(), [], new MemberModifiers { Accessibility = Accessibility.Public, IsStatic = isStatic })
        {
            DeclaringType = ring,
            Scope = ring.Scope,
            Body = HandBuiltModel.Block(new ReturnStatement(new NewExpression(HandBuiltModel.Node(), Named(), [], null, [], null), Named())),
        };
    }

    private ConstructedType Named()
    {
        return new ConstructedType(node, []);
    }

    private ConstructedType Through(IMistType argument)
    {
        return new ConstructedType(node, [], slots.Closed(ring, argument));
    }

    private static NewExpression Create(IMistType type, params SlotCapture[] captures)
    {
        return new NewExpression(HandBuiltModel.Node(), type, [], null, [], null) { Captures = captures };
    }

    private static string EmitFresh(Expression expression)
    {
        BodyEmitter.ResetTemporaries();
        return Emit(expression);
    }

    private List<string> TypeBody(MemberType type)
    {
        var diagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();
        TypeEmitter.EmitTypeBody(type, ConstraintClosureMap.Build(slots.Refinements.Built.Model, diagnostics), slots.Refinements.Built.Model.VariadicUses, writer, diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.ToImmutableArray().Select(diagnostic => diagnostic.Message)));
        return [.. writer.ToString().Split('\n').Select(line => line.Trim())];
    }
}