using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// Where a value-routed slot's value reaches in the C# being written. A bare read names the hidden field or
/// the hidden parameter of the member it sits in, which every member of the declaring type, of the method
/// or block declaring the slot, and every local function and lambda inside them has. Four positions have
/// neither, and each enters a reach around what it writes so that <see cref="DependentCarrierNames.ValueRead"/>
/// finds the innermost: a C# operator or conversion takes no hidden parameter, so it reads the owner's
/// slot from the first operand whose type carries it, and has no value when none does; a type nested in a
/// value-routed type is no instance of it; an attribute argument is a constant; an abstract's default body
/// restated in an implementing type reads the abstract's slot as that type's closing supplies it. A read
/// no reach supplies is refused by name rather than written as a member C# cannot find.
/// </summary>
internal sealed class SlotReach : IDisposable
{
    [ThreadStatic]
    private static SlotReach? current;

    private readonly SlotReach? enclosing;
    private readonly Dictionary<TypeParameter, string?> sources;
    private readonly bool every;
    private readonly string site;

    private SlotReach(Dictionary<TypeParameter, string?> sources, bool every, string site)
    {
        enclosing = current;
        this.sources = sources;
        this.every = every;
        this.site = site;
        current = this;
    }

    public void Dispose()
    {
        current = enclosing;
    }

    public static string Read(TypeParameter slot)
    {
        for(var reach = current; reach is not null; reach = reach.enclosing)
        {
            if(!reach.sources.TryGetValue(slot, out var source) && !reach.every)
            {
                continue;
            }

            if(source is not null)
            {
                return source;
            }

            EmissionDiagnostics.Refuse(
                BackendDiagnostics.ValueRoutedSlotOutOfReach, new NameValue(slot.Name), new NameValue(Owner(slot)), new EnumValue(reach.site));
            return "default";
        }

        return SlotRoutes.Hidden(slot);
    }

    public static IDisposable? OfOperator(Method op)
    {
        var routed = SlotRoutes.ValueRouted(op.DeclaringType);

        if(routed.Count == 0)
        {
            return null;
        }

        var sources = new Dictionary<TypeParameter, string?>(ReferenceEqualityComparer.Instance);

        foreach(var slot in routed)
        {
            sources[slot] = BodyEmitter.OperandRead(op, slot);
        }

        var site = op switch
        {
            CastOperator cast => $"the conversion from '{TypeDisplay.Mist(cast.SourceParameter.Type)}', whose operand does not carry it",
            Operator user => $"'operator {user.Symbol}', whose operands do not carry it",
            _ => $"'{op.Name}', whose operands do not carry it",
        };

        return new SlotReach(sources, every: false, site);
    }

    public static IDisposable? OfNested(MemberType type)
    {
        var sources = new Dictionary<TypeParameter, string?>(ReferenceEqualityComparer.Instance);

        for(var outer = type.DeclaringType as MemberType; outer is not null; outer = outer.DeclaringType as MemberType)
        {
            foreach(var slot in SlotRoutes.ValueRouted(outer))
            {
                sources[slot] = null;
            }
        }

        return sources.Count == 0 ? null : new SlotReach(sources, every: false, $"the nested type '{TypeEmitter.DeclaredName(type)}'");
    }

    public static IDisposable OfAttributeArgument()
    {
        return new SlotReach(new Dictionary<TypeParameter, string?>(ReferenceEqualityComparer.Instance), every: true, "an attribute argument, which is a constant");
    }

    public static IDisposable? OfRestated(Method method, IReadOnlyList<ConstructedType> path, DiagnosticBag diagnostics)
    {
        var routed = SlotRoutes.ValueRouted(method.DeclaringType);

        if(routed.Count == 0)
        {
            return null;
        }

        var closing = TypeSubstitution.ForPath(receiver: null, path, interner: null);
        var sources = new Dictionary<TypeParameter, string?>(ReferenceEqualityComparer.Instance);

        foreach(var slot in routed)
        {
            sources[slot] = BodyEmitter.SlotValue(closing.Apply(slot, interner: null), diagnostics)
                ?? EmissionDiagnostics.Fallback("slot value", $"{Owner(slot)}.{slot.Name}", "default");
        }

        return new SlotReach(sources, every: false, $"the restated default '{method.Name}'");
    }

    private static string Owner(TypeParameter slot)
    {
        return MistType.StripArity(slot.DeclaringMethod?.Name ?? slot.DeclaringType?.Name ?? slot.Name);
    }
}
