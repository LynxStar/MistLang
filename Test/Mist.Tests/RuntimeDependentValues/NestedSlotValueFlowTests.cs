using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.RuntimeDependentValues;

/// <summary>
/// A slot nested in a type argument that stands for one value, judged by the refinement analyzer over
/// hand-built bodies (runtime-dependent-values Requirements 2.5, 7.6 and 8): a call's own capture of its
/// dependent parameter and a receiver's own slot are met by equality as well as by refinement equality, and
/// a receiver's own slot read through a member is that value wherever it is nested. A capture written inside a
/// type the call states stands for its refinement.
/// </summary>
[Trait("Tier", "2")]
public sealed class NestedSlotValueFlowTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly MemberType ring;
    private readonly TypeParameter capacity;
    private readonly MemberType list;
    private readonly LocalVariable n;
    private readonly LocalVariable m;

    public NestedSlotValueFlowTests()
    {
        ring = HandBuiltSlots.Generic(HandBuiltModel.Class("Ring<>"), "Capacity", slots.Int);
        capacity = ring.TypeParametersList[0];
        list = HandBuiltModel.Class("List<>");
        list.TypeParametersList.Add(new TypeParameter("T", 0, list.Scope, list));
        var range = slots.Refinements.Between(slots.Int, 11L, 29L, "Range");
        n = new LocalVariable("n", range);
        m = new LocalVariable("m", range);
    }

    private IMistType Void => slots.Refinements.Built.Type("Void");

    private IReadOnlyList<Diagnostic> Analyze(params Statement[] statements)
    {
        return slots.Refinements.Built.Analyze(new RefinementAnalyzer(), HandBuiltModel.Block(statements));
    }

    private static void Refused(IReadOnlyList<Diagnostic> diagnostics, string message)
    {
        var refused = Assert.Single(diagnostics);
        Assert.Equal("MIST0486", refused.Number.Value);
        Assert.Contains(message, refused.Message, StringComparison.Ordinal);
    }

    private ConstructedType Rings(IMistType slot)
    {
        return slots.Closed(list, slots.Closed(ring, slot));
    }

    private (LocalVariable Rings, LocalDeclarationStatement Declared) RingsOf(LocalVariable name)
    {
        var written = slots.Capture(name, slots.Int);
        var rings = new LocalVariable("rings", Rings(written.Value));
        return (rings, HandBuiltSlots.Declare(rings, HandBuiltRefinements.Local(rings.Type, "made"), written));
    }

    private static SlotCapture Own(LocalVariable receiver, TypeParameter parameter, IMistType domain, int position)
    {
        var read = new MemberAccessExpression(HandBuiltModel.Node(), HandBuiltRefinements.Read(receiver), parameter.Name, domain, member: new TypeResult(parameter));
        return new SlotCapture($"{receiver.Name}.{parameter.Name}", domain, domain, read, position);
    }

    [Fact]
    public void ACallsOwnCaptureNestedInAnArgumentIsMetByItsValueUnlessWrittenInsideAStatedType()
    {
        ExpressionStatement call(Expression rings, SlotCapture stated, bool insideType = false)
        {
            var element = slots.Closed(ring, stated.Value);
            var taken = slots.Closed(list, element);
            var method = new Method("Run", Void, [new Parameter("rings", taken)], new MemberModifiers { IsStatic = true });
            var callee = new MethodReferenceExpression(HandBuiltModel.Node(), method, [insideType ? element : stated.Value]);
            return HandBuiltRefinements.Evaluate(new InvocationExpression(HandBuiltModel.Node(), callee, [rings], Void, method, [taken]) { Captures = [stated] });
        }

        var (rings, declared) = RingsOf(n);

        Refused(Analyze(declared, call(HandBuiltRefinements.Read(rings), slots.Capture(m, slots.Int))), "'n' and 'm' are not proven identical");
        Assert.Empty(Analyze(declared, call(HandBuiltRefinements.Read(rings), slots.Capture(m, slots.Int), insideType: true)));

        var literal = new LocalVariable("fifteen", Rings(slots.Literal(15)));
        var fifteen = slots.Refinements.Binary(HandBuiltRefinements.Read(m), BinaryOperatorKind.Equal, slots.Refinements.Literal(15L));
        var given = new LocalDeclarationStatement(literal, HandBuiltRefinements.Local(literal.Type, "made"));

        Assert.Empty(Analyze(given, HandBuiltRefinements.If(fifteen, call(HandBuiltRefinements.Read(literal), slots.Capture(m, slots.Int)))));
        Refused(Analyze(given, call(HandBuiltRefinements.Read(literal), slots.Capture(m, slots.Int))), "'15' and 'm' are not proven identical");
    }

    [Fact]
    public void ACreationsOwnCaptureNestedInAConstructorArgumentIsMetByItsValue()
    {
        var constructor = new Constructor("Ring", ring, [new Parameter("rings", Rings(capacity))], new MemberModifiers());

        ExpressionStatement create(Expression rings, LocalVariable closing)
        {
            var made = slots.Capture(closing, slots.Int);
            return HandBuiltRefinements.Evaluate(new NewExpression(HandBuiltModel.Node(), slots.Closed(ring, made.Value), [rings], constructor, [], [Rings(made.Value)]) { Captures = [made] });
        }

        var (rings, declared) = RingsOf(n);
        var same = slots.Refinements.Binary(HandBuiltRefinements.Read(n), BinaryOperatorKind.Equal, slots.Refinements.Literal(15L));

        Refused(Analyze(declared, create(HandBuiltRefinements.Read(rings), m)), "'n' and 'm' are not proven identical");

        var literal = new LocalVariable("fifteen", Rings(slots.Literal(15)));
        var given = new LocalDeclarationStatement(literal, HandBuiltRefinements.Local(literal.Type, "made"));

        Assert.Empty(Analyze(given, HandBuiltRefinements.If(same, create(HandBuiltRefinements.Read(literal), n))));
    }

    [Fact]
    public void AReceiversOwnSlotReadThroughAMemberIsThatOneValueWhereverItIsNested()
    {
        var made = slots.Capture(m, slots.Int);
        var written = slots.Capture(n, slots.Int);
        var e = new LocalVariable("e", slots.Closed(ring, written.Value));
        var creation = new NewExpression(HandBuiltModel.Node(), slots.Closed(ring, made.Value), [], null, [], []) { Captures = [made] };
        var declared = HandBuiltSlots.Declare(e, creation, written);
        var property = new Property("Rings", Rings(capacity), new MemberModifiers()) { DeclaringType = ring };

        Expression member()
        {
            var own = Own(e, capacity, slots.Int, 96);
            return new MemberAccessExpression(HandBuiltModel.Node(), HandBuiltRefinements.Read(e), "Rings", Rings(own.Value), member: new PropertyResult(property, ring, []))
            {
                Captures = [own],
            };
        }

        var method = new Method("Run", Void, [new Parameter("rings", Rings(capacity))], new MemberModifiers()) { DeclaringType = ring };

        ExpressionStatement run(Expression rings)
        {
            var own = Own(e, capacity, slots.Int, 97);
            var callee = new MemberAccessExpression(HandBuiltModel.Node(), HandBuiltRefinements.Read(e), "Run", Void) { Captures = [own] };
            return HandBuiltRefinements.Evaluate(new InvocationExpression(HandBuiltModel.Node(), callee, [rings], Void, method, [Rings(own.Value)]));
        }

        Assert.Empty(Analyze(declared, run(member())));

        var (rings, ofN) = RingsOf(n);

        Refused(Analyze(declared, ofN, run(HandBuiltRefinements.Read(rings))), "'n' and 'e.Capacity' are not proven identical");

        var element = new IndexExpression(HandBuiltModel.Node(), HandBuiltRefinements.Read(rings), slots.Refinements.Literal(0), slots.Closed(ring, ofN.Captures[0].Value));
        var throughElement = new MemberAccessExpression(HandBuiltModel.Node(), element, "Run", Void);
        var itself = new InvocationExpression(HandBuiltModel.Node(), throughElement, [HandBuiltRefinements.Read(rings)], Void, method, [Rings(ofN.Captures[0].Value)]);

        Refused(Analyze(ofN, HandBuiltRefinements.Evaluate(itself)), "'n' and 'Ring<n>.Capacity' are not proven identical");

        var alias = slots.Capture(n, slots.Int);

        Refused(Analyze(declared, HandBuiltSlots.Declare(new LocalVariable("alias", Rings(alias.Value)), member(), alias)), "'e.Capacity' and 'n' are not proven identical");
    }
}