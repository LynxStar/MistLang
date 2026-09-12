using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// Task.WhenAll and Task.WhenAny, which the CLR spells with a different SHAPE rather than a different
/// name: it hosts them on a non-generic Task whose overloads answer `Task` and `Task&lt;Task&gt;`, where
/// Mist declares them on Task&lt;TResult&gt; answering Task&lt;TResult[]&gt; and Task&lt;TResult&gt;.
/// Emitting the call as written therefore hands back a type the declaration did not promise -- nothing
/// where a Void[] was promised -- so the backend supplies a wrapper of the declared shape, exactly as it
/// supplies MistArray. The declaration is the contract and this is the backend keeping it; the stdlib
/// has nothing to answer for here.
/// </summary>
internal static class TaskCombinators
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

    public static bool Combines(Method callee)
    {
        return callee is { Name: "WhenAll" or "WhenAny", ParametersList.Count: 1 }
            && IsTask(callee.DeclaringType);
    }

    public static string Rewrite(Method callee, IReadOnlyList<string> arguments)
    {
        used = true;
        return $"MistTasks.{callee.Name}({arguments[0]})";
    }

    /// <summary>
    /// The CLR call itself, for a result nothing reads: the wrapper exists only to supply the declared
    /// shape, and a value that is discarded never observes one.
    /// </summary>
    public static string Discarded(Method callee, string argument)
    {
        return $"System.Threading.Tasks.Task.{callee.Name}({argument})";
    }

    private static bool IsTask(MemberType? declaring)
    {
        return declaring is not null
            && (declaring.FullyQualifiedName == WellKnownTypes.TaskGeneric.FullyQualifiedName
                || declaring.FullyQualifiedName == WellKnownTypes.Task.FullyQualifiedName);
    }

    public static void Emit(CSharpWriter writer)
    {
        writer.WriteLine("[System.Runtime.CompilerServices.CompilerGenerated]");
        writer.WriteLine("internal static class MistTasks");

        using(writer.Block())
        {
            writer.WriteLine(
                "public static async System.Threading.Tasks.Task<Standard.Void[]> WhenAll(System.Threading.Tasks.Task[] tasks)");
            using(writer.Block())
            {
                writer.WriteLine("await System.Threading.Tasks.Task.WhenAll(tasks);");
                writer.WriteLine("return new Standard.Void[tasks.Length];");
            }

            writer.WriteBlankLine();
            writer.WriteLine(
                "public static System.Threading.Tasks.Task<T[]> WhenAll<T>(System.Threading.Tasks.Task<T>[] tasks)");
            using(writer.Block())
            {
                writer.WriteLine("return System.Threading.Tasks.Task.WhenAll(tasks);");
            }

            writer.WriteBlankLine();
            writer.WriteLine(
                "public static System.Threading.Tasks.Task WhenAny(System.Threading.Tasks.Task[] tasks)");
            using(writer.Block())
            {
                writer.WriteLine(
                    "return System.Threading.Tasks.TaskExtensions.Unwrap(System.Threading.Tasks.Task.WhenAny(tasks));");
            }

            writer.WriteBlankLine();
            writer.WriteLine(
                "public static System.Threading.Tasks.Task<T> WhenAny<T>(System.Threading.Tasks.Task<T>[] tasks)");
            using(writer.Block())
            {
                writer.WriteLine(
                    "return System.Threading.Tasks.TaskExtensions.Unwrap(System.Threading.Tasks.Task.WhenAny(tasks));");
            }
        }
    }
}