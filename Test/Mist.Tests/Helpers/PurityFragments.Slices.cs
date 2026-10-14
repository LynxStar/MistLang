using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Helpers;

internal sealed partial class PurityFragments
{
    public IReadOnlyList<Diagnostic> Slices(string name)
    {
        return name switch
        {
            "pure-method-slices-through-an-impure-slice" => SliceRead(new PurityAnalyzer(), PureStatic, impureSlice: true, impureCount: false),
            "pure-method-slices-through-an-impure-count" => SliceRead(new PurityAnalyzer(), PureStatic, impureSlice: false, impureCount: true),
            "pure-method-slices-through-a-pure-slice" => SliceRead(new PurityAnalyzer(), PureStatic, impureSlice: false, impureCount: false),
            "pure-method-matches-a-slice-through-an-impure-slice" => SliceMatch(new PurityAnalyzer(), PureStatic),
            "readonly-method-slices-through-a-mutable-slice" => SliceRead(new ReadonlyAnalyzer(), ReadonlyStatic, impureSlice: true, impureCount: false),
            "readonly-method-slices-through-a-readonly-slice" => SliceRead(new ReadonlyAnalyzer(), ReadonlyStatic, impureSlice: false, impureCount: false),
            "readonly-method-matches-a-slice-through-a-mutable-slice" => SliceMatch(new ReadonlyAnalyzer(), ReadonlyStatic),
            "readonly-parameter-sliced-through-a-mutable-slice" => ReadonlyParameterSliced(),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    private (ClassType Strip, CountedSlice Read) Strip(bool impureSlice, bool impureCount)
    {
        var strip = HandBuiltModel.Class("Strip");
        _ = PureConstructor(strip);
        var count = new Property("Count", integer, Public)
        {
            DeclaringType = strip,
            Getter = impureCount
                ? new PropertyAccessor { Body = HandBuiltModel.Block(WriteStatic(ticks), new ReturnStatement(Literal(integer, 2L), integer)) }
                : new PropertyAccessor { IsAuto = true },
        };
        strip.PropertiesList.Add(count);
        var start = Input("start", integer);
        var length = Input("length", integer);
        var body = impureSlice
            ? HandBuiltModel.Block(WriteStatic(ticks), new ReturnStatement(Ref(length), integer))
            : Returning(Ref(length));
        var slice = Method(strip, "Slice", integer, impureSlice ? Public : Pure, body, start, length);
        return (strip, new CountedSlice(count, null, slice));
    }

    private IndexExpression Sliced(Expression receiver, CountedSlice read)
    {
        var range = new RangeExpression(Node(), Literal(integer, 0L), Literal(integer, 1L), model.Type("Range"));
        return new IndexExpression(Node(37), receiver, range, integer) { Slice = read };
    }

    private IReadOnlyList<Diagnostic> SliceRead(IProgramAnalyzer analyzer, MemberModifiers scope, bool impureSlice, bool impureCount)
    {
        var (strip, read) = Strip(impureSlice, impureCount);
        var s = new LocalVariable("s", strip);
        var cut = Method(probe, "Cut", integer, scope, HandBuiltModel.Block(
            new LocalDeclarationStatement(s, Fresh(strip)),
            new ReturnStatement(Sliced(Ref(s), read), integer)));
        return Judge(analyzer, cut);
    }

    private IReadOnlyList<Diagnostic> SliceMatch(IProgramAnalyzer analyzer, MemberModifiers scope)
    {
        var (strip, read) = Strip(impureSlice: true, impureCount: false);
        var s = new LocalVariable("s", strip);
        var middle = new ListPatternElement(new CatchAllPattern(Node(37), integer, new LocalVariable("middle", integer)), IsSlice: true, integer);
        var pattern = new ListPattern(Node(37), [middle], strip) { Slice = read };
        var matches = Method(probe, "Matches", boolean, scope, HandBuiltModel.Block(
            new LocalDeclarationStatement(s, Fresh(strip)),
            new ReturnStatement(new IsPatternExpression(Node(), Ref(s), pattern, boolean), boolean)));
        return Judge(analyzer, matches);
    }

    private IReadOnlyList<Diagnostic> ReadonlyParameterSliced()
    {
        var (strip, read) = Strip(impureSlice: true, impureCount: false);
        var s = new LocalVariable("s", strip) { Parameter = new Parameter("s", strip, new ParameterModifier(true, false, OwnershipKind.Unrestricted)) };
        var cut = Method(probe, "Cut", integer, Static, Returning(Sliced(Ref(s), read)), s);
        return Judge(new ReadonlyAnalyzer(), cut);
    }
}