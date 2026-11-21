using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Contracts.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// Bare storage holds its element type's declared default, which C# zero-fills. A closed element is
/// filled with its default at the allocation; an open one reads <c>MistDefault</c>, a static holder per
/// closed type, rather than a witness, because a stored witness would make a stampable struct managed and
/// unfit for <c>stackalloc</c>. The holder answers from registrations of the closed instantiations the
/// whole program reaches (<see cref="StampInstantiations"/>), made where <see cref="StampVisibility"/>
/// can name them, and from the CLR zero for the rest.
/// </summary>
internal static class StorageStamping
{
    [ThreadStatic]
    private static ProgramModelClass? program;

    [ThreadStatic]
    private static List<IMistType>? open;

    [ThreadStatic]
    private static bool used;

    [ThreadStatic]
    private static IReadOnlyList<IMistType>? hidden;

    public static IDisposable Enter(ProgramModelClass model)
    {
        program = model;
        open = null;
        used = false;
        hidden = null;
        return new Scope();
    }

    public static bool Used()
    {
        return used;
    }

    public static (string Helper, string? Value)? Fill(IMistType elementType, bool onStack)
    {
        var closed = TypeNameEmitter.Closed(elementType);
        var helper = onStack ? "MistStamp.Span" : "MistStamp.Array";

        if(DeclaredZero.Holds(closed))
        {
            return null;
        }

        if(TypeParameterReach.Collect(closed).Count > 0)
        {
            (open ??= []).Add(closed);
            used = true;
            return (helper, null);
        }

        if(DefaultSynthesis.Expression(closed) is not { } value)
        {
            return null;
        }

        used = true;
        return (helper, value);
    }

    public static IEnumerable<string> Registrations(object body, IMistType? enclosing)
    {
        if(program is null || (hidden ??= Stamped(StampInstantiations.Candidates(program), nameable: false)).Count == 0)
        {
            return [];
        }

        var mentioned = StampVisibility.Mentioned(body);

        return hidden
            .Where(type => StampVisibility.Hidden(type) is var parts && parts.Any(mentioned.Contains) && parts.All(part => StampVisibility.Reaches(enclosing, part)))
            .Select(type => $"MistDefault<{TypeNameEmitter.Emit(type)}>.Ensure(() => {DefaultSynthesis.Expression(type)});");
    }

    public static void Emit(CSharpWriter writer)
    {
        writer.WriteLine("[System.Runtime.CompilerServices.CompilerGenerated]");
        writer.WriteLine("internal static class MistDefault<T>");
        using(writer.Block())
        {
            writer.WriteLine("private static System.Func<T> factory;");
            writer.WriteLine("private static T value;");
            writer.WriteLine("private static bool resolved;");
            writer.WriteBlankLine();
            writer.WriteLine("public static T Value");
            using(writer.Block())
            {
                writer.WriteLine("get");
                using(writer.Block())
                {
                    writer.WriteLine("if(!resolved)");
                    using(writer.Block())
                    {
                        writer.WriteLine("value = factory is { } create ? create() : MistStamp.Default(typeof(T)) is T stamped ? stamped : default(T);");
                        writer.WriteLine("resolved = true;");
                    }

                    writer.WriteBlankLine();
                    writer.WriteLine("return value;");
                }
            }

            writer.WriteBlankLine();
            writer.WriteLine("public static void Ensure(System.Func<T> create)");
            using(writer.Block())
            {
                writer.WriteLine("if(factory is null)");
                using(writer.Block())
                {
                    writer.WriteLine("factory = create;");
                    writer.WriteLine("resolved = false;");
                }
            }
        }

        writer.WriteBlankLine();
        writer.WriteLine("[System.Runtime.CompilerServices.CompilerGenerated]");
        writer.WriteLine("internal static class MistStamp");
        using(writer.Block())
        {
            EmitFill(writer, "T[]", "System.Array.Fill(storage, value);");
            EmitFill(writer, "System.Span<T>", "storage.Fill(value);");
            EmitRegistrar(writer);
        }
    }

    private static void EmitFill(CSharpWriter writer, string storage, string fill)
    {
        var name = storage == "T[]" ? "Array" : "Span";

        writer.WriteLine($"public static {storage} {name}<T>({storage} storage)");
        using(writer.Block())
        {
            writer.WriteLine($"return {name}(storage, MistDefault<T>.Value);");
        }

        writer.WriteBlankLine();
        writer.WriteLine($"public static {storage} {name}<T>({storage} storage, T value)");
        using(writer.Block())
        {
            writer.WriteLine(fill);
            writer.WriteLine("return storage;");
        }

        writer.WriteBlankLine();
    }

    private static void EmitRegistrar(CSharpWriter writer)
    {
        writer.WriteLine("public static object Default(System.Type type)");
        using(writer.Block())
        {
            var registered = program is null ? [] : Stamped(StampInstantiations.Close(program, open ?? []), nameable: true);

            foreach(var type in registered)
            {
                writer.WriteLine($"if(type == typeof({TypeNameEmitter.Emit(type)}))");
                using(writer.Block())
                {
                    writer.WriteLine($"return {DefaultSynthesis.Expression(type)};");
                }

                writer.WriteBlankLine();
            }

            writer.WriteLine("return null;");
        }
    }

    private static List<IMistType> Stamped(IReadOnlyList<IMistType> instances, bool nameable)
    {
        return [.. instances.Where(type => StampVisibility.Nameable(type) == nameable
            && !DeclaredZero.Holds(type) && DefaultSynthesis.Expression(type) is not null)];
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose()
        {
            program = null;
            open = null;
            hidden = null;
        }
    }
}