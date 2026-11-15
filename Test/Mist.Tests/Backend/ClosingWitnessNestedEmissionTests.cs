using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Backend.ValueRouteProbes;

namespace Mist.Tests.Backend;

/// <summary>
/// The closing witness meeting a nested type over hand-built declarations (runtime-dependent-values design, <em>Identity
/// by value: a closing as a type argument</em> beside <em>conversions and nested types</em>): a witness for a nested
/// closing of a value-routed type compares the enclosing slot the nested instance carries, a test against a type nested
/// in a witnessed generic asks the enclosing witness, and a static of a nested type is found through the enclosing
/// closing written rather than the bare nested declaration.
/// </summary>
[Trait("Tier", "2")]
public sealed class ClosingWitnessNestedEmissionTests
{
    private const string Ring5 = "new MistClosing<Probe.Ring>([(int)(5)], static (__v, __s) => (__v is Probe.Ring __w1 && __w1.__Capacity == (int)__s[0]), null)";

    private readonly HandBuiltSlots slots = new();
    private readonly ClassType ring;
    private readonly TypeParameter capacity;
    private readonly ClassType tester;
    private readonly TypeParameter t;

    public ClosingWitnessNestedEmissionTests()
    {
        ring = Ring(slots, out capacity);
        tester = Generic("Tester<>");
        t = tester.TypeParametersList[0];
    }

    [Fact]
    public void AWitnessOfANestedClosingComparesTheEnclosingSlotTheNestedInstanceCarries()
    {
        var node = Nested(ring, "Node");
        using var routes = SlotRoutes.Enter([capacity]);
        using var witnesses = ClosingWitnesses.Enter([t]);

        Assert.Equal(
            "new Probe.Tester<Probe.Ring.Node>(new MistClosing<Probe.Ring.Node>([(int)(5)], static (__v, __s) => (__v is Probe.Ring.Node __w1 && __w1.__Capacity == (int)__s[0]), null))",
            Emit(New(slots.Closed(tester, new ConstructedType(node, [], slots.Closed(ring, slots.Literal(5)))))));
    }

    [Fact]
    public void ATestAgainstATypeNestedInAWitnessedGenericAsksTheEnclosingWitness()
    {
        var node = Nested(tester, "Node");
        var check = HandBuiltModel.Class("Check<>");
        var u = new TypeParameter("U", 0, check.Scope, check);
        check.TypeParametersList.Add(u);
        using var routes = SlotRoutes.Enter([capacity]);
        using var witnesses = ClosingWitnesses.Enter([t, u]);

        Assert.Equal(
            "(x is Probe.Tester<U>.Node __e1 && __e1.__closing_T.Equals(__closing_U))",
            Emit(Is(slots, HandBuiltRefinements.Local(UnknownType.Instance, "x"), Tested(new ConstructedType(node, [], slots.Closed(tester, u)), null))));
    }

    [Fact]
    public void AStaticOfANestedTypeIsFoundThroughTheEnclosingClosingWritten()
    {
        var count = Nested(tester, "Count");
        var hits = Hits(count);
        var cell = Nested(ring, "Cell");
        var size = Hits(cell);
        using var routes = SlotRoutes.Enter([capacity]);
        using var witnesses = ClosingWitnesses.Enter([t]);

        Assert.Equal($"Probe.Tester<Probe.Ring>.Count.__StaticsOf({Ring5}).hits", Emit(Access(count, hits, slots.Closed(tester, slots.Closed(ring, slots.Literal(5))))));
        Assert.Equal("Probe.Ring.Cell.__StaticsOf(3).hits", Emit(Access(cell, size, slots.Closed(ring, slots.Literal(3)))));
    }

    private Field Hits(ClassType owner)
    {
        var hits = new Field("hits", slots.Int, OwnershipKind.Unrestricted, new MemberModifiers { Accessibility = Accessibility.Public, IsStatic = true }) { DeclaringType = owner };
        owner.FieldsList.Add(hits);
        return hits;
    }

    private MemberAccessExpression Access(ClassType owner, Field field, ConstructedType enclosing)
    {
        var closing = new ConstructedType(owner, [], enclosing);
        return new MemberAccessExpression(
            HandBuiltModel.Node(), new TypeReferenceExpression(HandBuiltModel.Node(), closing), field.Name, slots.Int, member: new FieldResult(field, closing, []));
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

    private static NewExpression New(IMistType type, params SlotCapture[] captures)
    {
        return new NewExpression(HandBuiltModel.Node(), type, [], null, [], null) { Captures = captures };
    }
}