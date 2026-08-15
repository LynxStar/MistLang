using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// What a type's <c>default</c> is, lowered to a parameterless constructor plus a static factory so
/// that <c>default(T)</c> becomes an ordinary call. Mist's default of a class is an INSTANCE with every
/// field defaulted, not a null reference, so there is nothing C# can be asked for here.
/// <para>
/// A type is defaultable only if it declares no constructor or declares a parameterless one. Requiring
/// arguments is the author saying the type has no sensible zero, and for a derived type it is a hard
/// limit besides: a synthesized parameterless constructor would need a base call that does not exist.
/// Emission is GATED on this rather than a diagnostic being raised for the rest, because refusing a
/// program is a judgement on a finished model and nothing in this pipeline makes one yet.
/// </para>
/// </summary>
internal static class DefaultSynthesis
{
    public const string FactoryName = "__CreateDefault";

    /// <summary>
    /// The marker discriminating the sentinel constructor, needed only where the type already declares
    /// a parameterless one so construction and defaulting have to diverge. Distinct from
    /// <see cref="StructAncestorFactory.MarkerType"/> rather than shared with it: on a struct with no
    /// fields and no properties that factory's constructor takes its marker ALONE, so one marker serving
    /// both would put two identical signatures on the same type.
    /// </summary>
    public const string MarkerType = $"{ConstructorConstraintInterfaces.Namespace}.__DefaultInit";

    public const string MarkerTypeName = "__DefaultInit";

    public static bool IsDefaultable(MemberType type)
    {
        return type is ConcreteType { IsIntrinsic: false } concrete
            && HasParameterlessSlot(concrete)
            && BaseIsDefaultable(concrete)
            && InstanceFields(concrete).All(field => CanDefault(concrete, field))
            ;
    }

    /// <summary>
    /// Whether defaulting has to route through the sentinel, which is exactly when the type spends its
    /// parameterless slot on a constructor of its own.
    /// </summary>
    public static bool NeedsSentinel(ConcreteType type)
    {
        return type.ConstructorsList.Exists(c => c.ParametersList.Count == 0 && !c.Modifiers.IsStatic);
    }

    /// <summary>
    /// Each field paired with the expression its default assigns, in declaration order, or null where
    /// the answer is the field type's own literal default. A default block entry REPLACES the field's
    /// own answer rather than adding to it, which is what makes a block on a type that also has
    /// initializers mean the block's values and not both.
    /// </summary>
    public static List<(Field Field, Expression? Value)> Assignments(ConcreteType type)
    {
        var assignments = new List<(Field, Expression?)>();

        foreach(var field in InstanceFields(type))
        {
            assignments.Add((field, BlockEntry(type, field)?.Value ?? field.Initializer));
        }

        return assignments;
    }

    /// <summary>
    /// A field with no answer is what makes the whole type non-defaultable, since a constructor that
    /// skipped it would leave it at C#'s zero rather than Mist's -- null for a string where Mist means
    /// the empty one.
    /// </summary>
    private static bool CanDefault(ConcreteType type, Field field)
    {
        return BlockEntry(type, field) is not null
            || field.Initializer is not null
            || BodyEmitter.EmitTypeDefault(field.Type) is not null
            ;
    }

    /// <summary>
    /// <see cref="DefaultEmitter"/> assigns each of these through a <c>this.</c> prefix, which on a static
    /// field is CS0176. Statics must not gate defaultability either: one the type cannot zero says nothing
    /// about whether an INSTANCE can be defaulted.
    /// </summary>
    private static IEnumerable<Field> InstanceFields(ConcreteType type)
    {
        return type.FieldsList.Where(field => !field.Modifiers.IsStatic);
    }

    private static DefaultValueEntry? BlockEntry(ConcreteType type, Field field)
    {
        if(type.DefaultBlock is null)
        {
            return null;
        }

        foreach(var entry in type.DefaultBlock.Entries)
        {
            if(entry is DefaultValueEntry value && value.FieldName == field.Name)
            {
                return value;
            }
        }

        return null;
    }

    private static bool HasParameterlessSlot(ConcreteType type)
    {
        return type.ConstructorsList.Count == 0 || NeedsSentinel(type);
    }

    private static bool BaseIsDefaultable(ConcreteType type)
    {
        return type.BaseType is not MemberType baseType || IsDefaultable(baseType);
    }
}