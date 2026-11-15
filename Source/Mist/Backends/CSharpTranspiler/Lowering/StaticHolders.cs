using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// Static storage of a value-routed type, one storage for each value of its slots (runtime-dependent-values design,
/// <em>Identity by value: static storage</em>). The type's static fields and static properties are instance members of a
/// nested holder, whose constructor takes the slot values and runs the static initializers and the static
/// constructor's body in the order the type's C# statics run them on the carrier route: the fields, then the
/// properties, then the body. The type keeps one table from the values to the holder, and a read or a write finds
/// the holder there. The table enters a holder before its constructor runs anything, so an initializer that reaches
/// the storage of its own value, directly or through a call, finds the holder being built, as a C# static
/// initializer reads its own type's statics while they are set; one lock across every table serializes the
/// building, so a holder is built exactly once for a value when two threads ask together, and two types whose
/// initializers reach each other cannot deadlock. A holder that failed to build is never built again, and
/// every later use fails as a failed C# static initializer does.
/// </summary>
internal static class StaticHolders
{
    public const string Holder = "__Statics";

    public const string Lookup = "__StaticsOf";

    public const string Table = "__statics";

    [ThreadStatic]
    private static bool used;

    [ThreadStatic]
    private static MemberType? building;

    public static void Reset()
    {
        used = false;
    }

    public static bool Used()
    {
        return used;
    }

    public static bool IsHeld(Field field, MemberType? owner)
    {
        return field.Modifiers is { IsStatic: true, IsIntrinsic: false } && Keeps(owner);
    }

    public static bool IsHeld(Property property, MemberType? owner)
    {
        return property is { Modifiers: { IsStatic: true, IsIntrinsic: false }, ProjectedParameter: null } && Keeps(owner);
    }

    public static bool IsHeld(Constructor constructor)
    {
        return constructor.Modifiers.IsStatic && Keeps(constructor.DeclaringType);
    }

    public static bool Keeps(MemberType? type)
    {
        return type is ConcreteType && SlotRoutes.Carried(type).Count > 0;
    }

    public static bool HasStaticConstructor(MemberType? type)
    {
        return type is ConcreteType concrete && Keeps(concrete) && concrete.ConstructorsList.Exists(constructor => constructor.Modifiers.IsStatic);
    }

    public static string Runtime()
    {
        used = true;
        return "MistStatics";
    }

    public static string Through(string type, IEnumerable<string> values)
    {
        return $"{type}.{Lookup}({string.Join(", ", values)})";
    }

    public static IDisposable Building(MemberType type)
    {
        var previous = building;
        building = type;
        return new Scope(previous);
    }

    public static bool IsBuilding(MemberType type)
    {
        return ReferenceEquals(building, type);
    }

    public static string KeyType(IReadOnlyList<TypeParameter> slots)
    {
        return slots.Count == 1 ? SlotRoutes.Keyword(slots[0]) : $"({string.Join(", ", slots.Select(SlotRoutes.Keyword))})";
    }

    public static string Key(IReadOnlyList<TypeParameter> slots)
    {
        return slots.Count == 1 ? SlotRoutes.Hidden(slots[0]) : $"({string.Join(", ", slots.Select(SlotRoutes.Hidden))})";
    }

    public static string Unpacked(IReadOnlyList<TypeParameter> slots, string key)
    {
        return slots.Count == 1 ? key : string.Join(", ", slots.Select((_, i) => $"{key}.Item{i + 1}"));
    }

    public static Accessibility Reach(Accessibility declared)
    {
        return declared is Accessibility.Public or Accessibility.Protected ? Accessibility.Public : Accessibility.Internal;
    }

    public static void Emit(CSharpWriter writer)
    {
        writer.WriteLine("[System.Runtime.CompilerServices.CompilerGenerated]");
        writer.WriteLine("internal static class MistStatics");

        using(writer.Block())
        {
            writer.WriteLine("private static readonly object gate = new object();");
            writer.WriteBlankLine();
            writer.WriteLine("internal sealed class Table<TKey, THolder> where TKey : notnull where THolder : class");

            using(writer.Block())
            {
                writer.WriteLine("private readonly System.Collections.Concurrent.ConcurrentDictionary<TKey, THolder> made = new System.Collections.Concurrent.ConcurrentDictionary<TKey, THolder>();");
                writer.WriteLine("private readonly System.Collections.Generic.Dictionary<TKey, THolder> making = new System.Collections.Generic.Dictionary<TKey, THolder>();");
                writer.WriteLine("private readonly System.Collections.Generic.Dictionary<TKey, System.Exception> failed = new System.Collections.Generic.Dictionary<TKey, System.Exception>();");
                writer.WriteLine("private readonly System.Func<TKey, THolder> create;");
                writer.WriteBlankLine();
                writer.WriteLine("public Table(System.Func<TKey, THolder> create)");

                using(writer.Block())
                {
                    writer.WriteLine("this.create = create;");
                }

                writer.WriteBlankLine();
                writer.WriteLine("public THolder Of(TKey key)");

                using(writer.Block())
                {
                    writer.WriteLine("if (made.TryGetValue(key, out var held))");

                    using(writer.Block())
                    {
                        writer.WriteLine("return held;");
                    }

                    writer.WriteBlankLine();
                    writer.WriteLine("lock (gate)");

                    using(writer.Block())
                    {
                        writer.WriteLine("if (made.TryGetValue(key, out held) || making.TryGetValue(key, out held))");

                        using(writer.Block())
                        {
                            writer.WriteLine("return held;");
                        }

                        writer.WriteBlankLine();
                        writer.WriteLine("if (failed.TryGetValue(key, out var cause))");

                        using(writer.Block())
                        {
                            writer.WriteLine("throw new System.TypeInitializationException(typeof(THolder).DeclaringType?.FullName, cause);");
                        }

                        writer.WriteBlankLine();
                        writer.WriteLine("try");

                        using(writer.Block())
                        {
                            writer.WriteLine("held = create(key);");
                        }

                        writer.WriteLine("catch (System.Exception thrown)");

                        using(writer.Block())
                        {
                            writer.WriteLine("failed[key] = thrown;");
                            writer.WriteLine("throw new System.TypeInitializationException(typeof(THolder).DeclaringType?.FullName, thrown);");
                        }

                        writer.WriteLine("finally");

                        using(writer.Block())
                        {
                            writer.WriteLine("making.Remove(key);");
                        }

                        writer.WriteBlankLine();
                        writer.WriteLine("made[key] = held;");
                        writer.WriteLine("return held;");
                    }
                }

                writer.WriteBlankLine();
                writer.WriteLine("public void Enter(TKey key, THolder holder)");

                using(writer.Block())
                {
                    writer.WriteLine("making[key] = holder;");
                }
            }
        }
    }

    private sealed class Scope(MemberType? previous) : IDisposable
    {
        public void Dispose()
        {
            building = previous;
        }
    }
}