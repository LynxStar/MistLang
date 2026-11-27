using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.HandBuiltModel;
using static Mist.Tests.Helpers.HandBuiltRefinements;
using static Mist.Tests.ProgramAnalysis.RangeFlowBody;

namespace Mist.Tests.ProgramAnalysis;

public sealed class RangeIndexMemberDecisionTests
{
    [Theory]
    [Trait("Tier", "2")]
    [InlineData("stored-end")]
    [InlineData("offset-overwritten")]
    [InlineData("copy-before-write")]
    [InlineData("range-before-write")]
    [InlineData("reassigned-end")]
    [InlineData("same-at-join")]
    [InlineData("same-in-loop")]
    [InlineData("equivalent-range-at-join")]
    [InlineData("two-stored-ends")]
    [InlineData("empty-at-end")]
    [InlineData("element")]
    [InlineData("narrowed-offset")]
    [InlineData("string-element")]
    public void StoredIndexes_PreserveTheirCapturedOffsetsAndMarkers(string scenario)
    {
        var r = new RangeFlowBody();
        var h = r.Values;
        var end = Local(h.Index, "end");
        var start = Local(h.Index, "start");
        var offset = Local(h.Int, "offset");
        var initial = Give(end, r.Back(1));
        var capture = Give(r.Window, r.To(end));
        Statement[] body = scenario switch
        {
            "stored-end" => [initial, capture, r.ReadWindow(3)],
            "offset-overwritten" => [Give(offset, r.Number(1)), Give(end, h.Back(offset)), Set(offset, r.Number(5)), capture, r.ReadWindow(3)],
            "copy-before-write" => [initial, Give(start, end), Set(end, r.Back(4)), Give(r.Window, r.To(start)), r.ReadWindow(3)],
            "range-before-write" => [initial, capture, Set(end, r.Back(4)), r.ReadWindow(3)],
            "reassigned-end" => [initial, Set(end, r.Back(2)), capture, r.ReadWindow(2)],
            "same-at-join" => [initial, If(r.Flag, Set(end, r.Back(1))), capture, r.ReadWindow(3)],
            "same-in-loop" => [initial, new WhileStatement(r.Flag, Block(Set(end, r.Back(1)), Set(r.Flag, h.Literal(false)))), capture, r.ReadWindow(3)],
            "equivalent-range-at-join" => [initial, capture, If(r.Flag, Set(r.Window, r.To(r.Back(1)))), r.ReadWindow(3)],
            "two-stored-ends" => [Give(start, r.Back(3)), initial, r.ReadSlice(h.Slice(start, end), 1)],
            "empty-at-end" => [Give(end, r.Back(0)), Give(r.Slice, r.SliceOf(h.Slice(end, end)))],
            "element" => [initial, Evaluate(h.At(r.Numbers, end))],
            "narrowed-offset" => [Give(offset, IdentityCall("Offset", r.Number(1))), If(r.InInterval(offset, 1, 2),
                Give(end, h.Back(offset)), Set(offset, r.Number(5)), r.ReadSlice(r.To(end), 2))],
            "string-element" => [initial, Evaluate(new IndexExpression(Node(), h.Literal("abc"), end, h.Built.Type("Char")))],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        r.Check(null, body);
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("past-end-element", "MIST0274")]
    [InlineData("before-start-element", "MIST0274")]
    [InlineData("before-start-endpoint", "MIST0274")]
    [InlineData("different-index-at-join", "MIST0275")]
    [InlineData("different-marker-at-join", "MIST0275")]
    [InlineData("write-after-unknown-index", "MIST0275")]
    [InlineData("lambda-write", "MIST0275")]
    [InlineData("ref-local", "MIST0275")]
    [InlineData("ref-argument", "MIST0275")]
    public void UnknownDifferentAndInvalidIndexes_RemainRefused(string scenario, string id)
    {
        var r = new RangeFlowBody();
        var h = r.Values;
        var end = Local(h.Index, "end");
        var alias = Read(new LocalVariable("alias", h.Index) { IsRef = true });
        var initial = Give(end, r.Back(1));
        var element = Evaluate(h.At(r.Numbers, end));
        var capture = Give(r.Window, r.To(end));
        var read = r.ReadWindow(0);
        Statement[] body = scenario switch
        {
            "past-end-element" => [Give(end, r.Back(0)), element],
            "before-start-element" => [Give(end, r.Back(6)), element],
            "before-start-endpoint" => [Give(end, r.Back(6)), r.ReadSlice(r.To(end), 0)],
            "different-index-at-join" => [initial, If(r.Flag, Set(end, r.Back(2))), capture, read],
            "different-marker-at-join" => [initial, capture, If(r.Flag, Set(r.Window, r.To(r.Number(4)))), read],
            "write-after-unknown-index" => [Give(end, CallReturning("Edge", h.Index, r.Back(1))), capture, Set(end, r.Back(1)), read],
            "lambda-write" => [initial, r.LambdaWrite(end, r.Back(5)), capture, read],
            "ref-local" => [initial, Give(alias, end), Give(r.Window, r.To(alias)), Set(alias, r.Back(5)), read],
            "ref-argument" => [initial, r.PassByRef(end, r.Back(5)), capture, read],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        r.Check(id, body);
    }

    [Theory]
    [Trait("Tier", "4")]
    [InlineData("Index end = 4;")]
    [InlineData("Index end = ^1; end = 4;")]
    public void AnIntegerAssignment_DoesNotInventAnImplicitIndexConversion(string statements)
    {
        var model = StandardModelProbe.Build("int[] numbers = [1, 2]; Index edge = ^1; Range window = 0..edge; " +
            "var part = numbers[window]; var text = \"abc\"[0..edge]; var last = part[0]; " + statements, out var buildDiagnostics);
        Assert.DoesNotContain(buildDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var body = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]).MethodsList.Single(method => method.Name == "Run").Body!;
        var indexes = BodyWalker.DescendantsAndSelf(body).OfType<IndexExpression>().ToArray();
        Assert.Equal(3, indexes.Length);
        Assert.All(indexes, index => Assert.IsNotType<ErrorType>(index.Type));
        var slices = indexes.Where(Lengths.IsRangeSlice).ToArray();
        Assert.Equal(2, slices.Length);
        Assert.All(slices, slice =>
        {
            Assert.Null(slice.ParameterType);
            Assert.Null(slice.Indexer);
        });
        var diagnostics = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, diagnostics);
        Assert.Equal("MIST0157", Assert.Single(diagnostics.ToImmutableArray()).Number.Value);
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("literal-getter")]
    [InlineData("declared-field-then-call")]
    [InlineData("copy-then-write-field")]
    [InlineData("declared-getter-at-join")]
    [InlineData("static-getter")]
    [InlineData("from-end-getter")]
    [InlineData("two-return-getter")]
    public void MemberOffsets_UseTheirDeclaredOrLiftedSignature(string scenario)
    {
        var r = new RangeFlowBody();
        var h = r.Values;
        var holder = Class("Holder");
        var held = Local(holder, "holder");
        var offset = h.Between(h.Int, 3L, 4L, "Offset");
        var field = new Field("end", offset, default, new MemberModifiers()) { Initializer = r.Number(3), DeclaringType = holder };
        var fieldAccess = new MemberAccessExpression(Node(), held, field.Name, field.Type, member: new FieldResult(field, holder, []));
        var memberType = scenario == "declared-getter-at-join" ? offset : h.Int;
        var staticGetter = scenario == "static-getter";
        var getterBody = scenario == "two-return-getter"
            ? Block(If(r.Flag, new ReturnStatement(r.Number(3), h.Int)), new ReturnStatement(r.Number(4), h.Int))
            : Block(new ReturnStatement(r.Number(3), memberType));
        var property = new Property("End", memberType, new MemberModifiers { IsStatic = staticGetter })
        { DeclaringType = holder, Getter = new PropertyAccessor { Body = getterBody } };
        Expression getter()
        {
            return new MemberAccessExpression(Node(), staticGetter ? new TypeReferenceExpression(Node(), holder) : held, property.Name, memberType,
            member: new PropertyResult(property, holder, []));
        }

        var original = Local(h.Range, "original");
        var end = Local(h.Index, "end");
        var change = new Method("Change", h.Built.Type("Void"), [], new MemberModifiers())
        { Body = Block(Set(new FieldReferenceExpression(Node(), field), r.Number(4))), DeclaringType = holder };
        Statement[] body = scenario switch
        {
            "declared-field-then-call" => [Give(r.Window, r.To(fieldAccess)),
                Evaluate(new InvocationExpression(Node(), new MemberAccessExpression(Node(), held, change.Name, change.ReturnType), [], change.ReturnType, change, []))],
            "copy-then-write-field" => [Give(original, r.To(fieldAccess)), Give(r.Window, original), Set(fieldAccess, r.Number(4))],
            "declared-getter-at-join" => [Give(r.Window, r.To(getter())), If(r.Flag, Set(r.Window, r.To(getter())))],
            "from-end-getter" => [Give(end, h.Back(getter())), Give(r.Window, h.Slice(end, r.Back(0)))],
            "literal-getter" or "static-getter" or "two-return-getter" => [Give(r.Window, r.To(getter()))],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        r.Check(null, [.. body, r.ReadWindow(2)]);
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("field-initializer")]
    [InlineData("getter-reads-field")]
    [InlineData("index-getter")]
    [InlineData("index-field")]
    [InlineData("indirect-receiver")]
    [InlineData("ref-receiver")]
    public void UnsupportedMembers_DoNotAcquireFactsFromTheirSourceOrReceiver(string scenario)
    {
        var r = new RangeFlowBody();
        var h = r.Values;
        var holder = Class("Holder");
        var held = Local(holder, "holder");
        var alias = Read(new LocalVariable("alias", holder) { IsRef = true });
        var index = scenario is "index-field" or "index-getter";
        var memberType = index ? h.Index : h.Int;
        var constant = index ? r.Back(1) : r.Number(3);
        var field = new Field("end", memberType, default, new MemberModifiers()) { DeclaringType = holder, Initializer = constant };
        var getter = new Property("End", memberType, new MemberModifiers())
        {
            DeclaringType = holder,
            Getter = new PropertyAccessor { Body = Block(new ReturnStatement(scenario == "getter-reads-field" ? new FieldReferenceExpression(Node(), field) : constant, memberType)) },
        };
        var child = new Property("Child", holder, new MemberModifiers())
        { Getter = new PropertyAccessor { Body = Block(new ReturnStatement(new ThisExpression(Node(), holder), holder)) }, DeclaringType = holder };
        Expression receiver = scenario switch
        {
            "indirect-receiver" => new MemberAccessExpression(Node(), held, child.Name, holder, member: new PropertyResult(child, holder, [])),
            "ref-receiver" => alias,
            _ => held,
        };
        var member = scenario is "index-field" or "field-initializer"
            ? new MemberAccessExpression(Node(), receiver, field.Name, memberType, member: new FieldResult(field, holder, []))
            : new MemberAccessExpression(Node(), receiver, getter.Name, memberType, member: new PropertyResult(getter, holder, []));
        var capture = Give(r.Window, r.To(member));
        if(scenario == "ref-receiver")
        {
            r.Check("MIST0275", Give(alias, held), capture, r.ReadWindow(0));
        }
        else
        {
            r.Check("MIST0275", capture, r.ReadWindow(0));
        }
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("write-after-this-capture", null)]
    [InlineData("explicit-write-and-equivalent-join", null)]
    [InlineData("call-after-capture", null)]
    [InlineData("field-ref-alias", "MIST0275")]
    [InlineData("this-field-ref-alias", "MIST0275")]
    [InlineData("aliased-this-field-ref-alias", "MIST0275")]
    [InlineData("call-before-capture", "MIST0275")]
    [InlineData("different-at-join", "MIST0275")]
    public void VisibleOwnedFieldAssignments_CaptureIndexesUntilAnInvalidatingCallOrDifferentJoin(string scenario, string? id)
    {
        var r = new RangeFlowBody();
        var h = r.Values;
        var holder = Class("Holder");
        var field = new Field("edge", h.Index, default, new MemberModifiers()) { DeclaringType = holder };
        var implicitField = new FieldReferenceExpression(Node(), field);
        var self = new ThisExpression(Node(), holder);
        var alias = Local(holder, "self");
        Expression access(Expression receiver)
        {
            return new MemberAccessExpression(Node(), receiver, field.Name, h.Index, member: new FieldResult(field, holder, []));
        }

        var explicitField = access(self);
        var change = new Method("Change", h.Built.Type("Void"), [], new MemberModifiers()) { DeclaringType = holder, Body = Block(Set(implicitField, r.Back(5))) };
        var call = Evaluate(new InvocationExpression(Node(), new MethodReferenceExpression(Node(), change), [], change.ReturnType, change, []));
        var initial = Set(implicitField, r.Back(1));
        var capture = Give(r.Window, r.To(implicitField));
        Statement[] body = scenario switch
        {
            "write-after-this-capture" => [initial, Give(r.Window, r.To(explicitField)), Set(implicitField, r.Back(4))],
            "explicit-write-and-equivalent-join" => [Set(explicitField, r.Back(1)), capture, If(r.Flag, Set(r.Window, r.To(r.Back(1))))],
            "call-after-capture" => [initial, capture, call],
            "field-ref-alias" => [initial, RefWrite(implicitField, r.Back(5)), capture],
            "this-field-ref-alias" => [Set(explicitField, r.Back(1)), RefWrite(explicitField, r.Back(5)), Give(r.Window, r.To(explicitField))],
            "aliased-this-field-ref-alias" => [initial, Give(alias, self), RefWrite(access(alias), r.Back(5)), capture],
            "call-before-capture" => [initial, call, capture],
            "different-at-join" => [initial, If(r.Flag, Set(implicitField, r.Back(2))), capture],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        r.Check(id, [.. body, r.ReadWindow(id is null ? 3 : 0)]);
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("length", null)]
    [InlineData("shorter-receiver", "MIST0274")]
    [InlineData("from-end-length", null)]
    public void BuiltinLength_CapturesTheMeasuredValueOnce(string scenario, string? id)
    {
        var r = new RangeFlowBody();
        var h = r.Values;
        var end = Local(h.Index, "end");
        var length = h.Length(r.Numbers);
        var capture = Give(r.Window, h.Slice(r.Number(1), length));
        Statement[] body = scenario switch
        {
            "length" => [capture, r.ReadWindow(3)],
            "shorter-receiver" => [capture, Set(r.Numbers, r.ArrayLiteral(10)), r.ReadWindow(0)],
            "from-end-length" => [Give(end, h.Back(length)), Give(r.Window, h.Slice(end, r.Back(0))), r.ReadWindow(4)],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        r.Check(id, body);
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("same-length-source", null)]
    [InlineData("different-length-sources", "MIST0275")]
    [InlineData("copied-before-write", "MIST0274")]
    public void NonExactLengthCaptures_KeepReceiverIdentityAndTheirCapturedValues(string scenario, string? id)
    {
        var r = new RangeFlowBody();
        var h = r.Values;
        var first = Local(h.Sequence(), "a");
        var second = Local(h.Sequence(), "b");
        var copy = Local(h.Range, "copy");
        var capture = Give(r.Window, r.To(h.Length(first)));
        var guard = r.InInterval(h.Length(first), 3, 5);
        Statement[] body = scenario switch
        {
            "same-length-source" => [If(guard, capture, If(r.Flag, Set(r.Window, r.To(h.Length(first)))), r.ReadSlice(r.Window, 2, first))],
            "different-length-sources" => [Give(second, CallReturning("Values", second.Type, r.ArrayLiteral(10, 20, 30))), If(h.And(guard, r.InInterval(h.Length(second), 3, 5)),
                capture, If(r.Flag, Set(r.Window, r.To(h.Length(second)))), r.ReadSlice(r.Window, 0, first))],
            "copied-before-write" => [If(guard, capture, Give(copy, r.Window), Set(first, r.ArrayLiteral(10)), r.ReadSlice(copy, 0, first))],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        r.Check(id, [Give(first, CallReturning("Values", first.Type, r.ArrayLiteral(10, 20, 30))), .. body]);
    }
}