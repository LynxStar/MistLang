using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// The members a conformer owes the CLR enumerable interfaces but never wrote. Mist's
/// <c>IEnumerable</c> and <c>IEnumerator</c> declare only what iteration needs -- an enumerator, a
/// move and a current -- while the CLR pair they project onto inherit from the non-generic
/// interfaces and <c>IDisposable</c>, so conforming to one obliges four more members.
/// <para>
/// Synthesized rather than declared in <c>Source/Standard</c>, because each one's body is a
/// forward to a member the CONFORMER declares, which the abstract cannot name. <c>Reset</c> is the
/// exception and throws: Mist models no restart, and the CLR documents refusing it as the allowed
/// answer for an enumerator that cannot.
/// </para>
/// </summary>
internal static class IteratorBoilerplateEmitter
{
    public static void Emit(ConcreteType type, IReadOnlyList<Method> declared, CSharpWriter writer, ref bool anyEmitted)
    {
        var emitted = new HashSet<string>(StringComparer.Ordinal);

        foreach(var (reached, definition, _) in AbstractInheritance.Reached(SlotLift.Reached(type), interner: null))
        {
            var element = reached is ConstructedType { TypeArguments.Count: > 0 } constructed
                ? TypeNameEmitter.Emit(constructed.TypeArguments[0])
                : "object";

            if(Is(definition, WellKnownTypes.IEnumerator) && emitted.Add("enumerator"))
            {
                EmitEnumerator(type, declared, writer, ref anyEmitted);
            }
            else if(Is(definition, WellKnownTypes.IEnumerable) && emitted.Add("enumerable"))
            {
                EmitEnumerable(element, writer, ref anyEmitted);
            }
        }
    }

    private static void EmitEnumerator(
        ConcreteType type, IReadOnlyList<Method> declared, CSharpWriter writer, ref bool anyEmitted)
    {
        TypeEmitter.SeparateMember(writer, ref anyEmitted);
        writer.WriteLine("object System.Collections.IEnumerator.Current => Current;");

        if(!Answered(type, declared, "Reset"))
        {
            TypeEmitter.SeparateMember(writer, ref anyEmitted);
            writer.WriteLine("public void Reset() { throw new System.NotSupportedException(); }");
        }

        if(!Answered(type, declared, "Dispose"))
        {
            TypeEmitter.SeparateMember(writer, ref anyEmitted);
            writer.WriteLine("public void Dispose() { }");
        }
    }

    /// <summary>
    /// <paramref name="declared"/> is every method the conformer emits, its restated defaults and a struct's
    /// flattened set included, so any of it collides with the adapter whatever its accessibility. A base
    /// class's member implements the CLR interface for its deriver only when public.
    /// </summary>
    private static bool Answered(ConcreteType type, IReadOnlyList<Method> declared, string name)
    {
        if(declared.Any(method => Parameterless(method, name)))
        {
            return true;
        }

        for(var ancestor = Base(type); ancestor is not null; ancestor = Base(ancestor))
        {
            if(ancestor.MethodsList.Exists(
                method => method.Modifiers.Accessibility == Accessibility.Public && Parameterless(method, name)))
            {
                return true;
            }
        }

        return false;
    }

    public static bool InheritsUndeclared(AbstractType definition, Method method)
    {
        return Is(definition, WellKnownTypes.IEnumerator) && (Parameterless(method, "Reset") || Parameterless(method, "Dispose"));
    }

    private static bool Parameterless(Method method, string name)
    {
        return method.Name == name
            && !method.Modifiers.IsStatic
            && method.ParametersList.Count == 0
            && method.TypeParametersList.Count == 0;
    }

    private static ConcreteType? Base(ConcreteType type)
    {
        return type.BaseType is { } baseType ? SlotLift.Definition(baseType) as ConcreteType : null;
    }

    private static void EmitEnumerable(string element, CSharpWriter writer, ref bool anyEmitted)
    {
        TypeEmitter.SeparateMember(writer, ref anyEmitted);
        writer.WriteLine(
            $"System.Collections.Generic.IEnumerator<{element}>"
            + $" System.Collections.Generic.IEnumerable<{element}>.GetEnumerator() => GetEnumerator();");

        TypeEmitter.SeparateMember(writer, ref anyEmitted);
        writer.WriteLine(
            "System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()"
            + " => GetEnumerator();");
    }

    private static bool Is(IMistType definition, WellKnownType wellKnown)
    {
        return definition.FullyQualifiedName == wellKnown.FullyQualifiedName;
    }
}