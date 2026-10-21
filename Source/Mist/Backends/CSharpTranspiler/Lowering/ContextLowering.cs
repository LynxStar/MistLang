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
            EmitStore(writer);
            writer.WriteBlankLine();
            writer.WriteLine("public sealed class Optional<T>");
            using(writer.Block())
            {
                writer.WriteLine("private Optional()");
                using(writer.Block())
                {
                }
            }

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

    private static void EmitStore(CSharpWriter writer)
    {
        writer.WriteLine("private sealed class Frame(object value, Frame previous)");
        using(writer.Block())
        {
            writer.WriteLine("public object Value { get; } = value;");
            writer.WriteLine("public Frame Previous { get; } = previous;");
        }

        writer.WriteBlankLine();
        writer.WriteLine("private static readonly System.Threading.AsyncLocal<System.Collections.Generic.Dictionary<System.Type, Frame>> stacks = new();");
        writer.WriteBlankLine();
        writer.WriteLine("public static void Push<T>(object value) => Push(typeof(T), value);");
        writer.WriteBlankLine();
        writer.WriteLine("public static void Push(System.Type key, object value)");
        using(writer.Block())
        {
            writer.WriteLine("Frame previous = null;");
            writer.WriteLine("if (stacks.Value is { } current)");
            using(writer.Block())
            {
                writer.WriteLine("current.TryGetValue(key, out previous);");
            }

            writer.WriteBlankLine();
            writer.WriteLine("Set(key, new Frame(value, previous));");
        }

        writer.WriteBlankLine();
        writer.WriteLine("public static void Pop<T>() => Pop(typeof(T));");
        writer.WriteBlankLine();
        writer.WriteLine("public static void Pop(System.Type key) => Set(key, Top(key).Previous);");
        writer.WriteBlankLine();
        writer.WriteLine("public static object Peek<T>() => Peek(typeof(T));");
        writer.WriteBlankLine();
        writer.WriteLine("public static object Peek(System.Type key) => Top(key).Value;");
        writer.WriteBlankLine();
        writer.WriteLine("private static Frame Top(System.Type key)");
        using(writer.Block())
        {
            writer.WriteLine("var current = stacks.Value ?? throw new System.Collections.Generic.KeyNotFoundException();");
            writer.WriteLine("return current[key] ?? throw new System.InvalidOperationException(\"Stack empty.\");");
        }

        writer.WriteBlankLine();
        writer.WriteLine("private static void Set(System.Type key, Frame frame)");
        using(writer.Block())
        {
            // ExecutionContext inherits the map by reference, so published maps and frames must never change.
            writer.WriteLine("var next = stacks.Value is { } current");
            writer.WriteLine("    ? new System.Collections.Generic.Dictionary<System.Type, Frame>(current)");
            writer.WriteLine("    : new System.Collections.Generic.Dictionary<System.Type, Frame>();");
            writer.WriteLine("next[key] = frame;");
            writer.WriteLine("stacks.Value = next;");
        }
    }
}