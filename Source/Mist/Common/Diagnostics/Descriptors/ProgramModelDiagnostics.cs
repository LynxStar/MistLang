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
        Template: "Internal compiler error: unexpected namespace declaration node type '{nodeKind}'.",
        Description: "The program model builder met a namespace declaration whose node type it does not handle, which no valid source can produce. Report this as a compiler bug together with the source that triggered it.",
        Category: DiagnosticCategory.Ice,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("nodeKind", SlotKind.Name)]);

    public static readonly MistDiagnostic UnknownTypeDeclaration = new(
        DiagnosticNumber.Mint("MIST0289"),
        Title: "Unrecognized type declaration node",
        Template: "Internal compiler error: unrecognized type declaration node '{nodeKind}'.",
        Description: "Type registration met a declaration node of a kind the model builder was never taught, which no valid source can produce. Report this as a compiler bug together with the source that triggered it.",
        Category: DiagnosticCategory.Ice,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("nodeKind", SlotKind.Name)]);

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
        Template: "The namespace '{namespace}' could not be found.",
        Description: "A using directive or qualified name refers to a namespace that no project or referenced assembly in the compilation declares. Check the spelling, or add the reference that supplies it.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("namespace", SlotKind.Name)]);

    public static readonly MistDiagnostic GlobalUsingUndefinedNamespace = new(
        DiagnosticNumber.Mint("MIST0294"),
        Title: "Global using names unknown namespace",
        Template: "Global using namespace '{namespace}' in project '{project}' could not be found, so it imports nothing.",
        Description: "A project's global using list names a namespace that nothing in the compilation declares, so the entry brings no names into scope. Correct the entry in the project configuration or remove it.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Promotable,
        DefaultSeverity: DiagnosticSeverity.Warning,
        Slots: [new Slot("namespace", SlotKind.Name), new Slot("project", SlotKind.Name)]);

    // ── Type resolution ──────────────────────────────────────────────────

    public static readonly MistDiagnostic DuplicateTypeName = new(
        DiagnosticNumber.Mint("MIST0295"),
        Title: "Duplicate type name",
        Template: "A type named '{name}' already exists in namespace '{namespace}'.",
        Description: "Two type declarations in the same namespace share a name, so references to that name would have no single target. Rename one of them or move it to a different namespace.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name), new Slot("namespace", SlotKind.Name)]);

    public static readonly MistDiagnostic UnresolvedIdentifier = new(
        DiagnosticNumber.Mint("MIST0296"),
        Title: "Unresolved identifier",
        Template: "The name '{name}' could not be found in this scope.",
        Description: "A name in expression position matched nothing in any reachable scope. This covers a misspelling, a name that is not in scope, and an instance member named from a static context, which is excluded from lookup rather than matched and rejected; check the spelling and scope, or qualify the name with an instance.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name)]);

    public static readonly MistDiagnostic AwaitOfNonTask = new(
        DiagnosticNumber.Mint("MIST0297"),
        Title: "Await of non-task",
        Template: "'await' requires a task, but '{type}' is not one.",
        Description: "The operand of 'await' is not a task, so it has no result to unwrap; this is a rule of the language rather than of any backend. Await a task-valued expression, or drop the 'await'.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic UnresolvedType = new(
        DiagnosticNumber.Mint("MIST0298"),
        Title: "Unresolved type",
        Template: "The type '{name}' could not be found.",
        Description: "A type reference names a type that no reachable scope declares or imports. Check the spelling, add the using directive that brings it into scope, or reference the project that declares it.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name)]);

    public static readonly MistDiagnostic AmbiguousType = new(
        DiagnosticNumber.Mint("MIST0299"),
        Title: "Ambiguous type name",
        Template: "The type name '{name}' is ambiguous between imported namespaces '{firstNamespace}' and '{secondNamespace}'. Use a fully-qualified name to disambiguate.",
        Description: "A type name resolves to distinct types in two imported namespaces at the same scope level, so the reference has no single meaning. Qualify the name fully, or remove one of the imports.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name), new Slot("firstNamespace", SlotKind.Name), new Slot("secondNamespace", SlotKind.Name)]);

    public static readonly MistDiagnostic CyclicTypeAlias = new(
        DiagnosticNumber.Mint("MIST0300"),
        Title: "Cyclic type alias",
        Template: "Type alias '{name}' forms a cycle.",
        Description: "A type alias expands, directly or through other aliases, back to itself, so it names no finite target. It is reported once at the declaration where the expansion re-entered itself and the alias is treated as an error type; break the cycle by making one alias name a concrete type.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name)]);

    public static readonly MistDiagnostic UnresolvedMember = new(
        DiagnosticNumber.Mint("MIST0301"),
        Title: "Unresolved member",
        Template: "'{receiver}' has no member named '{name}'.",
        Description: "A member access names something that neither the receiver's type nor any of its bases declares. Check the member name, or confirm the receiver has the type you expect.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("receiver", SlotKind.Type), new Slot("name", SlotKind.Name)]);

    public static readonly MistDiagnostic AmbiguousMember = new(
        DiagnosticNumber.Mint("MIST0302"),
        Title: "Ambiguous member",
        Template: "The member '{name}' on '{receiver}' is ambiguous between {candidates}.",
        Description: "A member name matched several equally close declarations on the receiver, so no single one answers it. Qualify the access through the declaring type, or rename one of the colliding members.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("receiver", SlotKind.Type), new Slot("name", SlotKind.Name), new Slot("candidates", SlotKind.Sequence, SlotKind.Symbol)]);

    public static readonly MistDiagnostic AmbiguousIdentifier = new(
        DiagnosticNumber.Mint("MIST0303"),
        Title: "Ambiguous identifier",
        Template: "The identifier '{name}' is ambiguous between {candidates}.",
        Description: "A bare name matches more than one declaration at the same scope distance, so the reference cannot pick one. Qualify the name with its declaring type or namespace, or remove the import that introduces the duplicate.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name), new Slot("candidates", SlotKind.Sequence, SlotKind.Symbol)]);

    public static readonly MistDiagnostic UnknownTypeRefNode = new(
        DiagnosticNumber.Mint("MIST0304"),
        Title: "Unrecognized type reference node",
        Template: "Internal compiler error: unrecognized type reference node '{nodeKind}'.",
        Description: "Type resolution met a type reference node of a kind it does not handle, which no valid source can produce. Report this as a compiler bug together with the source that triggered it.",
        Category: DiagnosticCategory.Ice,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("nodeKind", SlotKind.Name)]);

    public static readonly MistDiagnostic ReservedTupleElementName = new(
        DiagnosticNumber.Mint("MIST0305"),
        Title: "Reserved tuple element name",
        Template: "'{name}' is a reserved tuple element name; it collides with the positional element accessors. Choose a different name.",
        Description: "A tuple element is given a name of the form the positional accessors already use, such as Item1, so the explicit name would shadow the positional one. Pick a name outside the reserved positional pattern.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name)]);

    // ── Base type resolution ─────────────────────────────────────────────

    public static readonly MistDiagnostic BaseTypeKindMismatch = new(
        DiagnosticNumber.Mint("MIST0306"),
        Title: "Base type kind mismatch",
        Template: "A {ownerKind} cannot inherit from a {baseKind} ('{type}').",
        Description: "A type's base list names a type of a kind it cannot derive from, for example a struct deriving from a class. Remove the entry, or change the declaration to a kind that can derive from it.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("ownerKind", SlotKind.Enum), new Slot("baseKind", SlotKind.Enum), new Slot("type", SlotKind.Name)]);

    public static readonly MistDiagnostic ConcreteInAbstractList = new(
        DiagnosticNumber.Mint("MIST0307"),
        Title: "Concrete type in abstract list",
        Template: "A concrete type ('{type}') cannot appear in the abstract implementations list. Only the first entry may be a concrete base type.",
        Description: "A type's base list places a class or struct after the first position, where only abstract types may appear; a type has at most one concrete base and it must come first. Move the concrete base to the front or remove it.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Name)]);

    public static readonly MistDiagnostic InvalidBaseType = new(
        DiagnosticNumber.Mint("MIST0308"),
        Title: "Invalid base type",
        Template: "'{type}' is not an abstract and cannot be used as a base type.",
        Description: "A base list entry names a type that is not an abstract and so cannot be conformed to, such as an enum or an alias of a primitive. Replace it with the abstract the declaration should conform to, or with a class it should derive from.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Name)]);

    // ── Member resolution ────────────────────────────────────────────────

    public static readonly MistDiagnostic GetterCannotHaveModifier = new(
        DiagnosticNumber.Mint("MIST0309"),
        Title: "Modifier on getter",
        Template: "The getter on property '{member}' cannot have a modifier.",
        Description: "A property's getter carries an accessibility modifier, but a getter always shares the accessibility of its property. Remove the modifier from the getter; narrow the setter instead if the two must differ.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name)]);

    public static readonly MistDiagnostic SetterModifierMustBePrivate = new(
        DiagnosticNumber.Mint("MIST0310"),
        Title: "Non-private setter modifier",
        Template: "Only 'private' is permitted as a modifier on the setter of property '{member}'.",
        Description: "A setter carries an accessibility modifier other than 'private', and 'private' is the only narrowing a setter may declare relative to its property. Change the modifier to 'private' or remove it.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name)]);

    public static readonly MistDiagnostic EmptyAccessorList = new(
        DiagnosticNumber.Mint("MIST0311"),
        Title: "Empty accessor list",
        Template: "'{member}' must declare at least a getter or a setter.",
        Description: "A property or indexer declares an accessor block with no getter and no setter in it, so it could be neither read nor written. Add at least one accessor.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name)]);

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
        Template: "Accessors on abstract property '{member}' cannot have modifiers.",
        Description: "An accessor of a property declared on an abstract type carries a modifier, but abstract members state a contract and the conforming type decides accessibility. Remove the modifier.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name)]);

    public static readonly MistDiagnostic AbstractPropertyInitAccessor = new(
        DiagnosticNumber.Mint("MIST0314"),
        Title: "Init accessor on abstract property",
        Template: "Abstract types cannot declare init accessors on property '{member}' because abstract types cannot be instantiated.",
        Description: "A property on an abstract type declares an init accessor, but init runs during construction and an abstract type is never constructed. Replace it with a setter or make the property getter-only.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name)]);

    public static readonly MistDiagnostic StaticPropertyInitAccessor = new(
        DiagnosticNumber.Mint("MIST0315"),
        Title: "Init accessor on static property",
        Template: "Static property '{member}' cannot declare an init accessor: init runs while an instance is constructed, and a static property belongs to no instance.",
        Description: "A static property declares an init accessor, which only has meaning while a particular instance is being constructed. Use a setter instead.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name)]);

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
        Template: "'{modifier}' is not valid on {declarationKind}.{hint}",
        Description: "A purity or scope modifier such as 'pure' or 'local' appears on a kind of declaration that cannot carry it. Remove the modifier, or move it to a declaration kind that accepts it.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("modifier", SlotKind.Enum), new Slot("declarationKind", SlotKind.Text), new Slot("hint", SlotKind.Text)]);

    public static readonly MistDiagnostic PureRequiresFunctionType = new(
        DiagnosticNumber.Mint("MIST0322"),
        Title: "Pure on non-function type",
        Template: "'pure' can only qualify a function type, and '{type}' is not one.",
        Description: "A type is qualified with 'pure', but purity is a property of callables and only a function type such as 'func<int, bool>' can carry it. Remove the qualifier, or apply it to the function type you meant.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type)]);

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
        Template: "The 'intrinsic' modifier is not valid on {memberKind}.",
        Description: "The 'intrinsic' modifier marks a member of a kind the backend cannot supply a definition for. Remove the modifier and give the member an ordinary definition.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("memberKind", SlotKind.Text)]);

    public static readonly MistDiagnostic MethodMustHaveBody = new(
        DiagnosticNumber.Mint("MIST0325"),
        Title: "Method without body",
        Template: "Method '{member}' must have a body unless it is declared 'intrinsic' or is on an abstract type.",
        Description: "A method on a concrete type ends in ';' with no body, and only abstract members and intrinsic members may omit one. Add a body, or mark the member 'intrinsic' if it belongs to the Standard library and the backend supplies it.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name)]);

    public static readonly MistDiagnostic IntrinsicMemberCannotHaveBody = new(
        DiagnosticNumber.Mint("MIST0326"),
        Title: "Body on intrinsic member",
        Template: "Intrinsic member '{member}' must not have a body; use ';' to mark it as backend-provided.",
        Description: "A member marked 'intrinsic' also has a body, but the modifier says the backend provides the implementation. Remove the body or the modifier.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name)]);

    public static readonly MistDiagnostic IntrinsicReservedForStandard = new(
        DiagnosticNumber.Mint("MIST0327"),
        Title: "Intrinsic outside Standard library",
        Template: "The 'intrinsic' modifier on '{type}' is reserved for the Standard library. It says the backend supplies the declaration, and a backend has nothing to supply for a type declared outside it.",
        Description: "A type outside the Standard library uses 'intrinsic', which asks the backend to supply a declaration it only knows how to supply for the Standard library. Remove the modifier and implement the member in Mist.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Name)]);

    public static readonly MistDiagnostic IntrinsicRequiresIntrinsicType = new(
        DiagnosticNumber.Mint("MIST0328"),
        Title: "Intrinsic member in non-intrinsic type",
        Template: "Member-level 'intrinsic' is only permitted inside a type that is itself declared 'intrinsic'. Type '{type}' is not intrinsic.",
        Description: "A member is marked 'intrinsic' inside a type that is not itself intrinsic, and the backend binds intrinsic members only through an intrinsic type. Mark the enclosing type 'intrinsic' or give the member a Mist body.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Name)]);

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
        Template: "Operator expects {expected} parameter(s) but has {actual}.",
        Description: "An operator declares a number of parameters that does not match its kind, for example a binary operator with one parameter. Give it exactly the parameters its kind requires.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("expected", SlotKind.Sequence, SlotKind.Quantity), new Slot("actual", SlotKind.Quantity)]);

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
        Template: "Type '{type}' declares more than one default block; only one is permitted per type.",
        Description: "A type contains two default blocks, and the defaults a type supplies live in a single block. Merge them into one.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Name)]);

    // ── Constraint resolution ────────────────────────────────────────────

    public static readonly MistDiagnostic UnresolvedConstraintTarget = new(
        DiagnosticNumber.Mint("MIST0338"),
        Title: "Unknown constraint target",
        Template: "The constraint clause references type parameter '{name}', which is not declared on this type or method.",
        Description: "A where clause constrains a type parameter that the enclosing type or method does not declare. Check the spelling, or add the type parameter to the declaration.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name)]);

    public static readonly MistDiagnostic ConflictingConstraints = new(
        DiagnosticNumber.Mint("MIST0339"),
        Title: "Conflicting constraints",
        Template: "Type parameter '{parameter}' has conflicting constraints: '{first}' and '{second}' are mutually exclusive.",
        Description: "A type parameter's where clause combines constraints that no type can satisfy at once. Remove one of them.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("parameter", SlotKind.Name), new Slot("first", SlotKind.Enum), new Slot("second", SlotKind.Enum)]);

    public static readonly MistDiagnostic DuplicateConstraint = new(
        DiagnosticNumber.Mint("MIST0340"),
        Title: "Duplicate constraint",
        Template: "Type parameter '{parameter}' has duplicate '{constraint}' constraint; the redundant occurrence has no effect.",
        Description: "A where clause repeats the same constraint keyword on one type parameter, which changes nothing. Delete the repeated occurrence.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Promotable,
        DefaultSeverity: DiagnosticSeverity.Warning,
        Slots: [new Slot("parameter", SlotKind.Name), new Slot("constraint", SlotKind.Enum)]);

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
        Template: "Extension property '{name}' cannot use auto-accessors (get; / set;); extension properties have no backing field.",
        Description: "An extension property uses 'get;' or 'set;' without a body, but an extension adds no storage to its receiver for an auto-accessor to use. Write a body or an expression for each accessor.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name)]);

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
        Template: "A member cannot have the same name as its enclosing type '{type}'. Rename the member.",
        Description: "A member has the same name as the type that declares it. The backend rejects such a declaration outright, and inside the type the bare name would resolve to the member where a type was meant, so rename the member.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Name)]);
}