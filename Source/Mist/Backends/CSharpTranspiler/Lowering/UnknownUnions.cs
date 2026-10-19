using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

internal static class UnknownUnions
{
    public static string? Widening(IMistType from, IMistType? to, string value)
    {
        return Widens(from, to) ? $"({value}).Payload" : null;
    }

    public static bool Widens(IMistType from, IMistType? to)
    {
        return to is not null && Unaliased(to) is UnknownType && Carrier(from) is not null;
    }

    public static string? Entering(Expression value, IMistType? slot, string text)
    {
        return Widening(value is ImplicitConversionExpression conversion ? conversion.TargetType : value.Type, slot, text);
    }

    public static UnionType? Carrier(IMistType type)
    {
        return Unaliased(type) is UnionType union && !OptionalUnion.IsOptional(union) ? union : null;
    }

    // An unknown holds the payload itself; narrowing rebuilds only its carrier and never clones that payload.
    public static string Test(TypeTestPattern pattern, string value, Func<Pattern, string, string> condition)
    {
        var union = Carrier(pattern.NarrowedType)!;
        var carrier = TypeNameEmitter.Emit(union);
        var parts = union.Constituents.Select((constituent, i) =>
        {
            var name = $"__part{i}";
            var binding = new LocalVariable(name, constituent);
            var tested = new TypeTestPattern(pattern.Node, constituent, binding,
                refinement: constituent is RefinedType refined ? refined.Predicate : null);
            var entry = UnionEntry.Factory(constituent, union) is { } factory
                ? $"{factory}({name})"
                : $"(({carrier})({name}))";
            return $"{condition(tested, "__payload")} ? {entry} : ";
        });
        var bindingText = pattern.Binding is { } bound ? $" {LocalNames.Of(bound)}" : "";
        var rebuilt = $"((System.Func<object, {carrier}?>)(__payload => __payload is {carrier} __carried ? __carried : "
            + string.Concat(parts) + $"default({carrier}?)))({value})";
        return $"{rebuilt} is {carrier}{bindingText}";
    }

    private static IMistType Unaliased(IMistType type)
    {
        return TypeNameEmitter.Closed(type) is TypeAlias { AliasedType: { } aliased } ? Unaliased(aliased) : TypeNameEmitter.Closed(type);
    }
}