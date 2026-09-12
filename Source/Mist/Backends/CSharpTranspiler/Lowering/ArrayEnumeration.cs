using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// An array's Enumerator, which the CLR does not have. Array.mist declares a nested STRUCT with a
/// Current of TValue; the CLR enumerates an array through an internal class reached only behind the
/// generic IEnumerator interface, and System.Array.GetEnumerator answers a non-generic one whose
/// Current is an object. Either would make the emitted code allocate and alias where Mist promised a
/// value copy. So the backend owns the type, as it owns MistArray and MistTasks: a generated struct
/// of exactly the declared shape, emitted only when a program reached the enumerator. Only the
/// concrete Array makes the struct promise -- through the IEnumerable abstract the slot promises
/// conformance alone, and that path binds to the abstract's own GetEnumerator and never comes here.
/// </summary>
internal static class ArrayEnumeration
{
    public const string StructName = "MistArrayEnumerator";

    [ThreadStatic]
    private static bool used;

    public static void Reset()
    {
        used = false;
    }

    public static bool Used()
    {
        return used;
    }

    public static bool IsEnumerator(IMistType? type)
    {
        return type is MistType { DeclaringType: { } outer }
            && outer.FullyQualifiedName == WellKnownTypes.Array.FullyQualifiedName
            && MistType.StripArity(type.Name) == "Enumerator";
    }

    public static bool Supplies(string member)
    {
        return member is "MoveNext" or "Current";
    }

    public static string? Emit(ConstructedType constructed, Func<IMistType, string> emit)
    {
        if(constructed.Enclosing is not { TypeArguments: [{ } element, ..] } || !IsEnumerator(constructed.Definition))
        {
            return null;
        }

        used = true;
        return $"{StructName}<{emit(element)}>";
    }

    public static bool Enumerates(Method callee)
    {
        return callee is { Name: "GetEnumerator", ParametersList.Count: 0 }
            && callee.DeclaringType?.FullyQualifiedName == WellKnownTypes.Array.FullyQualifiedName;
    }

    public static string Rewrite(string receiver, IMistType receiverType)
    {
        var element = receiverType is ConstructedType { TypeArguments: [{ } first, ..] }
            ? TypeNameEmitter.Emit(first)
            : "object";

        used = true;
        return $"new {StructName}<{element}>({receiver})";
    }

    public static void Emit(CSharpWriter writer)
    {
        writer.WriteLine("[System.Runtime.CompilerServices.CompilerGenerated]");
        writer.WriteLine($"internal struct {StructName}<T> : System.Collections.Generic.IEnumerator<T>");

        using(writer.Block())
        {
            writer.WriteLine("private readonly T[] array;");
            writer.WriteLine("private int index;");
            writer.WriteBlankLine();
            writer.WriteLine($"public {StructName}(T[] array)");
            using(writer.Block())
            {
                writer.WriteLine("this.array = array;");
                writer.WriteLine("index = -1;");
            }

            writer.WriteBlankLine();
            writer.WriteLine("public bool MoveNext()");
            using(writer.Block())
            {
                writer.WriteLine("index++;");
                writer.WriteLine("return index < array.Length;");
            }

            writer.WriteBlankLine();
            writer.WriteLine("public T Current");
            using(writer.Block())
            {
                writer.WriteLine("get { return array[index]; }");
            }

            writer.WriteBlankLine();
            writer.WriteLine("object System.Collections.IEnumerator.Current");
            using(writer.Block())
            {
                writer.WriteLine("get { return Current; }");
            }

            writer.WriteBlankLine();
            writer.WriteLine("public void Reset()");
            using(writer.Block())
            {
                writer.WriteLine("index = -1;");
            }

            writer.WriteBlankLine();
            writer.WriteLine("public void Dispose()");
            using(writer.Block())
            {
            }
        }
    }
}