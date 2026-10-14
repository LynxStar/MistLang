using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;

namespace Mist.Tests.Helpers;

internal sealed partial class StructuredContextFragments
{
    private IReadOnlyList<Diagnostic> Slices(string name)
    {
        var holder = HandBuiltModel.Class("Holder");
        types.Add(holder);
        var count = new Property("Count", integer, Public) { DeclaringType = holder, Getter = new PropertyAccessor { Body = HandBuiltModel.Block() } };
        holder.PropertiesList.Add(count);
        bodies.Add(new SourceRoot(count.Getter.Body!, holder, MistAssembly.Synthetic, count));
        var slice = new Method("Slice", text, [new Parameter("start", integer), new Parameter("length", integer)], Public)
        {
            DeclaringType = holder,
            Body = Owing(true),
            DeclarationNode = HandBuiltModel.Node(Site),
        };
        holder.MethodsList.Add(slice);
        bodies.Add(new SourceRoot(slice.Body!, holder, MistAssembly.Synthetic, slice));
        var reads = new CountedSlice(count, null, slice);

        IndexExpression sliced(int offset)
        {
            var range = new RangeExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(integer), HandBuiltModel.Literal(integer), model.Type("Range"));
            return new IndexExpression(HandBuiltModel.Node(offset), HandBuiltModel.Literal(holder), range, text) { Slice = reads };
        }

        IsPatternExpression listed(int offset)
        {
            var middle = new ListPatternElement(new CatchAllPattern(HandBuiltModel.Node(), text, null), IsSlice: true, ScrutineeType: text);
            var pattern = new ListPattern(HandBuiltModel.Node(offset), [middle], holder) { Slice = reads };
            return new IsPatternExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(holder), pattern, integer);
        }

        Entry(name switch
        {
            "slice-read-under-a-context" => [Under(text, Run(sliced(0)))],
            "slice-read-bare" => [Run(sliced(Outer))],
            "slice-read-by-a-list-pattern-under-a-context" => [Under(text, Run(listed(0)))],
            "slice-read-by-a-list-pattern-bare" => [Run(listed(Outer))],
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        });

        return Judge();
    }
}