using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

internal static partial class BodyEmitter
{
    /// <summary>
    /// The operand is emitted ONCE in every form, which is what makes a side-effecting one safe. C#'s two
    /// runtime-checked spellings each bind what they tested -- <c>as</c> answers the value or null, an
    /// <c>is</c> pattern binds it -- so neither wants the operand again; and a union needs no test at all,
    /// because the carrier's narrowing accessor already answers the constituent or absent, which is
    /// exactly what this expression's type says it produces.
    /// <para>
    /// Two spellings rather than one because C#'s <c>as</c> demands a reference or nullable target, so a
    /// value target goes through a pattern instead. <see cref="TypeNameEmitter.IsValueKind"/> decides,
    /// being the same authority that spells the optional this produces. A target whose spelling drops a
    /// length takes the pattern too, <c>as</c> being able to test only the spelling; see
    /// <see cref="ErasedShape"/>.
    /// </para>
    /// </summary>
    private static void EmitAs(
        AsExpression asExpression, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var owned = asExpression.Captures.Where(Evaluates).ToList();

        if(owned.Count > 0 && sequenced?.Contains(asExpression) != true && !CheckedAtRuntime(asExpression))
        {
            writer.Append(Evaluating(owned, asExpression, asExpression.Operand, () => Render(asExpression, diagnostics), diagnostics));
            return;
        }

        if(asExpression.Kind is AsConversionKind.Identity)
        {
            EmitExpression(asExpression.Operand, writer, diagnostics);
            return;
        }

        if(asExpression.Kind is AsConversionKind.Widening
            && UnknownUnions.Widens(asExpression.Operand.Type, asExpression.TargetType))
        {
            writer.Append(UnknownUnions.Widening(asExpression.Operand.Type, asExpression.TargetType, Render(asExpression.Operand, diagnostics))!);
            return;
        }

        var target = TypeNameEmitter.EmitUnannotated(asExpression.TargetType);

        if(asExpression.Kind is AsConversionKind.Widening && UnionEntry.Factory(asExpression.Operand.Type, asExpression.TargetType) is { } entry)
        {
            writer.Append($"{entry}({Render(asExpression.Operand, diagnostics)})");
            return;
        }

        if(asExpression.Kind is AsConversionKind.Widening)
        {
            writer.Append($"(({target})");
            EmitSubExpression(asExpression.Operand, PrefixPrecedence, writer, diagnostics);
            writer.Append(")");
            return;
        }

        var accessor = Accessor(asExpression);
        var checkedAtRuntime = CheckedAtRuntime(asExpression);

        if(checkedAtRuntime)
        {
            writer.Append("(");
        }

        EmitSubExpression(
            asExpression.Operand,
            accessor is null ? RelationalPrecedence : PrefixPrecedence,
            writer,
            diagnostics);

        if(accessor is not null && owned.Count == 0)
        {
            writer.Append(accessor);
        }

        if(!checkedAtRuntime)
        {
            return;
        }

        var tested = TestedType(asExpression.TargetType, asExpression.Node, diagnostics);
        var bound = $"__as{asExpression.Node.Span.Start}";
        var subject = owned.Count > 0 ? TemporaryName() : null;

        if(subject is not null)
        {
            writer.Append($" is var {subject}");
        }

        using var prelude = subject is null ? null : EmitPrelude(owned, " && ", writer, diagnostics);
        var compared = SlotSensitivity.AdmitsOnly(asExpression.Operand.Type, asExpression.TargetType)
            ? new List<string>()
            : RuntimeComparisons(asExpression.TargetType, bound, diagnostics);
        var narrowed = subject is null ? "" : $" && {subject}{accessor}";

        if(!TypeNameEmitter.IsValueKind(TypeNameEmitter.Closed(asExpression.TargetType))
            && ErasedShape.Members(asExpression.TargetType).Count == 0
            && compared.Count == 0)
        {
            writer.Append(subject is null
                ? $" as {target})"
                : $" ? {subject}{accessor} as {target} : default({TypeNameEmitter.EmitOptional(asExpression.TargetType)}))");
            return;
        }

        writer.Append($"{narrowed} is {tested} {bound}{string.Concat(compared.Select(test => $" && {test}"))} ? {bound} : default({TypeNameEmitter.EmitOptional(asExpression.TargetType)}))");
    }

    private static string? Accessor(AsExpression asExpression)
    {
        return asExpression.Constituent is { } constituent && TypeNameEmitter.Closed(asExpression.Operand.Type) is UnionType carrier
            ? $".{UnionCarrierNames.NarrowingAccessor(TypeNameEmitter.Closed(constituent), carrier)}"
            : null
            ;
    }

    private static bool CheckedAtRuntime(AsExpression asExpression)
    {
        if(asExpression.Kind is AsConversionKind.Identity or AsConversionKind.Widening)
        {
            return false;
        }

        return Accessor(asExpression) is null
            || (asExpression.Constituent is { } narrowedTo ? TypeNameEmitter.Closed(narrowedTo).FullyQualifiedName : null)
                != TypeNameEmitter.Closed(asExpression.TargetType).FullyQualifiedName
            ;
    }

    internal static void EmitCoerced(
        Expression expression, IMistType? target, CSharpWriter writer, DiagnosticBag diagnostics, IMistType? entered = null)
    {
        if(DelegateLowering.NeedsConstruction(expression.Type, target))
        {
            var source = Render(expression, diagnostics);
            var bare = expression is LambdaExpression { UnionCarrierDelegateType: null }
                || (expression is FunctionReferenceExpression reference && !RespelledGroups.Applies(reference));
            writer.Append($"new {TypeNameEmitter.Emit(target!)}({(bare ? source : $"({source}).Invoke")})");
            return;
        }

        EmitEntering(expression, entered ?? target, writer, diagnostics);
    }

    [ThreadStatic]
    private static (TupleLiteralExpression Literal, IReadOnlyList<IMistType> Slots)? enteringTuple;

    internal static void EmitEntering(
        Expression value, IMistType? slot, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(UnknownUnions.Entering(value, slot, "") is not null)
        {
            writer.Append(UnknownUnions.Entering(value, slot, Render(value, diagnostics))!);
            return;
        }

        if(UnionEntry.Unwrapped(value.Type, slot) is { } underlying)
        {
            var through = UnionEntry.Factory(underlying, slot);
            writer.Append($"{through}(({TypeNameEmitter.Emit(underlying)})");
            EmitSubExpression(value, PrefixPrecedence, writer, diagnostics);
            writer.Append(")");
            return;
        }

        if(UnionEntry.Factory(value.Type, slot) is { } factory)
        {
            writer.Append($"{factory}(");
            EmitExpression(value, writer, diagnostics);
            writer.Append(")");
            return;
        }

        if(value is not TupleLiteralExpression && UnionEntry.Decomposed(value.Type, slot, "") is not null)
        {
            var bound = TemporaryName();
            writer.Append("(");
            EmitSubExpression(value, RelationalPrecedence, writer, diagnostics);
            writer.Append($" is var {bound} ? {UnionEntry.Decomposed(value.Type, slot, bound)} : default)");
            return;
        }

        if(value is not TupleLiteralExpression tuple || UnionEntry.Elements(slot, tuple) is not { } elements)
        {
            EmitExpression(value, writer, diagnostics);
            return;
        }

        var enclosing = enteringTuple;
        enteringTuple = (tuple, elements);

        try
        {
            EmitExpression(value, writer, diagnostics);
        }
        finally
        {
            enteringTuple = enclosing;
        }
    }
}