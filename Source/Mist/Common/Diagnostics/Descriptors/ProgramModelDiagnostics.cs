#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

/// <summary>
/// Diagnostic descriptors for the program model construction phase.
/// Covers namespace validation, type registration, type resolution,
/// base type resolution, and member resolution.
/// </summary>
public static partial class ProgramModelDiagnostics
{
    // ── Internal / driver-level errors ────────────────────────────────────

    public static readonly MistDiagnostic UnexpectedNamespaceNode = new(
        DiagnosticNumber.Mint("MIST0288"),
        Title: "Unexpected namespace node",
        Template: "Internal compiler error: unexpected namespace declaration node type '{0}'.",
        Description: "The program model builder met a namespace declaration whose node type it does not handle, which no valid source can produce. Report this as a compiler bug together with the source that triggered it.",
        Category: DiagnosticCategory.Ice,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic UnknownTypeDeclaration = new(
        DiagnosticNumber.Mint("MIST0289"),
        Title: "Unrecognized type declaration node",
        Template: "Internal compiler error: unrecognized type declaration node '{0}'.",
        Description: "Type registration met a declaration node of a kind the model builder was never taught, which no valid source can produce. Report this as a compiler bug together with the source that triggered it.",
        Category: DiagnosticCategory.Ice,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    // ── Namespace validation ─────────────────────────────────────────────

    public static readonly MistDiagnostic DuplicateFileScopedNamespace = new(
        DiagnosticNumber.Mint("MIST0290"),
        Title: "Multiple file-scoped namespaces",
        Template: "Only one file-scoped namespace is permitted per file.",
        Description: "A source file declares a second file-scoped namespace, but a file-scoped declaration already covers the whole file and so can appear only once. Remove the extra declaration, or switch to block namespaces if the file must span several.",
        Category: DiagnosticCategory.Syn,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic MixedNamespaceStyles = new(
        DiagnosticNumber.Mint("MIST0291"),
        Title: "Mixed namespace styles",
        Template: "File-scoped and block namespaces cannot be mixed in the same file.",
        Description: "A file uses both a file-scoped namespace and a block namespace, and the two forms cannot coexist because a file-scoped declaration already claims the whole file. Pick one style for the file.",
        Category: DiagnosticCategory.Syn,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic FileScopedInsideBlock = new(
        DiagnosticNumber.Mint("MIST0292"),
        Title: "File-scoped namespace inside block",
        Template: "File-scoped namespace declarations cannot appear inside a block namespace.",
        Description: "A file-scoped namespace declaration sits inside a block namespace, where there is no file left for it to scope. Move it to the top level of the file, or turn it into a block namespace.",
        Category: DiagnosticCategory.Syn,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic UndefinedNamespace = new(
        DiagnosticNumber.Mint("MIST0293"),
        Title: "Unknown namespace",
        Template: "The namespace '{0}' could not be found.",
        Description: "A using directive or qualified name refers to a namespace that no project or referenced assembly in the compilation declares. Check the spelling, or add the reference that supplies it.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic GlobalUsingUndefinedNamespace = new(
        DiagnosticNumber.Mint("MIST0294"),
        Title: "Global using names unknown namespace",
        Template: "Global using namespace '{0}' in project '{1}' could not be found, so it imports nothing.",
        Description: "A project's global using list names a namespace that nothing in the compilation declares, so the entry brings no names into scope. Correct the entry in the project configuration or remove it.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Promotable,
        DefaultSeverity: DiagnosticSeverity.Warning);

    // ── Type resolution ──────────────────────────────────────────────────

    public static readonly MistDiagnostic DuplicateTypeName = new(
        DiagnosticNumber.Mint("MIST0295"),
        Title: "Duplicate type name",
        Template: "A type named '{0}' already exists in namespace '{1}'.",
        Description: "Two type declarations in the same namespace share a name, so references to that name would have no single target. Rename one of them or move it to a different namespace.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic UnresolvedIdentifier = new(
        DiagnosticNumber.Mint("MIST0296"),
        Title: "Unresolved identifier",
        Template: "The name '{0}' could not be found in this scope.",
        Description: "A name in expression position matched nothing in any reachable scope. This covers a misspelling, a name that is not in scope, and an instance member named from a static context, which is excluded from lookup rather than matched and rejected; check the spelling and scope, or qualify the name with an instance.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic AwaitOfNonTask = new(
        DiagnosticNumber.Mint("MIST0297"),
        Title: "Await of non-task",
        Template: "'await' requires a task, but '{0}' is not one.",
        Description: "The operand of 'await' is not a task, so it has no result to unwrap; this is a rule of the language rather than of any backend. Await a task-valued expression, or drop the 'await'.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic UnresolvedType = new(
        DiagnosticNumber.Mint("MIST0298"),
        Title: "Unresolved type",
        Template: "The type '{0}' could not be found.",
        Description: "A type reference names a type that no reachable scope declares or imports. Check the spelling, add the using directive that brings it into scope, or reference the project that declares it.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic AmbiguousType = new(
        DiagnosticNumber.Mint("MIST0299"),
        Title: "Ambiguous type name",
        Template: "The type name '{0}' is ambiguous between imported namespaces '{1}' and '{2}'. Use a fully-qualified name to disambiguate.",
        Description: "A type name resolves to distinct types in two imported namespaces at the same scope level, so the reference has no single meaning. Qualify the name fully, or remove one of the imports.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic CyclicTypeAlias = new(
        DiagnosticNumber.Mint("MIST0300"),
        Title: "Cyclic type alias",
        Template: "Type alias '{0}' forms a cycle.",
        Description: "A type alias expands, directly or through other aliases, back to itself, so it names no finite target. It is reported once at the declaration where the expansion re-entered itself and the alias is treated as an error type; break the cycle by making one alias name a concrete type.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic UnresolvedMember = new(
        DiagnosticNumber.Mint("MIST0301"),
        Title: "Unresolved member",
        Template: "'{0}' has no member named '{1}'.",
        Description: "A member access names something that neither the receiver's type nor any of its bases declares. Check the member name, or confirm the receiver has the type you expect.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic AmbiguousMember = new(
        DiagnosticNumber.Mint("MIST0302"),
        Title: "Ambiguous member",
        Template: "The member '{1}' on '{0}' is ambiguous between {2}.",
        Description: "A member name matched several equally close declarations on the receiver, so no single one answers it. Qualify the access through the declaring type, or rename one of the colliding members.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic AmbiguousIdentifier = new(
        DiagnosticNumber.Mint("MIST0303"),
        Title: "Ambiguous identifier",
        Template: "The identifier '{0}' is ambiguous between {1}.",
        Description: "A bare name matches more than one declaration at the same scope distance, so the reference cannot pick one. Qualify the name with its declaring type or namespace, or remove the import that introduces the duplicate.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic UnknownTypeRefNode = new(
        DiagnosticNumber.Mint("MIST0304"),
        Title: "Unrecognized type reference node",
        Template: "Internal compiler error: unrecognized type reference node '{0}'.",
        Description: "Type resolution met a type reference node of a kind it does not handle, which no valid source can produce. Report this as a compiler bug together with the source that triggered it.",
        Category: DiagnosticCategory.Ice,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ReservedTupleElementName = new(
        DiagnosticNumber.Mint("MIST0305"),
        Title: "Reserved tuple element name",
        Template: "'{0}' is a reserved tuple element name; it collides with the positional element accessors. Choose a different name.",
        Description: "A tuple element is given a name of the form the positional accessors already use, such as Item1, so the explicit name would shadow the positional one. Pick a name outside the reserved positional pattern.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    // ── Base type resolution ─────────────────────────────────────────────

    public static readonly MistDiagnostic BaseTypeKindMismatch = new(
        DiagnosticNumber.Mint("MIST0306"),
        Title: "Base type kind mismatch",
        Template: "A {0} cannot inherit from a {1} ('{2}').",
        Description: "A type's base list names a type of a kind it cannot derive from, for example a struct deriving from a class. Remove the entry, or change the declaration to a kind that can derive from it.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ConcreteInAbstractList = new(
        DiagnosticNumber.Mint("MIST0307"),
        Title: "Concrete type in abstract list",
        Template: "A concrete type ('{0}') cannot appear in the abstract implementations list. Only the first entry may be a concrete base type.",
        Description: "A type's base list places a class or struct after the first position, where only abstract types may appear; a type has at most one concrete base and it must come first. Move the concrete base to the front or remove it.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic InvalidBaseType = new(
        DiagnosticNumber.Mint("MIST0308"),
        Title: "Invalid base type",
        Template: "'{0}' is not an abstract and cannot be used as a base type.",
        Description: "A base list entry names a type that is not an abstract and so cannot be conformed to, such as an enum or an alias of a primitive. Replace it with the abstract the declaration should conform to, or with a class it should derive from.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    // ── Member resolution ────────────────────────────────────────────────

    public static readonly MistDiagnostic GetterCannotHaveModifier = new(
        DiagnosticNumber.Mint("MIST0309"),
        Title: "Modifier on getter",
        Template: "The getter on property '{0}' cannot have a modifier.",
        Description: "A property's getter carries an accessibility modifier, but a getter always shares the accessibility of its property. Remove the modifier from the getter; narrow the setter instead if the two must differ.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic SetterModifierMustBePrivate = new(
        DiagnosticNumber.Mint("MIST0310"),
        Title: "Non-private setter modifier",
        Template: "Only 'private' is permitted as a modifier on the setter of property '{0}'.",
        Description: "A setter carries an accessibility modifier other than 'private', and 'private' is the only narrowing a setter may declare relative to its property. Change the modifier to 'private' or remove it.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic EmptyAccessorList = new(
        DiagnosticNumber.Mint("MIST0311"),
        Title: "Empty accessor list",
        Template: "'{0}' must declare at least a getter or a setter.",
        Description: "A property or indexer declares an accessor block with no getter and no setter in it, so it could be neither read nor written. Add at least one accessor.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic IndexerCannotHaveInit = new(
        DiagnosticNumber.Mint("MIST0312"),
        Title: "Init accessor on indexer",
        Template: "Indexers cannot declare init accessors.",
        Description: "An indexer declares an init accessor, but init accessors exist for object initializers and an indexer has no single member slot an initializer could target. Use a setter, or drop the accessor.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic AbstractAccessorCannotHaveModifier = new(
        DiagnosticNumber.Mint("MIST0313"),
        Title: "Modifier on abstract accessor",
        Template: "Accessors on abstract property '{0}' cannot have modifiers.",
        Description: "An accessor of a property declared on an abstract type carries a modifier, but abstract members state a contract and the conforming type decides accessibility. Remove the modifier.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic AbstractPropertyInitAccessor = new(
        DiagnosticNumber.Mint("MIST0314"),
        Title: "Init accessor on abstract property",
        Template: "Abstract types cannot declare init accessors on property '{0}' because abstract types cannot be instantiated.",
        Description: "A property on an abstract type declares an init accessor, but init runs during construction and an abstract type is never constructed. Replace it with a setter or make the property getter-only.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic StaticPropertyInitAccessor = new(
        DiagnosticNumber.Mint("MIST0315"),
        Title: "Init accessor on static property",
        Template: "Static property '{0}' cannot declare an init accessor: init runs while an instance is constructed, and a static property belongs to no instance.",
        Description: "A static property declares an init accessor, which only has meaning while a particular instance is being constructed. Use a setter instead.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    // ── Modifier validation ──────────────────────────────────────────────

    public static readonly MistDiagnostic ConflictingDisciplineModifiers = new(
        DiagnosticNumber.Mint("MIST0316"),
        Title: "Conflicting discipline modifiers",
        Template: "A declaration cannot have more than one substructural discipline modifier. Specify at most one of 'affine', 'linear', or 'relevant'.",
        Description: "A declaration names more than one of 'affine', 'linear' and 'relevant', but a value follows exactly one substructural discipline. Keep only the modifier that states how the value may be used.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic StampableRequiresStruct = new(
        DiagnosticNumber.Mint("MIST0317"),
        Title: "Stampable on non-struct",
        Template: "The 'stampable' modifier is only valid on a non-ref struct declaration.",
        Description: "The 'stampable' modifier appears on a declaration that is not a plain struct, but stamping copies a value's bits and only a non-ref struct has bits that can be copied that way. Remove the modifier or change the declaration to a struct.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic PureAndLocal = new(
        DiagnosticNumber.Mint("MIST0318"),
        Title: "Pure combined with local",
        Template: "A declaration cannot be both 'pure' and 'local': 'local' allows a member to change its own instance, which 'pure' forbids.",
        Description: "A member is declared with both 'pure' and 'local', but 'local' grants the member the right to mutate its own instance while 'pure' forbids any mutation. Drop whichever modifier does not describe the member.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ReadonlyImpliedByPure = new(
        DiagnosticNumber.Mint("MIST0319"),
        Title: "Redundant readonly with pure",
        Template: "'readonly' is implied by 'pure' and can be omitted.",
        Description: "A member states 'readonly' alongside 'pure', but purity already forbids mutating the instance, so the extra modifier adds nothing. Remove 'readonly'.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Promotable,
        DefaultSeverity: DiagnosticSeverity.Info);

    public static readonly MistDiagnostic LocalRequiresInstance = new(
        DiagnosticNumber.Mint("MIST0320"),
        Title: "Local on non-instance member",
        Template: "'local' is only valid on an instance member: a static member or a constructor has no instance of its own.",
        Description: "The 'local' modifier is placed on a static member or a constructor, but it scopes mutation to the member's own instance and those declarations have none. Remove the modifier.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic PurityModifierPlacement = new(
        DiagnosticNumber.Mint("MIST0321"),
        Title: "Purity modifier misplaced",
        Template: "'{0}' is not valid on {1}.{2}",
        Description: "A purity or scope modifier such as 'pure' or 'local' appears on a kind of declaration that cannot carry it. Remove the modifier, or move it to a declaration kind that accepts it.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic PureRequiresFunctionType = new(
        DiagnosticNumber.Mint("MIST0322"),
        Title: "Pure on non-function type",
        Template: "'pure' can only qualify a function type, and '{0}' is not one.",
        Description: "A type is qualified with 'pure', but purity is a property of callables and only a function type such as 'func<int, bool>' can carry it. Remove the qualifier, or apply it to the function type you meant.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic AsyncOnlyOnMethods = new(
        DiagnosticNumber.Mint("MIST0323"),
        Title: "Async on non-method",
        Template: "The 'async' modifier is only valid on methods.",
        Description: "The 'async' modifier is placed on a declaration that is not a method, such as a field, property or type. Remove it; only a method body can suspend on 'await'.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic IntrinsicNotValidOnMember = new(
        DiagnosticNumber.Mint("MIST0324"),
        Title: "Intrinsic on unsupported member",
        Template: "The 'intrinsic' modifier is not valid on {0}.",
        Description: "The 'intrinsic' modifier marks a member of a kind the backend cannot supply a definition for. Remove the modifier and give the member an ordinary definition.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic MethodMustHaveBody = new(
        DiagnosticNumber.Mint("MIST0325"),
        Title: "Method without body",
        Template: "Method '{0}' must have a body unless it is declared 'intrinsic' or is on an abstract type.",
        Description: "A method on a concrete type ends in ';' with no body, and only abstract members and intrinsic members may omit one. Add a body, or mark the member 'intrinsic' if it belongs to the Standard library and the backend supplies it.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic IntrinsicMemberCannotHaveBody = new(
        DiagnosticNumber.Mint("MIST0326"),
        Title: "Body on intrinsic member",
        Template: "Intrinsic member '{0}' must not have a body; use ';' to mark it as backend-provided.",
        Description: "A member marked 'intrinsic' also has a body, but the modifier says the backend provides the implementation. Remove the body or the modifier.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic IntrinsicReservedForStandard = new(
        DiagnosticNumber.Mint("MIST0327"),
        Title: "Intrinsic outside Standard library",
        Template: "The 'intrinsic' modifier on '{0}' is reserved for the Standard library. It says the backend supplies the declaration, and a backend has nothing to supply for a type declared outside it.",
        Description: "A type outside the Standard library uses 'intrinsic', which asks the backend to supply a declaration it only knows how to supply for the Standard library. Remove the modifier and implement the member in Mist.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic IntrinsicRequiresIntrinsicType = new(
        DiagnosticNumber.Mint("MIST0328"),
        Title: "Intrinsic member in non-intrinsic type",
        Template: "Member-level 'intrinsic' is only permitted inside a type that is itself declared 'intrinsic'. Type '{0}' is not intrinsic.",
        Description: "A member is marked 'intrinsic' inside a type that is not itself intrinsic, and the backend binds intrinsic members only through an intrinsic type. Mark the enclosing type 'intrinsic' or give the member a Mist body.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic IntrinsicMemberMustBePublic = new(
        DiagnosticNumber.Mint("MIST0329"),
        Title: "Non-public intrinsic member",
        Template: "The 'intrinsic' modifier requires the member to be declared 'public'.",
        Description: "An intrinsic field, property or indexer is declared with an accessibility other than 'public', but a backend-supplied member has to be visible to the surface that maps onto it. Declare it 'public'.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic IndexerCannotHaveAutoAccessor = new(
        DiagnosticNumber.Mint("MIST0330"),
        Title: "Auto-accessor on indexer",
        Template: "Indexers cannot use auto-accessors; provide a body or expression for each accessor.",
        Description: "An indexer on a concrete type uses 'get;' or 'set;' without a body, but an indexer has no backing field for an auto-accessor to read or write. Give each accessor a body or an expression.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    // ── Cast operator validation ─────────────────────────────────────────

    public static readonly MistDiagnostic CastOperatorMissingParameter = new(
        DiagnosticNumber.Mint("MIST0331"),
        Title: "Cast operator without parameter",
        Template: "A cast operator must declare exactly one parameter (the source value).",
        Description: "A cast operator is declared with no parameter, but a conversion needs the source value as its single parameter. Add a parameter of the type being converted from.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic CastOperatorTooManyParameters = new(
        DiagnosticNumber.Mint("MIST0332"),
        Title: "Cast operator with extra parameters",
        Template: "A cast operator must declare exactly one parameter; additional parameters are not permitted.",
        Description: "A cast operator declares more than one parameter, but a conversion takes only the source value. Remove the extra parameters.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic OperatorMustBeStatic = new(
        DiagnosticNumber.Mint("MIST0333"),
        Title: "Non-static operator",
        Template: "Operators must be declared 'static'.",
        Description: "An operator or cast operator is declared without 'static', but operators are resolved on their operand types rather than on an instance. Add the 'static' modifier.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic OperatorWrongParameterCount = new(
        DiagnosticNumber.Mint("MIST0334"),
        Title: "Operator parameter count mismatch",
        Template: "Operator expects {0} parameter(s) but has {1}.",
        Description: "An operator declares a number of parameters that does not match its kind, for example a binary operator with one parameter. Give it exactly the parameters its kind requires.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic MemberNotAllowedOnAbstract = new(
        DiagnosticNumber.Mint("MIST0335"),
        Title: "Member not allowed on abstract",
        Template: "This member is not permitted on abstract types.",
        Description: "An abstract type declares a kind of member that abstract types cannot contain. Move the member to the concrete types that conform to the abstract.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic TypeSlotNotAllowedOnConcrete = new(
        DiagnosticNumber.Mint("MIST0336"),
        Title: "Type slot on concrete type",
        Template: "Type slots are only permitted on abstract types.",
        Description: "A class or struct declares a type slot, but slots are the abstract type's way of leaving a type for conformers to fill. Remove the slot, or move it to the abstract the type conforms to.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    // ── Default block validation ─────────────────────────────────────────

    public static readonly MistDiagnostic DuplicateDefaultBlock = new(
        DiagnosticNumber.Mint("MIST0337"),
        Title: "Duplicate default block",
        Template: "Type '{0}' declares more than one default block; only one is permitted per type.",
        Description: "A type contains two default blocks, and the defaults a type supplies live in a single block. Merge them into one.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    // ── Constraint resolution ────────────────────────────────────────────

    public static readonly MistDiagnostic UnresolvedConstraintTarget = new(
        DiagnosticNumber.Mint("MIST0338"),
        Title: "Unknown constraint target",
        Template: "The constraint clause references type parameter '{0}', which is not declared on this type or method.",
        Description: "A where clause constrains a type parameter that the enclosing type or method does not declare. Check the spelling, or add the type parameter to the declaration.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ConflictingConstraints = new(
        DiagnosticNumber.Mint("MIST0339"),
        Title: "Conflicting constraints",
        Template: "Type parameter '{0}' has conflicting constraints: '{1}' and '{2}' are mutually exclusive.",
        Description: "A type parameter's where clause combines constraints that no type can satisfy at once. Remove one of them.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic DuplicateConstraint = new(
        DiagnosticNumber.Mint("MIST0340"),
        Title: "Duplicate constraint",
        Template: "Type parameter '{0}' has duplicate '{1}' constraint; the redundant occurrence has no effect.",
        Description: "A where clause repeats the same constraint keyword on one type parameter, which changes nothing. Delete the repeated occurrence.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Promotable,
        DefaultSeverity: DiagnosticSeverity.Warning);

    // ── With clause resolution ───────────────────────────────────────────

    public static readonly MistDiagnostic ThisTierOnConstructor = new(
        DiagnosticNumber.Mint("MIST0341"),
        Title: "This tier on constructor",
        Template: "A 'with {{tier}}' clause is not valid on a constructor; constructors always produce an owned value.",
        Description: "A constructor declares a 'with' clause that sets the access tier of 'this', but a constructor always yields an owned value and so has no tier to choose. Remove the clause.",
        Category: DiagnosticCategory.Own,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    // ── Extension block validation ───────────────────────────────────────

    public static readonly MistDiagnostic ExtensionPropertyCannotHaveAutoAccessor = new(
        DiagnosticNumber.Mint("MIST0342"),
        Title: "Auto-accessor on extension property",
        Template: "Extension property '{0}' cannot use auto-accessors (get; / set;); extension properties have no backing field.",
        Description: "An extension property uses 'get;' or 'set;' without a body, but an extension adds no storage to its receiver for an auto-accessor to use. Write a body or an expression for each accessor.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic FieldKeywordOutsideAccessor = new(
        DiagnosticNumber.Mint("MIST0343"),
        Title: "Field keyword outside accessor",
        Template: "'field' is only meaningful inside a property accessor, where it names that property's storage.",
        Description: "The contextual keyword 'field' appears where no property accessor encloses it, so there is no backing storage for it to name; the grammar admits it in any expression and the model refuses the placement rather than leave a backend with nothing to emit. Move the expression into a property accessor or name a real variable.",
        Category: DiagnosticCategory.Syn,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic MemberNameMatchesEnclosingType = new(
        DiagnosticNumber.Mint("MIST0344"),
        Title: "Member named after enclosing type",
        Template: "A member cannot have the same name as its enclosing type '{0}'. Rename the member.",
        Description: "A member has the same name as the type that declares it. The backend rejects such a declaration outright, and inside the type the bare name would resolve to the member where a type was meant, so rename the member.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic PatternTypeNotAConstituent = new(
        DiagnosticNumber.Mint("MIST0345"),
        Title: "Pattern type not a constituent",
        Template: "'{0}' is not one of the types '{1}' can hold, so this pattern can never match.",
        Description: "A type pattern tests a union-typed value for a type that none of the union's constituents can hold. Narrowing a union goes through the accessor for the matching constituent, so a type outside the union has no lowering rather than merely a test that always fails; test for one of the constituent types instead.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic SwitchExpressionNeedsAnArm = new(
        DiagnosticNumber.Mint("MIST0346"),
        Title: "Switch expression without arms",
        Template: "A switch expression must declare at least one arm, because its type comes from what its arms produce.",
        Description: "A switch expression has an empty arm list, so there is nothing to give the expression a type or a value. Add at least one arm.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic SwitchArmsNoCommonType = new(
        DiagnosticNumber.Mint("MIST0347"),
        Title: "Switch arms lack common type",
        Template: "The arms of the switch expression have no common type ('{0}' and '{1}'). Annotate the target with the type you want.",
        Description: "The arms of a switch expression produce different types and no target type says which is wanted; the compiler does not yet compute a best common type by conversion or numeric promotion, so it asks rather than guesses. Annotate the target with the intended type, or convert the arms to one type.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic PatternBindingNotEstablished = new(
        DiagnosticNumber.Mint("MIST0348"),
        Title: "Binding inside or/not pattern",
        Template: "The pattern variable '{0}' cannot be introduced inside 'or' or 'not', because matching there does not establish a value for it.",
        Description: "A pattern nested under 'or' or 'not' declares a variable, but a disjunction only proves that some operand matched and a negation only proves its operand did not, so no value exists for the name to hold; allowing it would declare a local that no emitted pattern produces. Hoist the binding above the 'or' or 'not', or drop it.",
        Category: DiagnosticCategory.Flw,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic PatternSubpatternNotStorage = new(
        DiagnosticNumber.Mint("MIST0349"),
        Title: "Subpattern target not storage",
        Template: "'{1}' on '{0}' is not a field or a property, so a property pattern cannot match against it.",
        Description: "A property pattern names a member of the scrutinee that is a method, nested type or other non-storage member, which holds no value for the subpattern to be tested against. Match against a field or a property instead.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic PatternSubpatternNotInstance = new(
        DiagnosticNumber.Mint("MIST0350"),
        Title: "Subpattern target is static",
        Template: "'{1}' on '{0}' is static, so a property pattern cannot match against it.",
        Description: "A property pattern names a static member, but a pattern tests the state of the particular value being matched and a static member belongs to no instance. Match against an instance field or property.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic TuplePatternArityMismatch = new(
        DiagnosticNumber.Mint("MIST0351"),
        Title: "Tuple pattern arity mismatch",
        Template: "The pattern spells {2} elements but '{0}' holds {1}, so this pattern can never match.",
        Description: "A tuple pattern lists a different number of element patterns than the matched tuple has elements; the arity pairs each element pattern with the type it narrows, so a mismatch leaves positions typed by nothing. Make the pattern's element count equal to the tuple's.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic TuplePatternScrutineeNotATuple = new(
        DiagnosticNumber.Mint("MIST0352"),
        Title: "Tuple pattern on non-tuple",
        Template: "'{0}' is not a tuple, so a tuple pattern has no elements to match against it.",
        Description: "A tuple pattern is applied to a value that is neither a tuple nor 'unknown', and Mist has no user-declared deconstruction to read element positions out of other types. Match the value with a type or property pattern instead.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ReceiverNotSliceable = new(
        DiagnosticNumber.Mint("MIST0353"),
        Title: "Receiver not sliceable",
        Template: "'{0}' cannot be sliced by a range. A sub-sequence comes from an array, a span or a string, or from a type with an int Length or Count and either a Range indexer or a Slice(int, int) method.",
        Description: "A range index is applied to a value whose type offers no way to take a sub-sequence. Slice an array, span or string, or give the type an int Length or Count together with a Range indexer or a Slice(int, int) method.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ReceiverNotIndexable = new(
        DiagnosticNumber.Mint("MIST0354"),
        Title: "Receiver not indexable",
        Template: "'{0}' cannot be indexed because it declares no indexer and is not an array.",
        Description: "An element access is applied to a value that is not an array and whose type declares no indexer. Index a collection type, or declare an indexer on the type.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ReceiverNotCountable = new(
        DiagnosticNumber.Mint("MIST0355"),
        Title: "Receiver not countable",
        Template: "'{0}' has no int Length or Count, so it cannot be indexed from its end or matched by a list pattern.",
        Description: "An index from the end or a list pattern is applied to a value whose type has no int Length or Count, so there is no count to position against; the model refuses it here rather than hand the program to the backend to reject. Add a Length or Count property to the type, or index from the front.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ListPatternScrutineeNotIndexable = new(
        DiagnosticNumber.Mint("MIST0356"),
        Title: "List pattern on non-indexable",
        Template: "'{0}' cannot be indexed, so a list pattern has no elements to match against it.",
        Description: "A list pattern is applied to a value that cannot be indexed, and Mist has no user-declared deconstruction, so the pattern has no elements to read and nothing for its arity to constrain. Match an array or an indexable type with a count.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ListPatternMultipleSlices = new(
        DiagnosticNumber.Mint("MIST0357"),
        Title: "Multiple slices in list pattern",
        Template: "A list pattern can spell at most one '..' slice, because the slice is what divides the elements counted from the front from those counted from the back.",
        Description: "A list pattern contains more than one '..' slice. A single slice decides which elements count from the front and which from the back, so two slices leave the split undetermined and the pattern would state no length constraint at all; keep one slice.",
        Category: DiagnosticCategory.Syn,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic FunctionTypeNeedsAResult = new(
        DiagnosticNumber.Mint("MIST0358"),
        Title: "Function type without result",
        Template: "A function type must name its result as the last type argument, for example 'func<bool>' or 'func<int, bool>'.",
        Description: "A function type is written with no type arguments, but the result always occupies the last slot, so even the zero-parameter form names a result as in 'func<bool>'. Add the result type.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic LambdaArityMismatch = new(
        DiagnosticNumber.Mint("MIST0359"),
        Title: "Lambda arity mismatch",
        Template: "'{0}' takes {1} parameters but the lambda declares {2}.",
        Description: "A lambda declares a different number of parameters than the function type it is assigned to takes. Match the lambda's parameter list to the target type, or change the target.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic LambdaParameterNeedsAType = new(
        DiagnosticNumber.Mint("MIST0360"),
        Title: "Untyped lambda parameter",
        Template: "The type of lambda parameter '{0}' cannot be inferred. Annotate it, or assign the lambda to a function type.",
        Description: "A lambda parameter has no type annotation and no target function type supplies one; the body is checked against the parameter types, so inferring them from the body would be circular. Annotate the parameter, or assign the lambda to a function type.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic LambdaReturnsNoCommonType = new(
        DiagnosticNumber.Mint("MIST0361"),
        Title: "Lambda returns lack common type",
        Template: "The lambda's returns have no common type ('{0}' and '{1}'). Annotate the target with a function type to select the result.",
        Description: "The return statements of a block-bodied lambda produce values of different types and nothing selects a result type. Assign the lambda to a function type that names the result, or convert the returns to one type.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic FunctionCallArityMismatch = new(
        DiagnosticNumber.Mint("MIST0362"),
        Title: "Function call arity mismatch",
        Template: "'{0}' takes {1} arguments but {2} were passed.",
        Description: "A call through a function-typed value passes a number of arguments different from the number its type declares. Pass exactly as many arguments as the function type takes.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic NotCallable = new(
        DiagnosticNumber.Mint("MIST0363"),
        Title: "Not callable",
        Template: "'{name}' is not a method, and its type '{type}' is not a function type, so it cannot be called.{namespace? An extension method named '{name}' is declared in {namespace}, which is not imported here.}",
        Description: "A call is made on something that is neither a method nor a value of function type. Call a method or a function-typed value, or check that the name resolves to what you intended.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name), new Slot("type", SlotKind.Type), new Slot("namespace", SlotKind.Optional, SlotKind.Name)],
        Related: [DiagnosticNumber.Mint("MIST0364")]);

    public static readonly MistDiagnostic ValueNotCallable = new(
        DiagnosticNumber.Mint("MIST0364"),
        Title: "Value not callable",
        Template: "A value of type '{0}' is not a function, so it cannot be called.",
        Description: "A call is applied to a value whose type is not a function type. Only methods and function-typed values can be invoked; check that the expression is the callable you meant.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Related: [DiagnosticNumber.Mint("MIST0363")]);

    public static readonly MistDiagnostic DeconstructionArityMismatch = new(
        DiagnosticNumber.Mint("MIST0365"),
        Title: "Deconstruction arity mismatch",
        Template: "The deconstruction declares {2} targets but '{0}' holds {1} elements.",
        Description: "A deconstruction declares a different number of targets than the tuple it reads has elements. Declare exactly one target per element.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic DeconstructionSourceNotATuple = new(
        DiagnosticNumber.Mint("MIST0366"),
        Title: "Deconstruction of non-tuple",
        Template: "'{0}' is not a tuple, so it cannot be deconstructed.",
        Description: "A deconstruction reads from a value that is not a tuple, and Mist has no user-declared deconstruction, so a non-tuple has no elements to distribute among the targets. Deconstruct a tuple, or read the members individually.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic DuplicateLocalVariable = new(
        DiagnosticNumber.Mint("MIST0367"),
        Title: "Duplicate local variable",
        Template: "A variable named '{0}' is already declared in this scope.",
        Description: "A local variable is declared with a name already used by another local in the same scope. Rename one of them.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic DuplicateLocalFunction = new(
        DiagnosticNumber.Mint("MIST0368"),
        Title: "Duplicate local function",
        Template: "A local function named '{0}' is already declared in this scope.",
        Description: "Two local functions in the same scope share a name, and local functions do not overload. Rename one of them.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic LocalFunctionModifier = new(
        DiagnosticNumber.Mint("MIST0369"),
        Title: "Modifier on local function",
        Template: "A local function cannot be declared '{0}'.",
        Description: "A local function carries a modifier it cannot have: it has no accessibility to restrict, no 'this' of its own to be readonly or local for, no discipline to declare, and no compiler-supplied body to mark intrinsic. Remove the modifier.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic LocalFunctionGeneric = new(
        DiagnosticNumber.Mint("MIST0370"),
        Title: "Generic local function",
        Template: "Local function '{0}' cannot declare type parameters.",
        Description: "A local function declares type parameters, which local functions do not support. Move the function onto a type as a generic method, or drop the type parameters.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    // ── Body binding ─────────────────────────────────────────────────────

    public static readonly MistDiagnostic ThisOutsideType = new(
        DiagnosticNumber.Mint("MIST0371"),
        Title: "This outside a type",
        Template: "'this' is not valid here because there is no enclosing type. It is only valid inside an instance member of a type.",
        Description: "The 'this' keyword appears where no type encloses it, so there is no instance for it to name. Use it only inside an instance member of a type.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ThisInStaticMember = new(
        DiagnosticNumber.Mint("MIST0372"),
        Title: "This in static member",
        Template: "'this' is not valid in a static member because a static member has no instance to refer to.",
        Description: "The 'this' keyword appears inside a static member, which runs without an instance. Make the member an instance member, or refer to static state instead.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    // ── Array literal inference ──────────────────────────────────────────

    public static readonly MistDiagnostic ForEachElementNotInferable = new(
        DiagnosticNumber.Mint("MIST0373"),
        Title: "Foreach element type not inferable",
        Template: "Cannot infer the element type of '{0}' because it is not an array and conforms to no IEnumerable. Iterate a sequence, or state the element type in place of 'var'.",
        Description: "A 'foreach' declares its variable with 'var' over a collection that is not an array and conforms to no IEnumerable, so there is no element type to give the variable; the binder cannot finish the loop without one, since every read of the variable resolves against that type. Iterate a sequence, or write the element type in place of 'var'.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic RefLocalNeedsInitializer = new(
        DiagnosticNumber.Mint("MIST0374"),
        Title: "Ref local without initializer",
        Template: "Ref local '{0}' must be declared with an initializer: a reference names the storage it aliases where it is declared.",
        Description: "A 'ref' local is declared without an initializer, but a reference binds to the storage it aliases at the point of declaration and cannot be left dangling. Initialize it with the variable or element it should refer to.",
        Category: DiagnosticCategory.Own,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic EmptyArrayLiteralNeedsTargetType = new(
        DiagnosticNumber.Mint("MIST0375"),
        Title: "Empty array literal needs target",
        Template: "The element type of an empty array literal cannot be inferred. Annotate the target with an array type, for example 'int[] x = [];'.",
        Description: "An empty array literal appears where no target type supplies the element type, so the array's type cannot be inferred. Declare the receiving variable or parameter with an array type.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ArrayLiteralNoCommonElementType = new(
        DiagnosticNumber.Mint("MIST0376"),
        Title: "Array elements lack common type",
        Template: "The elements of the array literal have no common type ('{0}' and '{1}'). Annotate the target with an array type to select the element type explicitly.",
        Description: "The elements of an array literal have different types and no target type selects an element type; the compiler does not yet compute a best common type by conversion or numeric promotion, so it asks rather than guesses. Annotate the target with the array type you want.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic NumericLiteralOutOfRange = new(
        DiagnosticNumber.Mint("MIST0377"),
        Title: "Numeric literal out of range",
        Template: "The literal '{0}' is outside the range of '{1}'.",
        Description: "A numeric literal does not fit the type given by its suffix or by its target. Use a wider type, or change the literal.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    // ── Target-typed new ─────────────────────────────────────────────────

    public static readonly MistDiagnostic TargetTypedNewNeedsTargetType = new(
        DiagnosticNumber.Mint("MIST0378"),
        Title: "Target-typed new without target",
        Template: "The type of 'new(...)' cannot be determined at this position. Name it, as in 'new Point(...)'.",
        Description: "A 'new(...)' expression sits where nothing supplies the type to construct. This is also reported when several overloads accept the argument count, since the parameter that would supply the target cannot be chosen without the argument types and this is one of them; name the type explicitly.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic DefaultNeedsTargetType = new(
        DiagnosticNumber.Mint("MIST0379"),
        Title: "Default without target type",
        Template: "The type of 'default' cannot be determined at this position. Name it, as in 'default(int)'.",
        Description: "A bare 'default' appears where nothing supplies the type whose default value is meant. Write the type, as in 'default(int)', or place the expression where a target type is known.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic TargetTypeNotConstructible = new(
        DiagnosticNumber.Mint("MIST0380"),
        Title: "Target type not constructible",
        Template: "'{0}' cannot be constructed, so 'new(...)' has nothing to create here. Only a class or a struct can be.",
        Description: "A 'new(...)' expression is targeted at a type that has no constructors to run, such as an abstract, an enum or a function type. Target a class or struct, or construct a concrete type that conforms to the target.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    // ── Object initializers ──────────────────────────────────────────────

    public static readonly MistDiagnostic InitializerTargetNotAssignable = new(
        DiagnosticNumber.Mint("MIST0381"),
        Title: "Initializer target not assignable",
        Template: "'{1}' on '{0}' is not a field or a property, so an initializer block cannot assign to it.",
        Description: "An object initializer assigns to a member that is a method, nested type or other non-storage member. Assign only to fields and to properties with a setter or init accessor.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic InitializerTargetNotInstance = new(
        DiagnosticNumber.Mint("MIST0382"),
        Title: "Initializer target is static",
        Template: "'{1}' on '{0}' is static, so an initializer block cannot assign to it.",
        Description: "An object initializer assigns to a static member, but an initializer sets state on the instance just constructed. Assign the static member in a separate statement, or make the member an instance member.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic CollectionInitializerNotSupported = new(
        DiagnosticNumber.Mint("MIST0383"),
        Title: "Collection initializer not supported",
        Template: "An initializer block element must name a member and assign to it, as in 'Count = 1'. Collection initializers are not supported by the experimental pipeline; add the elements after construction.",
        Description: "An initializer block contains a bare expression, which is the collection-initializer form that stands for a call to an 'Add' overload the model never resolved, so emitting it would hand the backend a member nobody chose. Write 'Member = value' entries instead, and add collection elements after construction.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic DuplicateAssemblyName = new(
        DiagnosticNumber.Mint("MIST0384"),
        Title: "Duplicate assembly name",
        Template: "Assembly name '{0}' is used by multiple projects in this compilation. Each referenced project must have a unique name.",
        Description: "Two projects in one compilation produce assemblies with the same name, and since a Mist program refers to an assembly by name, references between them would be ambiguous with no way to disambiguate. Give each project a unique assembly name.",
        Category: DiagnosticCategory.Dep,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic NoApplicableOverload = new(
        DiagnosticNumber.Mint("MIST0385"),
        Title: "No overload with that arity",
        Template: "There is no overload of {0} accepting {1} argument(s). Declared arities: {2}.",
        Description: "A call passes a number of arguments that no overload of the named member accepts; counting parameters needs no conversion relation, so this is decided outright. Adjust the argument list to one of the declared arities.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic AmbiguousOverload = new(
        DiagnosticNumber.Mint("MIST0386"),
        Title: "Ambiguous overload",
        Template: "The call to {0} is ambiguous. These overloads are equally applicable: {1}. Cast an argument to choose one.",
        Description: "Several overloads are applicable to the call and none is better than the rest, so choosing would be arbitrary; the tie is reported only among candidates already proven applicable, which is what makes it safe to decide from a partial conversion relation. Cast an argument so that one overload is preferred.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic MethodGroupNotConverted = new(
        DiagnosticNumber.Mint("MIST0387"),
        Title: "Method group not converted",
        Template: "'{0}' names a method group, which is only valid where it is invoked or where a function type is expected whose signature one of its overloads has.",
        Description: "A method group reached a position that neither invokes it nor converts it to a function type whose signature one of its overloads has, including a generic overload the target leaves open. Call the method, or assign it to a function type that one overload matches.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic TypeArgumentsOnANonMethod = new(
        DiagnosticNumber.Mint("MIST0388"),
        Title: "Type arguments on non-method",
        Template: "'{0}' is not a method, so it takes no type arguments.",
        Description: "An expression supplies type arguments to something that is not a method, such as a field, property or local. Remove the type argument list, or apply it to the generic method you meant.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic MethodGroupAmbiguous = new(
        DiagnosticNumber.Mint("MIST0389"),
        Title: "Ambiguous method group conversion",
        Template: "'{0}' names more than one overload with the target function type, and none is more specific than the rest.",
        Description: "More than one overload of a method group matches the target function type and none is more specific than the others. Cast to a function type that singles one out, or wrap the call in a lambda.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic UnresolvableOverload = new(
        DiagnosticNumber.Mint("MIST0390"),
        Title: "No convertible overload",
        Template: "The call to {0} matches no overload. No conversion is known from the given arguments to any of: {1}.",
        Description: "Several overloads accept the argument count but no conversion is known from the given arguments to any of them. The compiler declines to guess, since with every candidate eliminated it has nothing to choose on; convert the arguments to the parameter types of the overload you intend.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic TypeArgumentsNotInferred = new(
        DiagnosticNumber.Mint("MIST0391"),
        Title: "Type arguments not inferred",
        Template: "Cannot infer the type arguments of {0} from the arguments. State all of them explicitly at the call: {1}.",
        Description: "A call to a generic method leaves at least one type parameter undetermined by the arguments; binding anyway would emit code naming a type parameter where a type belongs and move the error onto a generated file. State every type argument explicitly at the call.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic TypeArgumentsConflict = new(
        DiagnosticNumber.Mint("MIST0392"),
        Title: "Conflicting type argument inference",
        Template: "Cannot infer the type arguments of {0}: the arguments give {1} the types {2}, and none of them is a type the others all convert to. State all of them explicitly at the call: {3}.",
        Description: "The arguments of a generic call give one type parameter several different types, none of which the others all convert to. State the type arguments explicitly at the call, or convert the arguments to a single type.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic TypeArgumentCountMismatch = new(
        DiagnosticNumber.Mint("MIST0393"),
        Title: "Type argument count mismatch",
        Template: "The call to {0} states the wrong number of type arguments. Declared type parameter counts: {1}.",
        Description: "A call states a number of type arguments that no overload of the method declares. Supply exactly as many as the overload you intend has type parameters.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    // ── The unknown top type ─────────────────────────────────────────────

    public static readonly MistDiagnostic UnknownAtStoragePosition = new(
        DiagnosticNumber.Mint("MIST0394"),
        Title: "Unknown as storage type",
        Template: "{0} cannot have type '{1}': 'unknown' is not permitted as the declared type of writable storage. Use a concrete type, or accept 'unknown' as a parameter and pattern-match it.",
        Description: "Writable storage, directly or through a type argument, union constituent or tuple element, is declared with type 'unknown'. Storage is the one position that would let a value leave the top type without a pattern, and a getter-only property is refused too because the declared type decides what the slot admits; declare a concrete type, or take 'unknown' as a parameter and pattern-match it.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic UnknownViewHidesWriter = new(
        DiagnosticNumber.Mint("MIST0395"),
        Title: "Unknown view hides writer",
        Template: "'{1}' cannot be {2} on '{0}': it would write to storage typed by '{3}', which '{0}' closes with 'unknown'. Narrow the receiver with an 'is' pattern to a concrete construction first.",
        Description: "A member exists on the receiver but is hidden because the receiver closes a type parameter with 'unknown' while the runtime object holds a concrete argument, so the write would store into concretely typed storage through a top-typed view. Narrow the receiver with an 'is' pattern to a concrete construction before writing.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    // ── Retroactive conformance ──────────────────────────────────────────

    public static readonly MistDiagnostic ConformanceSourceNotConcrete = new(
        DiagnosticNumber.Mint("MIST0396"),
        Title: "Conformance subject not concrete",
        Template: "'{0}' cannot be declared to conform retroactively: only a class or a struct can be the subject of a with block.",
        Description: "A 'with' block declares retroactive conformance for a type that is not a class or struct. An abstract declares its conformances in its own base list and no other kind of type has members to map, so apply the block to a class or struct.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ConformanceTargetNotAbstract = new(
        DiagnosticNumber.Mint("MIST0397"),
        Title: "Conformance target not abstract",
        Template: "'{0}' is not an abstract type, so '{1}' cannot be declared to conform to it.",
        Description: "A 'with' block declares conformance to a type that is not an abstract, and only an abstract has slots to conform to. Name an abstract type, or use inheritance if a concrete base was meant.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic EnumVariantValueNotInferable = new(
        DiagnosticNumber.Mint("MIST0398"),
        Title: "Enum variant value not inferable",
        Template: "Enum variant '{0}' needs an explicit value: none can be inferred over base type '{1}'. Only a string base, which uses the variant name, and an integral base, which counts up from the previous variant, can supply one.",
        Description: "An enum variant has no explicit value and its base type offers no rule to supply one; only a string base, which uses the variant's name, and an integral base, which counts up from the previous variant, can. Give the variant an explicit value, since every later stage depends on it having one.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic EnumVariantValueNotCountable = new(
        DiagnosticNumber.Mint("MIST0399"),
        Title: "Enum variant value not countable",
        Template: "Enum variant '{0}' needs an explicit value: the previous variant's value is not a compile-time constant this phase can count on from.",
        Description: "An enum variant has no explicit value and the previous variant's value is not a constant this phase can fold, such as a const reference or arithmetic. The compiler refuses rather than restarting the count at zero, which would silently merge variants into aliases; give this variant an explicit value.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic EnumVariantValueOutOfRange = new(
        DiagnosticNumber.Mint("MIST0400"),
        Title: "Enum variant value out of range",
        Template: "Enum variant '{0}' would take inferred value {1}, which does not fit base type '{2}'. Give it an explicit value, or widen the base type.",
        Description: "Counting up from the previous variant produced a value that does not fit the enum's base type; it is reported in Mist terms because the author wrote no number here. Give the variant an explicit value, or widen the base type.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ConformanceUnresolvedSlot = new(
        DiagnosticNumber.Mint("MIST0401"),
        Title: "Unknown conformance slot",
        Template: "Abstract type '{0}' declares no member named '{1}'.",
        Description: "A 'with' block maps a slot that the abstract type does not declare. Check the slot name against the abstract's members.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ConformanceMappingUnsatisfied = new(
        DiagnosticNumber.Mint("MIST0402"),
        Title: "Conformance mapping unsatisfied",
        Template: "No member named '{0}' on '{1}' can answer slot '{2}' of '{3}'. A mapping needs a member of the same kind, and for a method one taking the same number of parameters.",
        Description: "A 'with' block maps an abstract's slot to a member of the conforming type, but no member of that name has the same kind and, for a method, the same parameter count; one diagnostic covers every way the two ends fail to pair since the remedy is the same. Point the mapping at a member that matches the slot, or add one.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic AsConversionNoRelationship = new(
        DiagnosticNumber.Mint("MIST0403"),
        Title: "As conversion has no relationship",
        Template: "A value of type '{0}' can never be '{1}', so 'as' has no result type to produce. One must contain the other, or the source must be a union or unknown holding it; a conversion is not enough, since 'as' answers the value rather than a copy of it.",
        Description: "An 'as' names a target type that the source can never be, in either direction, so the expression has no result type, neither the bare type of a provable conversion nor the optional of a checked one. This also covers a void end and a newtype tested against its source; make one type contain the other, or test a union or unknown that can hold the target.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic MultipleUnbracketedPacks = new(
        DiagnosticNumber.Mint("MIST0404"),
        Title: "Multiple bracket-free packs",
        Template: "'{0}' is a second bracket-free pack after '{1}'. Only one pack can absorb a flat argument list; declare this one as 'Type[]:{0}' and supply it in brackets.",
        Description: "A type parameter list declares two packs without brackets, and two of them could divide a flat argument list any number of ways, so the list is ambiguous by construction. Declare the second pack with a 'Type[]' domain and supply its arguments in brackets.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Related: [DiagnosticNumber.Mint("MIST0405")]);

    public static readonly MistDiagnostic MultipleParamsParameters = new(
        DiagnosticNumber.Mint("MIST0405"),
        Title: "Multiple params parameters",
        Template: "'{0}' is a second 'params' parameter after '{1}'. Only one parameter of a value-parameter list can absorb a flat argument list; declare this one without 'params' and pass it as one tuple or array.",
        Description: "A parameter list declares a second 'params' parameter, but a caller's flat argument list can be absorbed by only one parameter whichever of a tuple or an array collects it; the variadics spec states this and the pack rule as one rule. Declare the second parameter without 'params' and pass it as a single tuple or array.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Related: [DiagnosticNumber.Mint("MIST0404")]);

    public static readonly MistDiagnostic PackInTypePosition = new(
        DiagnosticNumber.Mint("MIST0406"),
        Title: "Pack used as single type",
        Template: "'{0}' is a pack, a list of types, and this position needs one type. Spread it with '{0}...' where a list is accepted: a type-argument list, a tuple type, or a member declaration.",
        Description: "A pack, which is a list of types, is written where exactly one type is needed. Spread it with '...' in a position that accepts a list, such as a type-argument list, a tuple type or a member declaration.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic DisallowedDependentDomain = new(
        DiagnosticNumber.Mint("MIST0407"),
        Title: "Disallowed dependent domain",
        Template: "Type '{0}' cannot be used as a dependent type parameter. Only primitive integral types, bool, and char are allowed.",
        Description: "A dependent type parameter is declared over a type whose values cannot carry type identity. A construction is identified by its argument, so the domain's equality must be total and exact, which excludes the floating-point types however convenient they look; use a primitive integral type, bool or char.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic DuplicateTypeParameter = new(
        DiagnosticNumber.Mint("MIST0408"),
        Title: "Duplicate type parameter",
        Template: "Type parameter '{0}' is declared more than once in the same type parameter list.",
        Description: "A type parameter list declares the same name twice. Rename one of the parameters.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic UnprovenPackIndex = new(
        DiagnosticNumber.Mint("MIST0409"),
        Title: "Unproven pack index",
        Template: "'{0}[{1}]' indexes a pack whose length is not known here, so it may hold no element {1}. Only a pack declared with a fixed size, 'Type[N]:{0}', or the binding of a guard proving its size, 'if ({0} is Type[N] fixed)', can be indexed in a type position.",
        Description: "A pack is indexed in a type position where its length is not fixed; a declaration is judged for every instantiation, including the empty pack, so no index is provably inside a 'Type[]' pack. Declare the pack with a fixed size 'Type[N]', or index it under a guard that proves the size.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic PackIndexOutOfBounds = new(
        DiagnosticNumber.Mint("MIST0410"),
        Title: "Pack index out of bounds",
        Template: "'{0}[{1}]' is outside the pack: '{0}' is declared to hold exactly {2} element(s), indexed from 0.",
        Description: "A pack is indexed at a position at or beyond the fixed number of elements it is declared to hold, counted from zero. Use an index below the declared size.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic TooFewTypeArgumentsForPack = new(
        DiagnosticNumber.Mint("MIST0411"),
        Title: "Too few type arguments",
        Template: "'{0}' needs at least {1} type arguments to fill the slots around its pack, but {2} were given.",
        Description: "A flat type-argument list is too short to fill the fixed slots around a pack even with the pack absorbing nothing; a pack makes the count flexible upward only. Supply at least one argument for each non-pack slot.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic SpreadStemCollision = new(
        DiagnosticNumber.Mint("MIST0412"),
        Title: "Spread member name collision",
        Template: "'{0}' collides with the spread member '{1}', which owns that name and every '{1}' followed by an index.",
        Description: "A member's name falls inside the family a spread member owns, which is the aggregate and every indexed projection of it. How many projections exist is unknown where the declaration is written, so the whole prefixed name space is reserved; rename the member.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ParamsArrayNotLast = new(
        DiagnosticNumber.Mint("MIST0413"),
        Title: "Params parameter not last",
        Template: "'{0}' is declared 'params' but is not the last parameter. A 'params' collection collects the arguments after the fixed ones, so nothing may follow it.",
        Description: "A homogeneous 'params' collection parameter is followed by other parameters, but it gathers the arguments that run past the fixed ones, which only the last slot can do; a value pack, which binds inward from both ends, may sit anywhere. This is a rule of Mist rather than of any backend, so move the 'params' parameter to the end.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ParamsParameterMalformed = new(
        DiagnosticNumber.Mint("MIST0414"),
        Title: "Malformed params parameter",
        Template: "'{0}' is declared 'params' but {1}. A 'params' parameter is a value pack, 'params TArgs {0}', or a collection a flat list is gathered into -- an array, 'params T[] {0}', a span, a list, or an enumerable interface -- passed by value.",
        Description: "A parameter is marked 'params' but its type or passing mode is not one a flat argument list can be gathered into. Declare it as a value pack, or as a by-value array, span, list or enumerable collection.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic UnprovableSpread = new(
        DiagnosticNumber.Mint("MIST0415"),
        Title: "Unprovable spread",
        Template: "'{0}' cannot be spread here: its length is not known at this point, so the number of arguments it stands for is not either.",
        Description: "A '...' spread is applied to an operand whose length is not known at this point, but the spread promises a definite number of arguments and the call was checked against that promise. Spread a pack or value whose arity is fixed here, or prove its size with a guard first.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic AnonymousMemberNotNameable = new(
        DiagnosticNumber.Mint("MIST0416"),
        Title: "Unnamed anonymous type member",
        Template: "This anonymous type member has no name. Write one with 'Name = ', or project something that ends in a name.",
        Description: "An anonymous type projects an expression that ends in no name, such as a call, so the member has nothing to be called; a projection names its member only by borrowing the name it ends in. Write 'Name = expression', or project a field, property or variable.",
        Category: DiagnosticCategory.Syn,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic AbstractConstructed = new(
        DiagnosticNumber.Mint("MIST0417"),
        Title: "Abstract type constructed",
        Template: "'{0}' is an abstract and cannot be constructed. Construct a class or a struct that conforms to it.",
        Description: "A 'new' expression names an abstract type, which has no constructor to run because it describes a contract rather than a value. Construct a class or struct that conforms to it.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic NewtypeCloneInvalid = new(
        DiagnosticNumber.Mint("MIST0418"),
        Title: "Invalid newtype clone",
        Template: "'{0}' cannot clone '{1}', whose member '{2}' does not hold for '{0}': {3}",
        Description: "A newtype clones a source type whose member no longer holds once the source's name means the newtype, for example a body passing 'this' where the source type is required. The source itself is not wrong, so the error is reported at the newtype; adjust the source member so it stays valid under the newtype, or choose a different source.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic NewtypeSourceNotSupported = new(
        DiagnosticNumber.Mint("MIST0419"),
        Title: "Unsupported newtype source",
        Template: "A newtype over '{0}' is not supported: a newtype clones a class or a struct declared in Mist.",
        Description: "A newtype names a source that is not a class or struct declared in Mist, and a newtype works by cloning its source's Mist members. Clone a Mist class or struct instead.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);
}