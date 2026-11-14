using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.RuntimeDependentValues;

/// <summary>
/// The six places a body writes a type that the binder refused before runtime-dependent-values MIST-1373: a typed
/// foreach variable, a typed deconstruction, a with binding, <c>default(T)</c>, the element type of an array creation
/// and a method group with stated arguments. Each declares its captures in the flow, so a closing it meets is proven
/// where <c>n == m</c> holds and refused as MIST0486 where it does not, judged by the refinement analyzer over
/// hand-built bodies.
/// </summary>
[Trait("Tier", "2")]
public sealed class BodySiteClosingProofTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly MemberType ring;
    private readonly MemberType cell;
    private readonly LocalVariable n;
    private readonly LocalVariable m;

    public BodySiteClosingProofTests()
    {
        n = new LocalVariable("n", slots.Int);
        m = new LocalVariable("m", slots.Int);
        ring = HandBuiltSlots.Generic(HandBuiltModel.Class("Ring<>"), "Capacity", slots.Int);
        cell = HandBuiltSlots.Generic(HandBuiltModel.Struct("Cell<>"), "N", slots.Int);
    }

    public static TheoryData<string> Sites { get; } = ["foreach", "deconstruction", "with", "default", "array", "method group"];

    [Theory]
    [MemberData(nameof(Sites))]
    public void AClosingWrittenAtTheSiteIsProvenWhereTheValuesAreEqualAndRefusedWhereTheyAreNot(string site)
    {
        var refused = Assert.Single(Analyze(Site(site)));

        Assert.Equal("MIST0486", refused.Number.Value);
        Assert.Contains("are not proven identical", refused.Message, StringComparison.Ordinal);
        Assert.Empty(Analyze([HandBuiltRefinements.If(slots.Refinements.Binary(HandBuiltRefinements.Read(n), BinaryOperatorKind.Equal, HandBuiltRefinements.Read(m)), Site(site))]));
    }

    private IReadOnlyList<Diagnostic> Analyze(params Statement[] statements)
    {
        return slots.Refinements.Built.Analyze(new RefinementAnalyzer(), HandBuiltModel.Block(statements));
    }

    private Statement[] Site(string site)
    {
        var ofN = slots.Capture(n, slots.Int);
        var ofM = slots.Capture(m, slots.Int);
        var ringN = slots.Closed(ring, ofN.Value);
        var ringM = slots.Closed(ring, ofM.Value);

        return site switch
        {
            "foreach" => Held("rings", slots.Closed(slots.Array, ringN, UnknownType.Instance), ofN, rings =>
                new ForEachStatement(new LocalVariable("r", ringM), rings, HandBuiltModel.Block()) { Captures = [ofM] }),
            "deconstruction" => Held("pair", slots.Refinements.Built.Interner.GetOrCreateTupleType([ringN, slots.Int]), ofN, pair =>
                new TupleDeconstructionStatement([new LocalVariable("x", ringM), new LocalVariable("y", slots.Int)], pair) { Captures = [ofM] }),
            "with" => Held("made", ringN, ofN, made =>
                new WithBlockStatement(made, HandBuiltModel.Block(), new LocalVariable("w", ringM), ringM) { Captures = [ofM] }),
            "default" => [HandBuiltSlots.Declare(new LocalVariable("d", ringM), new DefaultExpression(HandBuiltModel.Node(), ringN) { Captures = [ofN] }, ofM)],
            "array" => [Array(ofN, ofM)],
            "method group" => [Group(ofN, ofM)],
            _ => throw new ArgumentOutOfRangeException(nameof(site), site, null),
        };
    }

    private static Statement[] Held(string name, IMistType type, SlotCapture capture, Func<Expression, Statement> site)
    {
        var local = new LocalVariable(name, type);

        return [HandBuiltSlots.Declare(local, HandBuiltRefinements.Local(type, $"{name}Source"), capture), site(HandBuiltRefinements.Read(local))];
    }

    private LocalDeclarationStatement Array(SlotCapture ofN, SlotCapture ofM)
    {
        var cellN = slots.Closed(cell, ofN.Value);
        var created = new ArrayCreationExpression(HandBuiltModel.Node(), cellN, slots.Refinements.Literal(2L), slots.Closed(slots.Array, cellN, slots.Literal(2))) { Captures = [ofN] };

        return HandBuiltSlots.Declare(new LocalVariable("cells", slots.Closed(slots.Array, slots.Closed(cell, ofM.Value), UnknownType.Instance)), created, ofM);
    }

    private LocalDeclarationStatement Group(SlotCapture ofN, SlotCapture ofM)
    {
        var bag = HandBuiltModel.Class("Bag<>");
        var t = new TypeParameter("T", 0, bag.Scope, bag);
        bag.TypeParametersList.Add(t);
        var item = new TypeParameter("U", 0, new Scope(null), null);
        var first = new Method("First", item, [new Parameter("items", slots.Closed(bag, item))], new MemberModifiers { IsStatic = true, Accessibility = Accessibility.Public });
        first.TypeParametersList.Add(item);
        item.DeclaringMethod = first;
        var ringN = slots.Closed(ring, ofN.Value);
        var ringM = slots.Closed(ring, ofM.Value);
        var target = slots.Refinements.Built.Interner.GetOrCreateFunctionType([slots.Closed(bag, ringM)], ringM);
        var reference = new FunctionReferenceExpression(HandBuiltModel.Node(), null, first, target)
        {
            TypeArguments = [ringN],
            Captures = [ofN],
            ClosedParameterTypes = [slots.Closed(bag, ringN)],
            ClosedReturnType = ringN,
        };

        return HandBuiltSlots.Declare(new LocalVariable("f", target), reference, ofM);
    }
}