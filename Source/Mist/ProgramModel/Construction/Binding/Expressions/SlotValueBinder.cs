using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

/// <summary>
/// Reads the names written in the dependent slots of one type as values, and keeps the captures it
/// made for the node that wrote the type to own.
/// </summary>
internal sealed class SlotValueBinder(BindingContext ctx, BuilderContext context) : ISlotValueBinder
{
    private readonly List<SlotCapture> captures = [];

    public IReadOnlyList<SlotCapture> Captures => captures;

    public IMistType? Bind(QualifiedIdentifierNode name, TypeParameter slot)
    {
        var value = IdentifierBinder.BindName(name, name.IDENTIFIER.Lexeme, ctx, context);

        foreach(var segment in name.IDENTIFIERs)
        {
            value = PostfixBinder.BindMemberAccess(name, value, segment.Lexeme, ctx, context);
        }

        if(Capture(value, slot, QualifiedNameHelper.Build(name), context) is not { } capture)
        {
            return ErrorType.Instance;
        }

        captures.Add(capture);
        return capture.Value;
    }

    public static SlotCapture? CaptureLength(Expression size, ExpressionNode written, WellKnownType definition, BuilderContext context)
    {
        return context.Resolver.LookupWellKnown(definition) is IHasTypeParameters { TypeParametersList: [_, { IsDependent: true } length] }
            ? Capture(size, length, Written(written), context, converted: true)
            : null;
    }

    private static SlotCapture? Capture(Expression value, TypeParameter slot, string writtenName, BuilderContext context, bool converted = false)
    {
        if(value.Type is ErrorType || PriorFailure.Of(value) || slot.Domain is not { } domain)
        {
            return null;
        }

        var contract = slot is { RefinementNode: { } predicateNode, Refinement: { } predicate }
            ? new RefinedType(domain, predicateNode) { Predicate = predicate }
            : (IMistType)domain;

        return new SlotCapture(
            writtenName, contract, ConversionRelation.Unrefined(domain),
            converted ? value : ImplicitConversions.At(value, contract, context.ProgramModel.TypeInterner), value.Node.Span.Start);
    }

    private static string Written(ExpressionNode node)
    {
        return node.Origin is ParsedOrigin { Source.Contents: var contents } && node.Span.End <= contents.Length
            ? contents[node.Span.Start..node.Span.End].Trim()
            : "length";
    }
}