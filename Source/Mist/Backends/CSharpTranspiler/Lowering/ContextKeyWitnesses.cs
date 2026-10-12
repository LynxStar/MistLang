using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// The lowering of a <c>with</c> clause whose key names a type parameter. A context is found by EXACT
/// key, and the CLR erases what makes two Mist types distinct keys -- <c>string?</c> and <c>string</c>
/// are one <c>System.String</c> -- so <c>Peek&lt;T&gt;()</c> inside a generic body cannot recover the
/// key the caller resolved the clause to. Each such clause gains a <c>System.Type</c> PARAMETER the
/// call site fills with that key, placed with the default and constructor witnesses and for the same
/// reason they come first: a declared <c>params</c> array has to stay last. A closed key keeps the
/// generic spelling. Inside a body an open key is spelled by the parameter of the enclosing clause it
/// is the same key as -- which a nested call forwards and a <c>with</c> block files under -- and by
/// <c>typeof</c> the erased spelling where no clause holds it, the one case analysis admits being a
/// <c>with</c> block whose consumers sit in the same body. A member the target runs with no call
/// written -- an operator, a Drop, an enumerator, a local function C# lets capture -- carries no
/// parameter, and an answer whose channel differs from its slot's is bridged by
/// <see cref="ConstraintSlotBridgeEmitter"/>.
/// </summary>
internal static class ContextKeyWitnesses
{
    [ThreadStatic]
    private static Frame? scope;

    public static bool Carries(Method method)
    {
        var declared = Declared(method);

        return declared is not (Operator or CastOperator)
            && (declared.DeclaringType is not null || declared.DeclaringBlock is not null)
            && !(declared.ParametersList.Count == 0
                && declared.Name is "Drop" or "Dispose" or "GetEnumerator" or "MoveNext" or "Clone");
    }

    public static IReadOnlyList<int> Ordinals(Method method)
    {
        if(!Carries(method))
        {
            return [];
        }

        var bindings = Declared(method).ContextBindingsList;
        var ordinals = new List<int>();

        for(var ordinal = 0; ordinal < bindings.Count; ordinal++)
        {
            if(ContextKeys.IsOpen(bindings[ordinal].ContextType))
            {
                ordinals.Add(ordinal);
            }
        }

        return ordinals;
    }

    public static IReadOnlyList<IMistType> Keys(Method method)
    {
        var bindings = Declared(method).ContextBindingsList;

        return Ordinals(method).Select(ordinal => bindings[ordinal].ContextType).ToList();
    }

    public static string Name(int ordinal)
    {
        return $"__context_{ordinal}";
    }

    public static List<string> Parameters(Method method)
    {
        return Ordinals(method).Select(ordinal => $"System.Type {Name(ordinal)}").ToList();
    }

    public static List<string> Names(Method method)
    {
        return Ordinals(method).Select(Name).ToList();
    }

    public static List<string> Arguments(Method method, IReadOnlyList<IMistType>? keys)
    {
        var bindings = Declared(method).ContextBindingsList;

        return Ordinals(method)
            .Select(ordinal => Spell(keys is not null && ordinal < keys.Count ? keys[ordinal] : bindings[ordinal].ContextType))
            .ToList();
    }

    public static List<string> Erased(Method method)
    {
        var bindings = Declared(method).ContextBindingsList;

        return Ordinals(method).Select(ordinal => Erase(bindings[ordinal].ContextType)).ToList();
    }

    public static string Spell(IMistType key)
    {
        return Held(key) ?? Erase(key);
    }

    public static string? Held(IMistType key)
    {
        var closed = TypeNameEmitter.Closed(key);

        for(var frame = scope; frame is not null; frame = frame.Outer)
        {
            foreach(var (held, name) in frame.Entries)
            {
                if(ContextKeys.Same(TypeNameEmitter.Closed(held), closed))
                {
                    return name;
                }
            }
        }

        return null;
    }

    public static IDisposable Entering(Method method)
    {
        var bindings = Declared(method).ContextBindingsList;
        var entries = Ordinals(method).Select(ordinal => (bindings[ordinal].ContextType, Name(ordinal))).ToList();
        var outer = scope;
        scope = new Frame(entries, outer);
        return new Scope(outer);
    }

    private static string Erase(IMistType key)
    {
        return $"typeof({TypeNameEmitter.EmitContextKey(key)})";
    }

    private static Method Declared(Method method)
    {
        return method.SourceDeclaration ?? method;
    }

    private sealed record Frame(IReadOnlyList<(IMistType Key, string Name)> Entries, Frame? Outer);

    private sealed class Scope(Frame? outer) : IDisposable
    {
        public void Dispose()
        {
            scope = outer;
        }
    }
}