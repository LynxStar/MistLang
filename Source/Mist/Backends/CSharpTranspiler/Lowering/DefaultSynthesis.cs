using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
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
    /// that needs a default -- the expression, a slot of storage the synthesis is answering for, and the
    /// witness a call passes for a constrained type argument -- resolves it here, so the three cannot
    /// disagree. A refinement has none, no rule judging a <c>default</c> of one: only its storage starts
    /// from its base's zero, through <see cref="StorageZero"/>, after DefaultabilityAnalyzer has held that
    /// zero against the predicate.
    /// </summary>
    public static string? Expression(IMistType type)
    {
        type = TypeNameEmitter.Closed(type);

        if(BodyEmitter.EmitTypeDefault(type) is { } literal)
        {
            return literal;
        }

        if(type is TypeParameter parameter && DefaultWitnesses.IsWitnessed(parameter))
        {
            return $"{DefaultConstraintWitness.Name(parameter)}()";
        }

        if(Defaultability.DeclarationOf(type) is { } declaration && IsDefaultable(declaration))
        {
            return DefaultWitnesses.FactoryArguments(declaration, type) is { } witnesses ? Factory(type, witnesses) : null;
        }

        // An enum is defaultable exactly when it designates a variant, there being no other way to
        // choose between them -- unlike a class, whose fields each have a zero of their own.
        return type is EnumType { MembersList: { } variants } && variants.Exists(variant => variant.IsDefault)
            ? Factory(type, [])
            : null
            ;
    }

    private static string Factory(IMistType type, List<string> witnesses)
    {
        return $"{TypeNameEmitter.Emit(type)}.{FactoryName}({string.Join(", ", witnesses)})";
    }

    public static string? StorageZero(IMistType type)
    {
        return type is RefinedType refined ? StorageZero(refined.BaseType) : Expression(type);
    }

    public static bool IsDefaultable(MemberType type)
    {
        return Defaultability.IsDefaultable(type);
    }

    public static bool NeedsSentinel(ConcreteType type)
    {
        return Defaultability.NeedsSentinel(type);
    }

    public static bool UsesSentinel(ConcreteType type)
    {
        return NeedsSentinel(type) || DefaultWitnesses.Of(type).Count > 0 || ConstructorWitnesses.Stored(type).Count > 0;
    }

    /// <summary>
    /// A derived class's default reaches its base's part through the base's sentinel, never C#'s
    /// implicit <c>base()</c>: that would run a user constructor, and a default runs none. A base without
    /// constructors of its own needs no sentinel, its public parameterless constructor being its default,
    /// unless it takes witnesses, which that constructor cannot be passed.
    /// </summary>
    public static bool ChainsToBaseSentinel(ConcreteType type)
    {
        return type is ClassType && BaseDeclaration(type) is { } baseType && UsesSentinel(baseType);
    }

    public static List<string> BaseWitnesses(ConcreteType type)
    {
        return BaseDeclaration(type) is { } baseType ? DefaultWitnesses.FactoryArguments(baseType, type.BaseType!) ?? [] : [];
    }

    private static ConcreteType? BaseDeclaration(ConcreteType type)
    {
        return type.BaseType is { } baseType ? Defaultability.DeclarationOf(baseType) : null;
    }

    public static string SentinelAccessibility(ConcreteType type)
    {
        return type is ClassType { Modifiers.IsSealed: false } ? "protected" : "private";
    }

    public static List<(ConcreteType Ancestor, IReadOnlyList<ConstructedType> Path)> FlattenedAncestors(ConcreteType type)
    {
        var ancestors = new List<(ConcreteType, IReadOnlyList<ConstructedType>)>();

        if(type is not StructType)
        {
            return ancestors;
        }

        var path = new List<ConstructedType>();

        for(var written = type.BaseType; written is not null && Defaultability.DeclarationOf(written) is { } ancestor; written = ancestor.BaseType)
        {
            if(written is ConstructedType constructed)
            {
                path.Add(constructed);
            }

            ancestors.Insert(0, (ancestor, path.ToList()));
        }

        return ancestors;
    }

    /// <summary>
    /// Each slot of storage paired with what its default assigns, fields before properties, each in
    /// declaration order. A default block entry REPLACES the field's own answer rather than adding to
    /// it, which is what makes a block on a type that also has initializers mean the block's values and
    /// not both.
    /// </summary>
    public static List<DefaultAssignment> Assignments(ConcreteType type)
    {
        var assignments = new List<DefaultAssignment>();

        foreach(var slot in InstanceStorage.Of(type))
        {
            var entry = Defaultability.BlockEntry(type, slot);
            assignments.Add(new DefaultAssignment(
                slot,
                (entry as DefaultValueEntry)?.Value ?? (entry is null ? slot.Initializer : null),
                entry as DefaultBindingEntry));
        }

        return assignments;
    }

    /// <summary>
    /// Everything a struct's default assigns, its ancestors' storage first. An ancestor's property that a
    /// nearer struct redeclares is left out: flattening keeps only the nearer declaration, so the
    /// ancestor's assignment would land on it after its initializer had run.
    /// </summary>
    public static List<DefaultAssignment> FlattenedAssignments(ConcreteType type)
    {
        var chain = FlattenedAncestors(type).Append((type, [])).ToList();
        var assignments = new List<DefaultAssignment>();

        for(var i = 0; i < chain.Count; i++)
        {
            var (declaring, path) = chain[i];
            var nearer = chain.Skip(i + 1).SelectMany(link => link.Ancestor.PropertiesList).Select(property => property.Name).ToHashSet();
            assignments.AddRange(Assignments(declaring)
                .Where(assignment => assignment.Storage.Property is not { } property || !nearer.Contains(property.Name))
                .Select(assignment => assignment with { Path = path }));
        }

        return assignments;
    }
}

/// <summary>
/// What one slot's default assigns: an expression, a type binding to construct and override, or
/// neither, meaning the slot type's own literal default.
/// </summary>
internal readonly record struct DefaultAssignment(
    InstanceStorage Storage, Expression? Value, DefaultBindingEntry? Binding)
{
    public IReadOnlyList<ConstructedType> Path { get; init; } = [];
}