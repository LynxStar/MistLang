using Mist.Backends.CSharpTranspiler.Emitter;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// The runtime a <c>with</c> block needs: a stack of ambient values keyed by type.
/// <para>
/// THE BACKEND'S OWN, not a stand-in for a Standard declaration. Mist declares no context type --
/// <c>with(x) { }</c> is syntax, and where the value lives while the block runs is the target's
/// answer. C# has no ambient-value stack, so one is emitted; another backend may have somewhere to
/// put it already.
/// </para>
/// <para>
/// EMITTED ONLY WHEN A BLOCK USED ONE, recorded as the bodies are written. Thread-static so
/// parallel emissions cannot see each other's, and read after every unit is emitted rather than
/// before, since only writing the bodies discovers it.
/// </para>
/// </summary>
internal static class ContextLowering
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

    public static void Require()
    {
        used = true;
    }

    public static void Emit(CSharpWriter writer)
    {
        writer.WriteLine("[System.Runtime.CompilerServices.CompilerGenerated]");
        writer.WriteLine("internal static class MistContext");

        using(writer.Block())
        {
            writer.WriteLine(
                "private static readonly System.Collections.Generic.Dictionary<System.Type, "
                + "System.Collections.Generic.Stack<object>> stacks = new();");
            writer.WriteBlankLine();
            writer.WriteLine("public static void Push<T>(object value)");
            using(writer.Block())
            {
                writer.WriteLine("if (!stacks.TryGetValue(typeof(T), out var stack))");
                using(writer.Block())
                {
                    writer.WriteLine("stack = new System.Collections.Generic.Stack<object>();");
                    writer.WriteLine("stacks[typeof(T)] = stack;");
                }

                writer.WriteBlankLine();
                writer.WriteLine("stack.Push(value);");
            }

            writer.WriteBlankLine();
            writer.WriteLine("public static void Pop<T>() => stacks[typeof(T)].Pop();");
            writer.WriteBlankLine();
            writer.WriteLine("public static object Peek<T>() => stacks[typeof(T)].Peek();");
            writer.WriteBlankLine();
            writer.WriteLine("public sealed class Pure<T>");
            using(writer.Block())
            {
                writer.WriteLine("private Pure()");
                using(writer.Block())
                {
                }
            }
        }
    }
}