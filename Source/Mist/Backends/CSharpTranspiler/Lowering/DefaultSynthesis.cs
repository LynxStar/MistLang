using Mist.Backends.CSharpTranspiler.Emitter;
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
/// <para>
/// A type is defaultable only if it states a zero -- a default block, no constructor at all, or a
/// parameterless one. Requiring arguments and saying nothing else is the author saying the type has no
/// sensible zero. Emission is GATED on this rather than a diagnostic being raised for the rest, because
/// refusing a program is a judgement on a finished model and nothing in this pipeline makes one yet.
/// </para>
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
        return IsDefaultable(type, []);
    }

    /// <summary>
    /// The set holds the types already under test, and a type reached again through its own fields is
    /// answered NOT defaultable: a cycle means the default is infinitely deep, so there is no instance
    /// to build rather than one this pipeline declines to build.
    /// </summary>
    private static bool IsDefaultable(MemberType type, HashSet<MemberType> visiting)
    {
        if(!visiting.Add(type))
        {
            return false;
        }

        try
        {
            return type is ConcreteType { IsIntrinsic: false } concrete
                && StatesAZero(concrete)
                && BaseIsDefaultable(concrete, visiting)
                && InstanceFields(concrete).All(field => CanDefault(concrete, field, visiting))
                ;
        }
        finally
        {
            _ = visiting.Remove(type);
        }
    }

    /// <summary>
    /// Whether defaulting has to route through the sentinel, which is whenever the type declares a
    /// constructor of its own. Not just when that constructor is the parameterless one: a type spelling
    /// only <c>T(int)</c> has no parameterless constructor in Mist either, and emitting a public one to
    /// default it would hand C# a construction Mist rejects.
    /// </summary>
    public static bool NeedsSentinel(ConcreteType type)
    {
        return type.ConstructorsList.Exists(c => !c.Modifiers.IsStatic);
    }

    /// <summary>
    /// Each field paired with what its default assigns, in declaration order. A default block entry
    /// REPLACES the field's own answer rather than adding to it, which is what makes a block on a type
    /// that also has initializers mean the block's values and not both.
    /// </summary>
    public static List<DefaultAssignment> Assignments(ConcreteType type)
    {
        var assignments = new List<DefaultAssignment>();

        foreach(var field in InstanceFields(type))
        {
            var entry = BlockEntry(type, field);

            assignments.Add(new DefaultAssignment(
                field,
                (entry as DefaultValueEntry)?.Value ?? (entry is null ? field.Initializer : null),
                entry as DefaultBindingEntry));
        }

        return assignments;
    }

    /// <summary>
    /// A field with no answer is what makes the whole type non-defaultable, since a constructor that
    /// skipped it would leave it at C#'s zero rather than Mist's -- null for a string where Mist means
    /// the empty one. A field holding a defaultable type has one: that type's own default.
    /// </summary>
    private static bool CanDefault(ConcreteType type, Field field, HashSet<MemberType> visiting)
    {
        var entry = BlockEntry(type, field);

        if(entry is DefaultBindingEntry binding)
        {
            return binding.BoundType is MemberType bound && IsDefaultable(bound, visiting);
        }

        return entry is not null
            || field.Initializer is not null
            || BodyEmitter.EmitTypeDefault(field.Type) is not null
            || (field.Type is MemberType nested && IsDefaultable(nested, visiting))
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

    private static DefaultEntry? BlockEntry(ConcreteType type, Field field)
    {
        if(type.DefaultBlock is null)
        {
            return null;
        }

        foreach(var entry in type.DefaultBlock.Entries)
        {
            if(entry.FieldName == field.Name)
            {
                return entry;
            }
        }

        return null;
    }

    private static bool StatesAZero(ConcreteType type)
    {
        return type.DefaultBlock is not null || HasParameterlessConstructor(type);
    }

    /// <summary>
    /// The base must also leave a parameterless constructor STANDING, not merely be defaultable: a base
    /// that states its zero through a default block alone emits only the private sentinel, and the
    /// derived sentinel's implicit <c>base()</c> would then name a constructor that does not exist.
    /// </summary>
    private static bool BaseIsDefaultable(ConcreteType type, HashSet<MemberType> visiting)
    {
        return type.BaseType is not MemberType baseType
            || (IsDefaultable(baseType, visiting)
                && baseType is ConcreteType concrete
                && HasParameterlessConstructor(concrete))
            ;
    }

    private static bool HasParameterlessConstructor(ConcreteType type)
    {
        return !NeedsSentinel(type)
            || type.ConstructorsList.Exists(c => c.ParametersList.Count == 0 && !c.Modifiers.IsStatic)
            ;
    }
}

/// <summary>
/// What one field's default assigns: an expression, a type binding to construct and override, or
/// neither, meaning the field type's own literal default.
/// </summary>
internal readonly record struct DefaultAssignment(
    Field Field, Expression? Value, DefaultBindingEntry? Binding);