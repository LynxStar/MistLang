using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// What a type's <c>default</c> is, lowered to a parameterless constructor plus a static factory so
/// that <c>default(T)</c> becomes an ordinary call. Mist's default of a class is an INSTANCE with every
/// field defaulted, not a null reference, so there is nothing C# can be asked for here.
/// </summary>
internal static class DefaultSynthesis
{
    public const string FactoryName = "__CreateDefault";

    /// <summary>
    /// The marker discriminating the sentinel constructor, needed where the type declares constructors
    /// of its own so construction and defaulting have to diverge. Distinct from
    /// <see cref="StructAncestorFactory.MarkerType"/> rather than shared with it: on a struct with no
    /// fields and no properties that factory's constructor takes its marker ALONE, so one marker serving
    /// both would put two identical signatures on the same type.
    /// </summary>
    public const string MarkerType = $"{ConstructorConstraintInterfaces.Namespace}.__DefaultInit";

    public const string MarkerTypeName = "__DefaultInit";

    /// <summary>
    /// The default of a type as a C# expression, or null where this backend cannot name one. Every site
    /// that needs a default -- the expression, a field the synthesis is answering for, and the witness a
    /// call passes for a constrained type argument -- resolves it here, so the three cannot disagree.
    /// </summary>
    public static string? Expression(IMistType type)
    {
        type = TypeNameEmitter.Closed(type);

        if(BodyEmitter.EmitTypeDefault(type) is { } literal)
        {
            return literal;
        }

        if(type is TypeParameter parameter && DefaultConstraintWitness.Constrains(parameter))
        {
            return $"{DefaultConstraintWitness.Name(parameter)}()";
        }

        if(type is MemberType member && IsDefaultable(member))
        {
            return Factory(type);
        }

        // An enum is defaultable exactly when it designates a variant, there being no other way to
        // choose between them -- unlike a class, whose fields each have a zero of their own.
        return type is EnumType { MembersList: { } variants } && variants.Exists(variant => variant.IsDefault)
            ? Factory(type)
            : null
            ;
    }

    private static string Factory(IMistType type)
    {
        return $"{TypeNameEmitter.Emit(type)}.{FactoryName}()";
    }

    public static bool IsDefaultable(MemberType type)
    {
        return Defaultability.IsDefaultable(type);
    }

    public static bool NeedsSentinel(ConcreteType type)
    {
        return Defaultability.NeedsSentinel(type);
    }

    /// <summary>
    /// A derived class's default reaches its base's part through the base's sentinel, never C#'s
    /// implicit <c>base()</c>: that would run a user constructor, and a default runs none. A base without
    /// constructors of its own needs no sentinel, its public parameterless constructor being its default.
    /// </summary>
    public static bool ChainsToBaseSentinel(ConcreteType type)
    {
        return type is ClassType && type.BaseType is ConcreteType { IsIntrinsic: false } baseType && NeedsSentinel(baseType);
    }

    public static string SentinelAccessibility(ConcreteType type)
    {
        return type is ClassType { Modifiers.IsSealed: false } ? "protected" : "private";
    }

    public static IEnumerable<ConcreteType> FlattenedAncestors(ConcreteType type)
    {
        var ancestors = new List<ConcreteType>();

        if(type is not StructType)
        {
            return ancestors;
        }

        for(var ancestor = type.BaseType as ConcreteType; ancestor is not null; ancestor = ancestor.BaseType as ConcreteType)
        {
            ancestors.Insert(0, ancestor);
        }

        return ancestors;
    }

    /// <summary>
    /// Each field paired with what its default assigns, in declaration order. A default block entry
    /// REPLACES the field's own answer rather than adding to it, which is what makes a block on a type
    /// that also has initializers mean the block's values and not both.
    /// </summary>
    public static List<DefaultAssignment> Assignments(ConcreteType type)
    {
        var assignments = new List<DefaultAssignment>();

        foreach(var field in Defaultability.InstanceFields(type))
        {
            var entry = Defaultability.BlockEntry(type, field);
            assignments.Add(new DefaultAssignment(
                field,
                (entry as DefaultValueEntry)?.Value ?? (entry is null ? field.Initializer : null),
                entry as DefaultBindingEntry));
        }

        return assignments;
    }
}

/// <summary>
/// What one field's default assigns: an expression, a type binding to construct and override, or
/// neither, meaning the field type's own literal default.
/// </summary>
internal readonly record struct DefaultAssignment(
    Field Field, Expression? Value, DefaultBindingEntry? Binding);