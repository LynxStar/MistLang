using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// The runtime a GENERATED array needs: <c>new Array&lt;T, N&gt;(i =&gt; ...)</c>, which fills every
/// slot by calling a factory with the index.
/// <para>
/// C# HAS NO SYNTAX FOR IT. <c>new T[n]</c> zero-fills and a braced initializer lists elements one
/// by one; neither takes a function of the index, so the construction cannot be spelled as an
/// expression at all without something to call. A helper keeps it one expression, where hoisting a
/// temporary and a loop ahead of the statement would make every enclosing expression -- a ternary
/// arm, an argument -- a place that has to know how to hoist.
/// </para>
/// <para>
/// EMITTED ONLY WHEN A CONSTRUCTION USED ONE. Thread-static for the same reason
/// <see cref="ContextLowering"/> is, and read after the bodies are written, since only writing them
/// discovers it.
/// </para>
/// </summary>
internal static class ArrayLowering
{
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

    /// <summary>
    /// The element type and length of a generated array construction, or null when this is not one.
    /// </summary>
    public static (string Element, long Length)? Generated(IMistType type, int argumentCount)
    {
        if(argumentCount != 1
            || type is not ConstructedType constructed
            || constructed.Definition.FullyQualifiedName != WellKnownTypes.Array.FullyQualifiedName
            || constructed.TypeArguments is not [{ } element, LiftedType { Value: long length }])
        {
            return null;
        }

        used = true;
        return (TypeNameEmitter.Emit(element), length);
    }

    public static bool Generates(Constructor constructor)
    {
        return constructor.ParametersList.Count == 1
            && constructor.DeclaringType?.FullyQualifiedName == WellKnownTypes.Array.FullyQualifiedName;
    }

    public static void Emit(CSharpWriter writer)
    {
        writer.WriteLine("[System.Runtime.CompilerServices.CompilerGenerated]");
        writer.WriteLine("internal static class MistArray");

        using(writer.Block())
        {
            writer.WriteLine("public static T[] Generate<T>(int length, System.Func<int, T> factory)");

            using(writer.Block())
            {
                writer.WriteLine("var result = new T[length];");
                writer.WriteBlankLine();
                writer.WriteLine("for (var i = 0; i < length; i++)");
                using(writer.Block())
                {
                    writer.WriteLine("result[i] = factory(i);");
                }

                writer.WriteBlankLine();
                writer.WriteLine("return result;");
            }
        }
    }
}