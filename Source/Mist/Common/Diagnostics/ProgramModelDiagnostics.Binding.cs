namespace Mist.Common.Diagnostics;

public static partial class ProgramModelDiagnostics
{
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


    public static readonly MistDiagnostic ForEachElementNotInferable = new(
        DiagnosticNumber.Mint("MIST0373"),
        Title: "Foreach element type not inferable",
        Template: "Cannot infer the element type of '{type}' because it is not an array and conforms to no IEnumerable. Iterate a sequence, or state the element type in place of 'var'.",
        Description: "A 'foreach' declares its variable with 'var' over a collection that is not an array and conforms to no IEnumerable, so there is no element type to give the variable; the binder cannot finish the loop without one, since every read of the variable resolves against that type. Iterate a sequence, or write the element type in place of 'var'.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic RefLocalNeedsInitializer = new(
        DiagnosticNumber.Mint("MIST0374"),
        Title: "Ref local without initializer",
        Template: "Ref local '{name}' must be declared with an initializer: a reference names the storage it aliases where it is declared.",
        Description: "A 'ref' local is declared without an initializer, but a reference binds to the storage it aliases at the point of declaration and cannot be left dangling. Initialize it with the variable or element it should refer to.",
        Category: DiagnosticCategory.Own,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name)]);

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
        Template: "The elements of the array literal have no common type ('{first}' and '{second}'). Annotate the target with an array type to select the element type explicitly.",
        Description: "The elements of an array literal have different types and no target type selects an element type; the compiler does not yet compute a best common type by conversion or numeric promotion, so it asks rather than guesses. Annotate the target with the array type you want.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("first", SlotKind.Type), new Slot("second", SlotKind.Type)]);

    public static readonly MistDiagnostic NumericLiteralOutOfRange = new(
        DiagnosticNumber.Mint("MIST0377"),
        Title: "Numeric literal out of range",
        Template: "The literal '{literal}' is outside the range of '{type}'.",
        Description: "A numeric literal does not fit the type given by its suffix or by its target. Use a wider type, or change the literal.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("literal", SlotKind.Text), new Slot("type", SlotKind.Type)]);


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
        Template: "'{type}' cannot be constructed, so 'new(...)' has nothing to create here. Only a class or a struct can be.",
        Description: "A 'new(...)' expression is targeted at a type that has no constructors to run, such as an abstract, an enum or a function type. Target a class or struct, or construct a concrete type that conforms to the target.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type)]);


    public static readonly MistDiagnostic InitializerTargetNotAssignable = new(
        DiagnosticNumber.Mint("MIST0381"),
        Title: "Initializer target not assignable",
        Template: "'{member}' on '{type}' is not a field or a property, so an initializer block cannot assign to it.",
        Description: "An object initializer assigns to a member that is a method, nested type or other non-storage member. Assign only to fields and to properties with a setter or init accessor.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("member", SlotKind.Name)]);

    public static readonly MistDiagnostic InitializerTargetNotInstance = new(
        DiagnosticNumber.Mint("MIST0382"),
        Title: "Initializer target is static",
        Template: "'{member}' on '{type}' is static, so an initializer block cannot assign to it.",
        Description: "An object initializer assigns to a static member, but an initializer sets state on the instance just constructed. Assign the static member in a separate statement, or make the member an instance member.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("member", SlotKind.Name)]);

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
        Template: "Assembly name '{name}' is used by multiple projects in this compilation. Each referenced project must have a unique name.",
        Description: "Two projects in one compilation produce assemblies with the same name, and since a Mist program refers to an assembly by name, references between them would be ambiguous with no way to disambiguate. Give each project a unique assembly name.",
        Category: DiagnosticCategory.Dep,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name)]);

    public static readonly MistDiagnostic NoApplicableOverload = new(
        DiagnosticNumber.Mint("MIST0385"),
        Title: "No overload with that arity",
        Template: "There is no overload of {callee} accepting {count} argument(s). Declared arities: {arities}.",
        Description: "A call passes a number of arguments that no overload of the named member accepts; counting parameters needs no conversion relation, so this is decided outright. Adjust the argument list to one of the declared arities.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("callee", SlotKind.Symbol), new Slot("count", SlotKind.Quantity), new Slot("arities", SlotKind.Sequence, SlotKind.Quantity)]);

    public static readonly MistDiagnostic AmbiguousOverload = new(
        DiagnosticNumber.Mint("MIST0386"),
        Title: "Ambiguous overload",
        Template: "The call to {callee} is ambiguous. These overloads are equally applicable: {candidates}. Cast an argument to choose one.",
        Description: "Several overloads are applicable to the call and none is better than the rest, so choosing would be arbitrary; the tie is reported only among candidates already proven applicable, which is what makes it safe to decide from a partial conversion relation. Cast an argument so that one overload is preferred.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("callee", SlotKind.Symbol), new Slot("candidates", SlotKind.Sequence, SlotKind.Symbol)]);

    public static readonly MistDiagnostic MethodGroupNotConverted = new(
        DiagnosticNumber.Mint("MIST0387"),
        Title: "Method group not converted",
        Template: "'{name}' names a method group, which is only valid where it is invoked or where a function type is expected whose signature one of its overloads has.",
        Description: "A method group reached a position that neither invokes it nor converts it to a function type whose signature one of its overloads has, including a generic overload the target leaves open. Call the method, or assign it to a function type that one overload matches.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name)]);

    public static readonly MistDiagnostic TypeArgumentsOnANonMethod = new(
        DiagnosticNumber.Mint("MIST0388"),
        Title: "Type arguments on non-method",
        Template: "'{name}' is not a method, so it takes no type arguments.",
        Description: "An expression supplies type arguments to something that is not a method, such as a field, property or local. Remove the type argument list, or apply it to the generic method you meant.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name)]);

    public static readonly MistDiagnostic MethodGroupAmbiguous = new(
        DiagnosticNumber.Mint("MIST0389"),
        Title: "Ambiguous method group conversion",
        Template: "'{name}' names more than one overload with the target function type, and none is more specific than the rest.",
        Description: "More than one overload of a method group matches the target function type and none is more specific than the others. Cast to a function type that singles one out, or wrap the call in a lambda.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name)]);

    public static readonly MistDiagnostic UnresolvableOverload = new(
        DiagnosticNumber.Mint("MIST0390"),
        Title: "No convertible overload",
        Template: "The call to {callee} matches no overload. No conversion is known from the given arguments to any of: {candidates}.",
        Description: "Several overloads accept the argument count but no conversion is known from the given arguments to any of them. The compiler declines to guess, since with every candidate eliminated it has nothing to choose on; convert the arguments to the parameter types of the overload you intend.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("callee", SlotKind.Symbol), new Slot("candidates", SlotKind.Sequence, SlotKind.Symbol)]);

    public static readonly MistDiagnostic TypeArgumentsNotInferred = new(
        DiagnosticNumber.Mint("MIST0391"),
        Title: "Type arguments not inferred",
        Template: "Cannot infer the type arguments of {callee} from the arguments. State all of them explicitly at the call: {parameters}.",
        Description: "A call to a generic method leaves at least one type parameter undetermined by the arguments; binding anyway would emit code naming a type parameter where a type belongs and move the error onto a generated file. State every type argument explicitly at the call.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("callee", SlotKind.Symbol), new Slot("parameters", SlotKind.Sequence, SlotKind.Type)]);

    public static readonly MistDiagnostic TypeArgumentsConflict = new(
        DiagnosticNumber.Mint("MIST0392"),
        Title: "Conflicting type argument inference",
        Template: "Cannot infer the type arguments of {callee}: the arguments give '{parameter}' the types {bounds}, and none of them is a type the others all convert to. State all of them explicitly at the call: {parameters}.",
        Description: "The arguments of a generic call give one type parameter several different types, none of which the others all convert to. State the type arguments explicitly at the call, or convert the arguments to a single type.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("callee", SlotKind.Symbol), new Slot("parameter", SlotKind.Type), new Slot("bounds", SlotKind.Sequence, SlotKind.Type), new Slot("parameters", SlotKind.Sequence, SlotKind.Type)]);

    public static readonly MistDiagnostic TypeArgumentCountMismatch = new(
        DiagnosticNumber.Mint("MIST0393"),
        Title: "Type argument count mismatch",
        Template: "The call to {callee} states the wrong number of type arguments. Declared type parameter counts: {arities}.",
        Description: "A call states a number of type arguments that no overload of the method declares. Supply exactly as many as the overload you intend has type parameters.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("callee", SlotKind.Symbol), new Slot("arities", SlotKind.Sequence, SlotKind.Quantity)]);


    public static readonly MistDiagnostic UnknownAtStoragePosition = new(
        DiagnosticNumber.Mint("MIST0394"),
        Title: "Unknown as storage type",
        Template: "{declarationKind} cannot have type '{type}': 'unknown' is not permitted as the declared type of writable storage. Use a concrete type, or accept 'unknown' as a parameter and pattern-match it.",
        Description: "Writable storage, directly or through a type argument, union constituent or tuple element, is declared with type 'unknown'. Storage is the one position that would let a value leave the top type without a pattern, and a getter-only property is refused too because the declared type decides what the slot admits; declare a concrete type, or take 'unknown' as a parameter and pattern-match it.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("declarationKind", SlotKind.Text), new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic UnknownViewHidesWriter = new(
        DiagnosticNumber.Mint("MIST0395"),
        Title: "Unknown view hides writer",
        Template: "'{member}' cannot be {operation} on '{receiver}': it would write to storage typed by '{parameter}', which '{receiver}' closes with 'unknown'. Narrow the receiver with an 'is' pattern to a concrete construction first.",
        Description: "A member exists on the receiver but is hidden because the receiver closes a type parameter with 'unknown' while the runtime object holds a concrete argument, so the write would store into concretely typed storage through a top-typed view. Narrow the receiver with an 'is' pattern to a concrete construction before writing.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("receiver", SlotKind.Type), new Slot("member", SlotKind.Name), new Slot("operation", SlotKind.Text), new Slot("parameter", SlotKind.Name)]);


    public static readonly MistDiagnostic ConformanceSourceNotConcrete = new(
        DiagnosticNumber.Mint("MIST0396"),
        Title: "Conformance subject not concrete",
        Template: "'{type}' cannot be declared to conform retroactively: only a class or a struct can be the subject of a with block.",
        Description: "A 'with' block declares retroactive conformance for a type that is not a class or struct. An abstract declares its conformances in its own base list and no other kind of type has members to map, so apply the block to a class or struct.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic ConformanceTargetNotAbstract = new(
        DiagnosticNumber.Mint("MIST0397"),
        Title: "Conformance target not abstract",
        Template: "'{target}' is not an abstract type, so '{source}' cannot be declared to conform to it.",
        Description: "A 'with' block declares conformance to a type that is not an abstract, and only an abstract has slots to conform to. Name an abstract type, or use inheritance if a concrete base was meant.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("target", SlotKind.Type), new Slot("source", SlotKind.Type)]);

    public static readonly MistDiagnostic EnumVariantValueNotInferable = new(
        DiagnosticNumber.Mint("MIST0398"),
        Title: "Enum variant value not inferable",
        Template: "Enum variant '{name}' needs an explicit value: none can be inferred over base type '{type}'. Only a string base, which uses the variant name, and an integral base, which counts up from the previous variant, can supply one.",
        Description: "An enum variant has no explicit value and its base type offers no rule to supply one; only a string base, which uses the variant's name, and an integral base, which counts up from the previous variant, can. Give the variant an explicit value, since every later stage depends on it having one.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name), new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic EnumVariantValueNotCountable = new(
        DiagnosticNumber.Mint("MIST0399"),
        Title: "Enum variant value not countable",
        Template: "Enum variant '{name}' needs an explicit value: the previous variant's value is not a compile-time constant this phase can count on from.",
        Description: "An enum variant has no explicit value and the previous variant's value is not a constant this phase can fold, such as a const reference or arithmetic. The compiler refuses rather than restarting the count at zero, which would silently merge variants into aliases; give this variant an explicit value.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name)]);

    public static readonly MistDiagnostic EnumVariantValueOutOfRange = new(
        DiagnosticNumber.Mint("MIST0400"),
        Title: "Enum variant value out of range",
        Template: "Enum variant '{name}' would take inferred value {value}, which does not fit base type '{type}'. Give it an explicit value, or widen the base type.",
        Description: "Counting up from the previous variant produced a value that does not fit the enum's base type; it is reported in Mist terms because the author wrote no number here. Give the variant an explicit value, or widen the base type.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name), new Slot("value", SlotKind.Quantity), new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic ConformanceUnresolvedSlot = new(
        DiagnosticNumber.Mint("MIST0401"),
        Title: "Unknown conformance slot",
        Template: "Abstract type '{type}' declares no member named '{member}'.",
        Description: "A 'with' block maps a slot that the abstract type does not declare. Check the slot name against the abstract's members.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("member", SlotKind.Name)]);

    public static readonly MistDiagnostic ConformanceMappingUnsatisfied = new(
        DiagnosticNumber.Mint("MIST0402"),
        Title: "Conformance mapping unsatisfied",
        Template: "No member named '{source}' on '{conformer}' can answer slot '{target}' of '{abstractType}'. A mapping needs a member of the same kind, and for a method one taking the same number of parameters.",
        Description: "A 'with' block maps an abstract's slot to a member of the conforming type, but no member of that name has the same kind and, for a method, the same parameter count; one diagnostic covers every way the two ends fail to pair since the remedy is the same. Point the mapping at a member that matches the slot, or add one.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("source", SlotKind.Name), new Slot("conformer", SlotKind.Type), new Slot("target", SlotKind.Name), new Slot("abstractType", SlotKind.Type)]);

    public static readonly MistDiagnostic AsConversionNoRelationship = new(
        DiagnosticNumber.Mint("MIST0403"),
        Title: "As conversion has no relationship",
        Template: "A value of type '{source}' can never be '{target}', so 'as' has no result type to produce. One must contain the other, or the source must be a union or unknown holding it; a conversion is not enough, since 'as' answers the value rather than a copy of it.",
        Description: "An 'as' names a target type that the source can never be, in either direction, so the expression has no result type, neither the bare type of a provable conversion nor the optional of a checked one. This also covers a void end and a newtype tested against its source; make one type contain the other, or test a union or unknown that can hold the target.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("source", SlotKind.Type), new Slot("target", SlotKind.Type)]);

    public static readonly MistDiagnostic MultipleUnbracketedPacks = new(
        DiagnosticNumber.Mint("MIST0404"),
        Title: "Multiple bracket-free packs",
        Template: "'{parameter}' is a second bracket-free pack after '{previous}'. Only one pack can absorb a flat argument list; declare this one as 'Type[]:{parameter}' and supply it in brackets.",
        Description: "A type parameter list declares two packs without brackets, and two of them could divide a flat argument list any number of ways, so the list is ambiguous by construction. Declare the second pack with a 'Type[]' domain and supply its arguments in brackets.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("parameter", SlotKind.Name), new Slot("previous", SlotKind.Name)],
        Related: [DiagnosticNumber.Mint("MIST0405")]);

    public static readonly MistDiagnostic MultipleParamsParameters = new(
        DiagnosticNumber.Mint("MIST0405"),
        Title: "Multiple params parameters",
        Template: "'{parameter}' is a second 'params' parameter after '{previous}'. Only one parameter of a value-parameter list can absorb a flat argument list; declare this one without 'params' and pass it as one tuple or array.",
        Description: "A parameter list declares a second 'params' parameter, but a caller's flat argument list can be absorbed by only one parameter whichever of a tuple or an array collects it; the variadics spec states this and the pack rule as one rule. Declare the second parameter without 'params' and pass it as a single tuple or array.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("parameter", SlotKind.Name), new Slot("previous", SlotKind.Name)],
        Related: [DiagnosticNumber.Mint("MIST0404")]);

    public static readonly MistDiagnostic PackInTypePosition = new(
        DiagnosticNumber.Mint("MIST0406"),
        Title: "Pack used as single type",
        Template: "'{name}' is a pack, a list of types, and this position needs one type. Spread it with '{name}...' where a list is accepted: a type-argument list, a tuple type, or a member declaration.",
        Description: "A pack, which is a list of types, is written where exactly one type is needed. Spread it with '...' in a position that accepts a list, such as a type-argument list, a tuple type or a member declaration.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name)]);

    public static readonly MistDiagnostic DisallowedDependentDomain = new(
        DiagnosticNumber.Mint("MIST0407"),
        Title: "Disallowed dependent domain",
        Template: "Type '{type}' cannot be used as a dependent type parameter. Only primitive integral types, bool, and char are allowed.",
        Description: "A dependent type parameter is declared over a type whose values cannot carry type identity. A construction is identified by its argument, so the domain's equality must be total and exact, which excludes the floating-point types however convenient they look; use a primitive integral type, bool or char.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic DuplicateTypeParameter = new(
        DiagnosticNumber.Mint("MIST0408"),
        Title: "Duplicate type parameter",
        Template: "Type parameter '{name}' is declared more than once in the same type parameter list.",
        Description: "A type parameter list declares the same name twice. Rename one of the parameters.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name)]);

    public static readonly MistDiagnostic UnprovenPackIndex = new(
        DiagnosticNumber.Mint("MIST0409"),
        Title: "Unproven pack index",
        Template: "'{name}[{index}]' indexes a pack whose length is not known here, so it may hold no element {index}. Only a pack declared with a fixed size, 'Type[N]:{name}', or the binding of a guard proving its size, 'if ({name} is Type[N] fixed)', can be indexed in a type position.",
        Description: "A pack is indexed in a type position where its length is not fixed; a declaration is judged for every instantiation, including the empty pack, so no index is provably inside a 'Type[]' pack. Declare the pack with a fixed size 'Type[N]', or index it under a guard that proves the size.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name), new Slot("index", SlotKind.Quantity)]);

    public static readonly MistDiagnostic PackIndexOutOfBounds = new(
        DiagnosticNumber.Mint("MIST0410"),
        Title: "Pack index out of bounds",
        Template: "'{name}[{index}]' is outside the pack: '{name}' is declared to hold exactly {length} element(s), indexed from 0.",
        Description: "A pack is indexed at a position at or beyond the fixed number of elements it is declared to hold, counted from zero. Use an index below the declared size.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name), new Slot("index", SlotKind.Quantity), new Slot("length", SlotKind.Quantity)]);

    public static readonly MistDiagnostic TooFewTypeArgumentsForPack = new(
        DiagnosticNumber.Mint("MIST0411"),
        Title: "Too few type arguments",
        Template: "'{type}' needs at least {minimum} type arguments to fill the slots around its pack, but {actual} were given.",
        Description: "A flat type-argument list is too short to fill the fixed slots around a pack even with the pack absorbing nothing; a pack makes the count flexible upward only. Supply at least one argument for each non-pack slot.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("minimum", SlotKind.Quantity), new Slot("actual", SlotKind.Quantity)]);

    public static readonly MistDiagnostic SpreadStemCollision = new(
        DiagnosticNumber.Mint("MIST0412"),
        Title: "Spread member name collision",
        Template: "'{name}' collides with the spread member '{stem}', which owns that name and every '{stem}' followed by an index.",
        Description: "A member's name falls inside the family a spread member owns, which is the aggregate and every indexed projection of it. How many projections exist is unknown where the declaration is written, so the whole prefixed name space is reserved; rename the member.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name), new Slot("stem", SlotKind.Name)]);

    public static readonly MistDiagnostic ParamsArrayNotLast = new(
        DiagnosticNumber.Mint("MIST0413"),
        Title: "Params parameter not last",
        Template: "'{name}' is declared 'params' but is not the last parameter. A 'params' collection collects the arguments after the fixed ones, so nothing may follow it.",
        Description: "A homogeneous 'params' collection parameter is followed by other parameters, but it gathers the arguments that run past the fixed ones, which only the last slot can do; a value pack, which binds inward from both ends, may sit anywhere. This is a rule of Mist rather than of any backend, so move the 'params' parameter to the end.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name)]);

    public static readonly MistDiagnostic ParamsParameterMalformed = new(
        DiagnosticNumber.Mint("MIST0414"),
        Title: "Malformed params parameter",
        Template: "'{name}' is declared 'params' but {reason}. A 'params' parameter is a value pack, 'params TArgs {name}', or a collection a flat list is gathered into -- an array, 'params T[] {name}', a span, a list, or an enumerable interface -- passed by value.",
        Description: "A parameter is marked 'params' but its type or passing mode is not one a flat argument list can be gathered into. Declare it as a value pack, or as a by-value array, span, list or enumerable collection.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name), new Slot("reason", SlotKind.Text)]);

    public static readonly MistDiagnostic UnprovableSpread = new(
        DiagnosticNumber.Mint("MIST0415"),
        Title: "Unprovable spread",
        Template: "'{type}' cannot be spread here: its length is not known at this point, so the number of arguments it stands for is not either.",
        Description: "A '...' spread is applied to an operand whose length is not known at this point, but the spread promises a definite number of arguments and the call was checked against that promise. Spread a pack or value whose arity is fixed here, or prove its size with a guard first.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type)]);

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
        Template: "'{type}' is an abstract and cannot be constructed. Construct a class or a struct that conforms to it.",
        Description: "A 'new' expression names an abstract type, which has no constructor to run because it describes a contract rather than a value. Construct a class or struct that conforms to it.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic NewtypeCloneInvalid = new(
        DiagnosticNumber.Mint("MIST0418"),
        Title: "Invalid newtype clone",
        Template: "'{type}' cannot clone '{source}', whose member '{member}' does not hold for '{type}'.",
        Description: "A newtype clones a source type whose member no longer holds once the source's name means the newtype, for example a body passing 'this' where the source type is required. The source itself is not wrong, so the error is reported at the newtype; adjust the source member so it stays valid under the newtype, or choose a different source.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("source", SlotKind.Type), new Slot("member", SlotKind.Name)]);

    public static readonly MistDiagnostic NewtypeSourceNotSupported = new(
        DiagnosticNumber.Mint("MIST0419"),
        Title: "Unsupported newtype source",
        Template: "A newtype over '{type}' is not supported: a newtype clones a class or a struct declared in Mist.",
        Description: "A newtype names a source that is not a class or struct declared in Mist, and a newtype works by cloning its source's Mist members. Clone a Mist class or struct instead.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type)]);
}