using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// The receiver a field named by its bare Mist name is emitted through, so C# reaches the field the binder
/// chose instead of whatever its own simple-name lookup finds. A static field is named through the C# type
/// holding its storage, which is not always its declaring type: a struct carries copies of its ancestors'
/// fields and a newtype duplicates its source's, so naming the declaring type there would read other storage.
/// </summary>
internal static class FieldReceiver
{
    [ThreadStatic]
    private static Frame? holders;

    public static IDisposable Holding(ConcreteType source, string spelling, IEnumerable<Field> fields)
    {
        var outer = holders;
        holders = new Frame(source, spelling, fields.ToHashSet<Field>(ReferenceEqualityComparer.Instance), outer);
        return new Scope(outer);
    }

    public static string Of(Field field)
    {
        if(ReceiverSpelling.Carries || field.DeclaringType is not { } declaring)
        {
            return ReceiverSpelling.Qualifier(field.DeclaringType, field.Modifiers.IsStatic);
        }

        return (field.Modifiers.IsStatic ? Holder(declaring, field) : ReceiverSpelling.This) + ".";
    }

    private static string Holder(MemberType declaring, Field field)
    {
        for(var frame = holders; frame is not null; frame = frame.Outer)
        {
            if(frame.Fields.Contains(field))
            {
                return frame.Spelling;
            }

            if(frame.Source.BaseType is { } baseType && UnsafeReach.DerivesFrom(baseType, declaring))
            {
                return TypeNameEmitter.Emit(UnsafeReach.ClosedOwner(declaring, baseType));
            }
        }

        return TypeNameEmitter.Emit(declaring.TypeParametersList.Count == 0
            ? declaring
            : new ConstructedType(declaring, declaring.TypeParametersList));
    }

    private sealed record Frame(ConcreteType Source, string Spelling, HashSet<Field> Fields, Frame? Outer);

    private sealed class Scope(Frame? outer) : IDisposable
    {
        public void Dispose()
        {
            holders = outer;
        }
    }
}