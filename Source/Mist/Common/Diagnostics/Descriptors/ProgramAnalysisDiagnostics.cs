#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

public static partial class ProgramAnalysisDiagnostics
{
    public static readonly MistDiagnostic SwitchNotExhaustive = new(
        DiagnosticNumber.Mint("MIST0148"),
        Title: "Switch not exhaustive",
        Template: "Switch expression is not exhaustive. Missing cases: {missing}",
        Description: "Some value of the scrutinee reaches no arm of this switch expression. The program still compiles, and an unmatched value throws at runtime, which is why the finding is a warning rather than an error; whether Mist should refuse such a switch is the open policy question MIST-106. Add arms for the listed cases, or a discard arm to cover the remainder.",
        Category: DiagnosticCategory.Flw,
        Policy: PolicyClass.Optional,
        DefaultSeverity: DiagnosticSeverity.Warning,
        Slots: [new Slot("missing", SlotKind.Sequence, SlotKind.Name)]);

    public static readonly MistDiagnostic CastHasNoConversion = new(
        DiagnosticNumber.Mint("MIST0149"),
        Title: "Cast without conversion",
        Template: "No conversion is defined from '{source}' to '{target}'. A cast reaches only a type the value already is, or one a declared cast operator reaches; to narrow a value, test it with an 'is' pattern instead.",
        Description: "The cast names a target type that the value neither already is nor can reach through a declared cast operator, so there is nothing for the cast to do. Declare a cast operator between the two types, or narrow the value with an 'is' pattern when the target is a subtype.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("source", SlotKind.Type), new Slot("target", SlotKind.Type)]);

    public static readonly MistDiagnostic EnumCastArgumentMismatch = new(
        DiagnosticNumber.Mint("MIST0150"),
        Title: "Enum cast argument mismatch",
        Template: "Cannot cast '{source}' to enum '{target}'. A cast to an enum looks up the variant holding a value of its underlying type '{parameter}', and '{source}' does not convert to '{parameter}' implicitly.",
        Description: "Casting to an enum selects the variant whose underlying value matches the operand, so the operand must convert implicitly to the enum's underlying type, and this one does not. Convert the operand to the underlying type first, or cast from a value that already has it.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("source", SlotKind.Type), new Slot("target", SlotKind.Type), new Slot("parameter", SlotKind.Type)]);

    public static readonly MistDiagnostic CastArgumentMismatch = new(
        DiagnosticNumber.Mint("MIST0151"),
        Title: "Cast operand mismatch",
        Template: "Cannot cast '{source}' to '{target}'. The cast calls {operator}, and '{source}' does not convert to its parameter type '{parameter}' implicitly.",
        Description: "The cast resolves to a declared cast operator whose parameter type the operand does not implicitly convert to. Convert the operand to that parameter type first, or declare a cast operator that accepts the operand's type.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("source", SlotKind.Type), new Slot("target", SlotKind.Type), new Slot("operator", SlotKind.Symbol), new Slot("parameter", SlotKind.Type)]);

    public static readonly MistDiagnostic NewtypeSourceDefinesNoCopy = new(
        DiagnosticNumber.Mint("MIST0152"),
        Title: "Newtype source has no copy",
        Template: "Cannot cast '{source}' to '{target}'. '{cloneSource}' defines no copy to convert with; a newtype and its mutable class source convert only when the class implements 'IClonable<{cloneSource}>' with a Clone of its own.",
        Description: "Converting between a newtype and its mutable class source needs a copy, because the two types share no storage, and the source class declares none. Implement 'IClonable<T>' on the source class with a Clone of its own, or convert from an immutable source that needs no copy.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("source", SlotKind.Type), new Slot("target", SlotKind.Type), new Slot("cloneSource", SlotKind.Type)]);

    public static readonly MistDiagnostic CopyConversionDoesNotHold = new(
        DiagnosticNumber.Mint("MIST0153"),
        Title: "Field-wise copy does not hold",
        Template: "Cannot convert '{source}' to '{target}'. The copy is field for field, and it does not hold at '{member}': a '{declared}' in the source is a '{renamed}' in the newtype. '{cloneSource}' implements no 'IClonable<{cloneSource}>' to say how it is copied.",
        Description: "A newtype over a mutable class converts by copying field for field, and one field's type in the source does not match the corresponding field in the newtype, with no 'IClonable<T>' on that field type to say how it is copied. Implement 'IClonable<T>' on the offending field type, or align the field types of the source and the newtype.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("source", SlotKind.Type), new Slot("target", SlotKind.Type), new Slot("member", SlotKind.Name), new Slot("declared", SlotKind.Type), new Slot("renamed", SlotKind.Type), new Slot("cloneSource", SlotKind.Type)]);

    public static readonly MistDiagnostic CloneConversionDoesNotHold = new(
        DiagnosticNumber.Mint("MIST0154"),
        Title: "Clone conversion does not hold",
        Template: "Cannot cast '{source}' to '{target}'. The cast runs '{operator}' with a '{parameter}' as this, and it does not hold at '{member}'.",
        Description: "The cast runs the source type's Clone with a value of the target type as 'this', and that Clone's body is not valid for the target at the named location. Adjust the Clone so every member it touches exists with compatible types on both the source and the newtype.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("source", SlotKind.Type), new Slot("target", SlotKind.Type), new Slot("operator", SlotKind.Symbol), new Slot("parameter", SlotKind.Type), new Slot("member", SlotKind.Name)]);

    public static readonly MistDiagnostic AmbiguousCast = new(
        DiagnosticNumber.Mint("MIST0155"),
        Title: "Ambiguous cast",
        Template: "The cast from '{source}' to '{target}' is ambiguous between {candidates}. Cast the value to one operator's parameter type first to choose it.",
        Description: "More than one declared cast operator can carry the value to the target type, and none is more specific than the others. Convert the value to one operator's parameter type first so a single operator applies, or remove the redundant operator.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("source", SlotKind.Type), new Slot("target", SlotKind.Type), new Slot("candidates", SlotKind.Sequence, SlotKind.Symbol)]);

    public static readonly MistDiagnostic AmbiguousImplicitConversion = new(
        DiagnosticNumber.Mint("MIST0156"),
        Title: "Ambiguous implicit conversion",
        Template: "The implicit conversion from '{source}' to '{target}' is ambiguous between {candidates}. Cast the value to choose one.",
        Description: "Two or more implicit conversion operators apply between these types and neither is preferred. Add an explicit cast to pick one, or remove one of the competing operators.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("source", SlotKind.Type), new Slot("target", SlotKind.Type), new Slot("candidates", SlotKind.Sequence, SlotKind.Symbol)]);

    public static readonly MistDiagnostic NoImplicitConversion = new(
        DiagnosticNumber.Mint("MIST0157"),
        Title: "No implicit conversion",
        Template: "Cannot implicitly convert type '{source}' to '{target}'.",
        Description: "The source type does not convert to the target type implicitly and no explicit cast exists either. Give the expression a value of the target type, or declare a cast operator between the two types.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("source", SlotKind.Type), new Slot("target", SlotKind.Type)]);

    public static readonly MistDiagnostic CastRequired = new(
        DiagnosticNumber.Mint("MIST0158"),
        Title: "Explicit cast required",
        Template: "Cannot implicitly convert type '{source}' to '{target}'. An explicit cast exists (are you missing a cast?)",
        Description: "The source type reaches the target only through an explicit cast operator, and the conversion here is implicit. Add the cast, or declare the operator as implicit if the conversion is always safe.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("source", SlotKind.Type), new Slot("target", SlotKind.Type)]);

    public static readonly MistDiagnostic InaccessibleMember = new(
        DiagnosticNumber.Mint("MIST0159"),
        Title: "Inaccessible member",
        Template: "'{member}' is inaccessible due to its protection level: {accessibility} members are accessible only {scope}.",
        Description: "The member's accessibility modifier excludes this use site. Access it from within the scope its modifier allows, or widen the modifier on the declaration.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name), new Slot("accessibility", SlotKind.Enum), new Slot("scope", SlotKind.Name)]);

    public static readonly MistDiagnostic InaccessibleType = new(
        DiagnosticNumber.Mint("MIST0160"),
        Title: "Inaccessible type",
        Template: "The type '{type}' is inaccessible here: {accessibility} types are accessible only {scope}.",
        Description: "The type's accessibility modifier does not admit a reference from this location. Widen the type's accessibility, or move the reference inside the scope that may see it.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("accessibility", SlotKind.Enum), new Slot("scope", SlotKind.Name)]);

    public static readonly MistDiagnostic InconsistentAccessibility = new(
        DiagnosticNumber.Mint("MIST0161"),
        Title: "Inconsistent accessibility",
        Template: "Inconsistent accessibility: {exposedKind} '{exposed}' is less accessible than {ownerKind} '{owner}'.",
        Description: "A declaration exposes a type that is less accessible than the declaration itself, so some callers could see the member but not its type. Widen the type's accessibility, or narrow the member's to match.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("exposedKind", SlotKind.Enum), new Slot("exposed", SlotKind.Name), new Slot("ownerKind", SlotKind.Enum), new Slot("owner", SlotKind.Name)]);

    public static readonly MistDiagnostic SealedBaseType = new(
        DiagnosticNumber.Mint("MIST0162"),
        Title: "Derivation from sealed type",
        Template: "Cannot derive from sealed type '{type}'.",
        Description: "A sealed type admits no derived types. Remove the base, or remove 'sealed' from the base type if it is meant to be extended.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic CyclicInheritance = new(
        DiagnosticNumber.Mint("MIST0163"),
        Title: "Inheritance cycle",
        Template: "The base '{base}' of '{type}' forms an inheritance cycle.",
        Description: "Following base types from this declaration leads back to itself, so no type in the loop can be laid out. Break the cycle by removing or replacing one of the base references.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("base", SlotKind.Type)]);

    public static readonly MistDiagnostic NewtypeOverSealedType = new(
        DiagnosticNumber.Mint("MIST0164"),
        Title: "Newtype over sealed type",
        Template: "Cannot declare a newtype over sealed type '{type}'.",
        Description: "A newtype wraps its source type the way a derived type wraps a base, and a sealed type refuses both. Choose an unsealed source, or unseal the source if the newtype is intended.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic SealedTypeAlias = new(
        DiagnosticNumber.Mint("MIST0165"),
        Title: "Sealed type alias",
        Template: "The 'sealed' modifier is not valid on a type alias; it can only be applied to class or struct declarations.",
        Description: "An alias introduces no new type for 'sealed' to close, so the modifier has no meaning there. Remove the modifier from the alias, or seal the class or struct it names.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic EnumBaseNotReadonly = new(
        DiagnosticNumber.Mint("MIST0166"),
        Title: "Enum base not readonly",
        Template: "Enum '{type}' has a non-readonly base type '{base}'. Enum base types, and every constituent of a union base, must be readonly.",
        Description: "An enum's variants are constants, so its base type and every constituent of a union base must be readonly, and the named base type is not. Use a readonly base type, or mark the base type readonly.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("base", SlotKind.Type)]);

    public static readonly MistDiagnostic EnumDuplicateValue = new(
        DiagnosticNumber.Mint("MIST0167"),
        Title: "Duplicate enum value",
        Template: "Enum member '{member}' has the same value as member '{previousMember}' in enum '{type}'.",
        Description: "Two members of the enum carry the same underlying value, so a cast from that value could not pick a variant. Give each member a distinct value.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name), new Slot("previousMember", SlotKind.Name), new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic DefaultEntryNotAField = new(
        DiagnosticNumber.Mint("MIST0168"),
        Title: "Default entry not a field",
        Template: "'{member}' is not a field of '{type}', so a default block cannot assign it.",
        Description: "A default block assigns fields of the type that declares it, and the named entry resolves to no such field. Correct the name, or declare the field the entry is meant to initialize.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name), new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic OperatorNotPublic = new(
        DiagnosticNumber.Mint("MIST0169"),
        Title: "Operator not public static",
        Template: "Operator '{operator}' must be declared 'public static'.",
        Description: "Operators are found through their operand types and bound without an instance, so every operator declaration must be both public and static. Add the missing modifiers.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("operator", SlotKind.Name)]);

    public static readonly MistDiagnostic OperatorUnpaired = new(
        DiagnosticNumber.Mint("MIST0170"),
        Title: "Unpaired operator",
        Template: "Operator '{operator}' on '{type}' requires a matching operator '{pair}', with the same parameter and return types, to also be declared.",
        Description: "Some operators come in pairs, such as equality with inequality, and declaring one without its counterpart of identical parameter and return types leaves the pair incomplete. Declare the matching operator.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("operator", SlotKind.Name), new Slot("type", SlotKind.Type), new Slot("pair", SlotKind.Name)]);

    public static readonly MistDiagnostic LogicalOperatorDeclared = new(
        DiagnosticNumber.Mint("MIST0171"),
        Title: "Logical operator on non-Boolean type",
        Template: "Operator '{operator}' can be declared only on Boolean.",
        Description: "The short-circuit logical operators are reserved to Boolean and cannot be overloaded by user types. Remove the declaration, or express the operation as a named method or a different operator.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("operator", SlotKind.Name)]);

    public static readonly MistDiagnostic OperatorNotOnDeclaringType = new(
        DiagnosticNumber.Mint("MIST0172"),
        Title: "Operator omits declaring type",
        Template: "Operator '{operator}' must take '{type}', the type declaring it, as a parameter; an operator is found only through its operands' types.",
        Description: "Operator lookup starts from the operands' types, so an operator none of whose parameters is the declaring type can never be found. Make at least one parameter the declaring type, or move the operator to a type that is an operand.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("operator", SlotKind.Name), new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic RefinementNotComparable = new(
        DiagnosticNumber.Mint("MIST0173"),
        Title: "Refinement over incomparable type",
        Template: "A relational refinement predicate cannot constrain '{declaration}': its type '{type}' supports no comparison operators.",
        Description: "A relational refinement predicate orders the refined value, but its type declares no comparison operators. Use an equality or pattern predicate instead, or give the type comparison operators.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("declaration", SlotKind.Name), new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic RefinementConstantMismatch = new(
        DiagnosticNumber.Mint("MIST0174"),
        Title: "Refinement constant type mismatch",
        Template: "The refinement predicate on '{declaration}' compares a constant of type '{constantType}' with a value of type '{type}', which no value of '{type}' can equal or be ordered against.",
        Description: "The refinement compares the refined value against a constant of a type it can neither equal nor be ordered against, so the predicate could never hold. Use a constant of the refined value's type.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("declaration", SlotKind.Name), new Slot("constantType", SlotKind.Type), new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic RefinementOverMutableMember = new(
        DiagnosticNumber.Mint("MIST0175"),
        Title: "Refinement reads mutable member",
        Template: "The refinement predicate on '{declaration}' reads '{member}', which can answer differently after a value was proven. A predicate reads only a readonly field, or a property with no 'set' accessor whose getter is pure.",
        Description: "A refinement is proven once and relied on afterwards, so its predicate may read only members whose answers cannot change: readonly fields, or set-less properties with pure getters. Rewrite the predicate over such members, or make the member it reads readonly.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("declaration", SlotKind.Name), new Slot("member", SlotKind.Name)]);

    public static readonly MistDiagnostic DuplicateSignature = new(
        DiagnosticNumber.Mint("MIST0176"),
        Title: "Duplicate member signature",
        Template: "Type '{type}' already declares {member} with parameter types ({parameters}); neither the return type nor a parameter's name tells two declarations apart.",
        Description: "The type already declares a member with this name and parameter types, and differing return types or parameter names do not create a distinct overload. Remove one declaration or change a parameter type.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("member", SlotKind.Name), new Slot("parameters", SlotKind.Sequence, SlotKind.Type)]);

    public static readonly MistDiagnostic MemberRedeclaration = new(
        DiagnosticNumber.Mint("MIST0177"),
        Title: "Member redeclared",
        Template: "'{type}' cannot redeclare member '{member}' already defined in '{base}'.",
        Description: "The type declares a member that an ancestor already defines, and Mist does not hide inherited members by redeclaration. Rename the new member, or remove it and use the inherited one.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("member", SlotKind.Name), new Slot("base", SlotKind.Type)]);

    public static readonly MistDiagnostic ConstraintNotSatisfied = new(
        DiagnosticNumber.Mint("MIST0178"),
        Title: "Constraint not satisfied",
        Template: "Type argument '{argument}' does not satisfy the '{constraint}' constraint on type parameter '{parameter}'.",
        Description: "The type argument supplied for a generic parameter lacks a conformance or discipline the parameter's constraint demands. Supply a type that meets the constraint, or relax the constraint on the declaration.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("argument", SlotKind.Type), new Slot("constraint", SlotKind.Name), new Slot("parameter", SlotKind.Name)]);

    public static readonly MistDiagnostic ConstructorConstraintNotSatisfied = new(
        DiagnosticNumber.Mint("MIST0179"),
        Title: "Constructor constraint not satisfied",
        Template: "Type '{type}' must have a public constructor with parameters '({parameters})' to satisfy the 'new({parameters})' constraint on type parameter '{parameter}'.",
        Description: "A 'new(...)' constraint promises callers a public constructor with the listed parameter types, and the type argument declares none. Add such a constructor to the type, or use a type that has one.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("parameters", SlotKind.Sequence, SlotKind.Type), new Slot("parameter", SlotKind.Name)]);

    public static readonly MistDiagnostic NotDefaultable = new(
        DiagnosticNumber.Mint("MIST0180"),
        Title: "Type has no default",
        Template: "Type '{type}' has no default. Construct it explicitly, or use a type that has a default.",
        Description: "A default value is required here, but the type declares neither a default block nor any other way to produce one. Construct a value explicitly, or choose a type that provides a default.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic FieldHasNoDefault = new(
        DiagnosticNumber.Mint("MIST0181"),
        Title: "Field without default",
        Template: "Field '{field}' of '{type}' has no default, which '{type}' needs because it declares {declaration}: its type '{fieldType}' has none. Give the field an initializer or a default block entry, or give '{type}' a constructor that assigns it and no default block.",
        Description: "The containing type promises a default, and that default must give every field a value, but this field's type has none to start from. Add an initializer or a default block entry for the field, or replace the type's default with a constructor that assigns it.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("field", SlotKind.Name), new Slot("type", SlotKind.Type), new Slot("declaration", SlotKind.Enum), new Slot("fieldType", SlotKind.Type)]);

    public static readonly MistDiagnostic BaseNotDefaultable = new(
        DiagnosticNumber.Mint("MIST0182"),
        Title: "Base type without default",
        Template: "Type '{type}' needs a default because it declares {declaration}, and its base '{base}' has none to start from. Use a base type that has a default, or give '{type}' a constructor and no default block.",
        Description: "A type's default is built on its base type's default, and the base provides none, yet this type declares something that requires one. Derive from a type with a default, or drop the default block in favor of a constructor.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("declaration", SlotKind.Enum), new Slot("base", SlotKind.Type)]);

    public static readonly MistDiagnostic SlotNotFilled = new(
        DiagnosticNumber.Mint("MIST0183"),
        Title: "Abstract slot not filled",
        Template: "'{type}' does not fill slot '{slot}' of '{abstractType}': it declares no public member named '{member}'.",
        Description: "A type claiming conformance to an abstract type must provide a public member for each of its slots, and no member with this slot's name exists. Declare the member, or map an existing one onto the slot in a 'with' block.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("slot", SlotKind.Name), new Slot("abstractType", SlotKind.Type), new Slot("member", SlotKind.Name)]);

    public static readonly MistDiagnostic SlotSignatureMismatch = new(
        DiagnosticNumber.Mint("MIST0184"),
        Title: "Slot signature mismatch",
        Template: "'{type}' does not fill slot '{slot}' of '{abstractType}': its '{member}' has a different signature.",
        Description: "The type has a member of the slot's name, but its parameters or return type differ from what the abstract type declares. Change the member's signature to match the slot, or map a different member onto it.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("slot", SlotKind.Name), new Slot("abstractType", SlotKind.Type), new Slot("member", SlotKind.Name)]);

    public static readonly MistDiagnostic SlotConstraintStrengthened = new(
        DiagnosticNumber.Mint("MIST0185"),
        Title: "Slot constraint strengthened",
        Template: "'{type}' does not fill slot '{slot}' of '{abstractType}': its '{member}' requires '{constraint}' of type parameter '{parameter}', which the slot does not promise.",
        Description: "The member filling a slot places a constraint on a type parameter that the abstract slot does not promise, so a caller going through the abstract type could pass an argument the member rejects. Remove the extra constraint, or add it to the slot.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("slot", SlotKind.Name), new Slot("abstractType", SlotKind.Type), new Slot("member", SlotKind.Name), new Slot("constraint", SlotKind.Name), new Slot("parameter", SlotKind.Name)]);

    public static readonly MistDiagnostic SlotReturnNotOwned = new(
        DiagnosticNumber.Mint("MIST0186"),
        Title: "Slot filler not returning owned",
        Template: "'{type}' does not fill slot '{slot}' of '{abstractType}': its '{member}' does not return owned, and a call through the slot hands its caller a value nothing else holds.",
        Description: "The abstract slot promises an owned result, and a caller through the slot takes the value as something nothing else holds, but the filling member does not return owned. Declare the member's return as owned and make its body produce an owned value.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("slot", SlotKind.Name), new Slot("abstractType", SlotKind.Type), new Slot("member", SlotKind.Name)]);

    public static readonly MistDiagnostic ReadonlyContractNotMet = new(
        DiagnosticNumber.Mint("MIST0187"),
        Title: "Readonly contract not met",
        Template: "Member '{member}' in type '{type}' must be readonly to satisfy the readonly contract on abstract '{abstractType}': it writes, or reaches something that does.",
        Description: "The abstract type marks the slot readonly, so the member filling it may neither write nor reach anything that writes, and this member does. Mark the member readonly and remove the writes it performs or reaches.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name), new Slot("type", SlotKind.Type), new Slot("abstractType", SlotKind.Type)]);

    public static readonly MistDiagnostic PropertySlotAccessorMissing = new(
        DiagnosticNumber.Mint("MIST0188"),
        Title: "Property slot accessor missing",
        Template: "'{type}.{member}' does not implement the '{accessor}' accessor required by abstract type '{abstractType}'.",
        Description: "The abstract type's property slot declares an accessor that the conforming property does not provide. Add the accessor to the property.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("member", SlotKind.Name), new Slot("accessor", SlotKind.Enum), new Slot("abstractType", SlotKind.Type)]);

    public static readonly MistDiagnostic ConformanceSlotUnanswered = new(
        DiagnosticNumber.Mint("MIST0189"),
        Title: "Conformance slot unanswered",
        Template: "The with block for '{type} : {abstractType}' leaves slot '{slot}' unanswered. Map a member onto it, or declare a public member of that name and signature.",
        Description: "A 'with' block conforms a type to an abstract type explicitly, and one of the abstract's slots has neither a mapping in the block nor a public member of matching name and signature. Map a member onto the slot, or declare one.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("abstractType", SlotKind.Type), new Slot("slot", SlotKind.Name)]);

    public static readonly MistDiagnostic RedundantConformance = new(
        DiagnosticNumber.Mint("MIST0190"),
        Title: "Redundant conformance",
        Template: "'{type}' already conforms to '{abstractType}', so this with block is redundant. A type reaches an abstract once: remove the with block, or remove '{abstractType}' from the base list.",
        Description: "The type already reaches this abstract type through its base list or another 'with' block, and a type conforms to an abstract once. Remove the extra 'with' block or the base list entry.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("abstractType", SlotKind.Type)]);

    public static readonly MistDiagnostic TypeSlotUnfilled = new(
        DiagnosticNumber.Mint("MIST0191"),
        Title: "Associated type slot unfilled",
        Template: "'{type}' declares no nested type named '{member}' to fill the associated type slot '{abstractType}.{member}'.",
        Description: "The abstract type declares an associated type slot, and the conforming type declares no nested type of that name to fill it. Add the nested type.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("member", SlotKind.Name), new Slot("abstractType", SlotKind.Type)]);

    public static readonly MistDiagnostic TypeSlotConstraintNotSatisfied = new(
        DiagnosticNumber.Mint("MIST0192"),
        Title: "Type slot constraint not satisfied",
        Template: "Nested type '{type}' does not satisfy constraint '{constraint}' of associated type slot '{abstractType}.{slot}'.",
        Description: "The nested type filling an associated type slot does not meet the constraint the slot declares. Make the nested type conform, or choose a different nested type.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("constraint", SlotKind.Name), new Slot("abstractType", SlotKind.Type), new Slot("slot", SlotKind.Name)]);

    public static readonly MistDiagnostic PatternNeverMatches = new(
        DiagnosticNumber.Mint("MIST0193"),
        Title: "Pattern never matches",
        Template: "A value of type '{source}' can never be '{target}', so this pattern never matches.",
        Description: "The scrutinee's type and the pattern's type share no value, so the arm can never be taken. Remove the pattern or test against a type the scrutinee can actually be.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("source", SlotKind.Type), new Slot("target", SlotKind.Type)]);

    public static readonly MistDiagnostic PatternNarrowsToUnknown = new(
        DiagnosticNumber.Mint("MIST0194"),
        Title: "Pattern narrows to unknown",
        Template: "Narrowing to 'unknown' is not meaningful: the pattern is tautologically true and introduces no new type information. Remove the pattern or narrow to a concrete type.",
        Description: "Every value already widens to 'unknown', so an 'is unknown' test always succeeds and adds no information. Remove the pattern or narrow to a concrete type.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic PatternNarrowsToUnknownClosure = new(
        DiagnosticNumber.Mint("MIST0195"),
        Title: "Pattern narrows to unknown closure",
        Template: "Narrowing '{source}' to '{target}' is not meaningful: every '{source}' already widens to '{target}', so the pattern is tautologically true, and the binding it introduces only hides the members that write storage of the parameters closed with 'unknown'. Narrow to a concrete construction instead.",
        Description: "The pattern tests for a generic type closed with 'unknown', which every instantiation of that type already widens to; the test is always true and the binding merely hides the members that write storage of the parameters closed with 'unknown'. Narrow to a concrete instantiation instead.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("source", SlotKind.Type), new Slot("target", SlotKind.Type)]);

    public static readonly MistDiagnostic PatternNotComparable = new(
        DiagnosticNumber.Mint("MIST0196"),
        Title: "Relational pattern over incomparable type",
        Template: "A relational pattern cannot be applied to a value of type '{type}', which supports no comparison operators.",
        Description: "A relational pattern orders the scrutinee against a bound, and the scrutinee's type declares no comparison operators. Use a constant or type pattern, or give the type comparison operators.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic PatternConstantMismatch = new(
        DiagnosticNumber.Mint("MIST0197"),
        Title: "Pattern constant type mismatch",
        Template: "A constant of type '{constantType}' cannot be compared with a value of type '{type}'.",
        Description: "The constant in the pattern has a type that no value of the scrutinee's type can be compared with, so the arm could never match. Use a constant of the scrutinee's type.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("constantType", SlotKind.Type), new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic PatternBoundNotConstant = new(
        DiagnosticNumber.Mint("MIST0198"),
        Title: "Relational pattern bound not constant",
        Template: "The bound of a relational pattern must be a constant.",
        Description: "The bound of a relational pattern is fixed at compile time so exhaustiveness and overlap can be checked, and this one is a runtime expression. Replace it with a constant, or compare in a guard clause.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic SwitchArmUnreachable = new(
        DiagnosticNumber.Mint("MIST0199"),
        Title: "Unreachable switch arm",
        Template: "This switch arm is unreachable: an earlier arm already matches everything it could.",
        Description: "A preceding arm already covers every value this arm could match, so control never reaches it. Remove the arm, or reorder arms so the more specific one comes first.",
        Category: DiagnosticCategory.Flw,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic OperatorNotApplicable = new(
        DiagnosticNumber.Mint("MIST0200"),
        Title: "Operator not applicable",
        Template: "Operator '{operator}' cannot be applied to operands of type '{left}' and '{right}'.",
        Description: "No operator with this symbol is defined for the operand types, either built in or declared on them. Convert an operand to a type that supports it, or declare the operator on one of the operand types.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("operator", SlotKind.Name), new Slot("left", SlotKind.Type), new Slot("right", SlotKind.Type)]);

    public static readonly MistDiagnostic UnknownHasNoOperators = new(
        DiagnosticNumber.Mint("MIST0201"),
        Title: "Operator applied to unknown",
        Template: "Operator '{operator}' cannot be applied to operands of type '{left}' and '{right}': no operators are defined for 'unknown'. Narrow the value with an 'is' pattern first.",
        Description: "A value typed 'unknown' could be anything, so no operator is defined on it. Narrow the value with an 'is' pattern before operating on it.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("operator", SlotKind.Name), new Slot("left", SlotKind.Type), new Slot("right", SlotKind.Type)]);

    public static readonly MistDiagnostic UnknownHasNoMembers = new(
        DiagnosticNumber.Mint("MIST0202"),
        Title: "Member access on unknown",
        Template: "Cannot access member '{member}' on a value of type 'unknown': 'unknown' has no members. Narrow the value with an 'is' pattern first.",
        Description: "A value typed 'unknown' exposes no members because nothing is known about it. Narrow the value with an 'is' pattern to a type that has the member.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name)]);

    public static readonly MistDiagnostic UnknownClosureConstructed = new(
        DiagnosticNumber.Mint("MIST0203"),
        Title: "Construction of unknown closure",
        Template: "'{type}' cannot be constructed: its type argument for {parameters} is 'unknown', and a generic type closed with 'unknown' is only a view of a concrete instantiation. Construct a concrete instantiation and widen it.",
        Description: "A generic type closed with 'unknown' is only a view onto some concrete instantiation and cannot be instantiated itself. Construct a concrete instantiation and widen it to the 'unknown' closure.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("parameters", SlotKind.Sequence, SlotKind.Name)]);

    public static readonly MistDiagnostic UnknownHasNoDefault = new(
        DiagnosticNumber.Mint("MIST0204"),
        Title: "Unknown has no default",
        Template: "'unknown' has no default value: no value is 'unknown' and nothing more. Give the slot a concrete type, or an explicit value.",
        Description: "No value is exactly 'unknown', so there is no default to fill a slot of that type. Give the slot a concrete type, or assign an explicit value.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ConditionalAccessOnNonOptional = new(
        DiagnosticNumber.Mint("MIST0205"),
        Title: "Conditional access on non-optional",
        Template: "The optional chaining operator '?.' requires a receiver of the form 'T | void', but found '{type}'.",
        Description: "The '?.' operator short-circuits on a 'void' alternative, and the receiver's type has none, so there is nothing to test. Use plain '.' access, or give the receiver an optional type.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic CoalescingAssignmentNotAStatement = new(
        DiagnosticNumber.Mint("MIST0206"),
        Title: "Coalescing assignment as expression",
        Template: "The void-coalescing assignment '??=' is a statement and cannot be used as a sub-expression.",
        Description: "The '??=' form is a statement and yields no value, so it cannot sit inside another expression. Move it onto its own line, or use '??' to compute a value.",
        Category: DiagnosticCategory.Syn,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ReadonlyLocalReassigned = new(
        DiagnosticNumber.Mint("MIST0207"),
        Title: "Readonly local reassigned",
        Template: "Cannot reassign readonly local variable '{variable}'.",
        Description: "A readonly local is bound once, and this statement assigns it again. Remove the assignment, or declare the local without 'readonly'.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("variable", SlotKind.Name)]);

    public static readonly MistDiagnostic ReadonlyReferenceWritten = new(
        DiagnosticNumber.Mint("MIST0208"),
        Title: "Write through readonly reference",
        Template: "'{target}' is a readonly reference, so it cannot be {action}. Read its value, or bind it to a 'readonly ref' local.",
        Description: "The reference grants read access only, and the code assigns through it or takes a mutable reference from it. Read the value instead, bind it to a 'readonly ref' local, or obtain a mutable reference from the owner.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("target", SlotKind.Name), new Slot("action", SlotKind.Enum)]);

    public static readonly MistDiagnostic ReferenceToNonLocation = new(
        DiagnosticNumber.Mint("MIST0209"),
        Title: "Reference to non-location",
        Template: "Cannot take a reference to {target}: it is not a storage location, so it cannot be {action}. Only a variable, a field, an array or span element, or a result returned by ref can be.",
        Description: "A 'ref' needs a storage location to alias, and the operand is a temporary value such as a call result or a literal. Store the value in a variable first and reference that.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("target", SlotKind.Name), new Slot("action", SlotKind.Enum)]);

    public static readonly MistDiagnostic NotAssignable = new(
        DiagnosticNumber.Mint("MIST0210"),
        Title: "Target not assignable",
        Template: "Cannot assign to {target}: the left-hand side of an assignment must be a variable, a field, a property, an indexer, an array or span element, or a result returned by ref.",
        Description: "The left-hand side of the assignment is a value, not a storage location, so there is nowhere to write. Assign to a variable, field, property, indexer, element, or ref-returned result instead.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("target", SlotKind.Name)]);

    public static readonly MistDiagnostic StaticLambdaCapture = new(
        DiagnosticNumber.Mint("MIST0211"),
        Title: "Static lambda captures",
        Template: "A static lambda cannot reference {kind} '{name}'.",
        Description: "A lambda marked static promises to close over nothing, and its body refers to an enclosing local, parameter, or 'this'. Pass the value as a parameter, or drop the 'static' modifier.",
        Category: DiagnosticCategory.Own,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("kind", SlotKind.Enum), new Slot("name", SlotKind.Name)]);

    public static readonly MistDiagnostic StaticLocalFunctionCapture = new(
        DiagnosticNumber.Mint("MIST0212"),
        Title: "Static local function captures",
        Template: "A static local function cannot reference {kind} '{name}'.",
        Description: "A local function marked static may not reach enclosing locals, parameters, or 'this', and this one does. Pass the value in explicitly, or remove 'static'.",
        Category: DiagnosticCategory.Own,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("kind", SlotKind.Enum), new Slot("name", SlotKind.Name)]);

    public static readonly MistDiagnostic OptionalOfOptional = new(
        DiagnosticNumber.Mint("MIST0213"),
        Title: "Optional of optional",
        Template: "The optional suffix '?' cannot be applied to '{type}', which already contains 'void'.",
        Description: "The type already admits 'void', so applying '?' again would add nothing and makes the intent unclear. Remove the redundant suffix.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic NoGetAccessor = new(
        DiagnosticNumber.Mint("MIST0214"),
        Title: "No get accessor",
        Template: "Cannot read '{member}': it has no 'get' accessor.",
        Description: "The property or indexer is read here but declares no getter. Add a 'get' accessor, or assign instead of reading.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name)]);

    public static readonly MistDiagnostic NoSetAccessor = new(
        DiagnosticNumber.Mint("MIST0215"),
        Title: "No set accessor",
        Template: "Cannot assign to '{member}': it has no 'set' or 'init' accessor.",
        Description: "The property or indexer is assigned here but declares neither a 'set' nor an 'init' accessor. Add one, or remove the assignment.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name)]);

    public static readonly MistDiagnostic InaccessibleAccessor = new(
        DiagnosticNumber.Mint("MIST0216"),
        Title: "Inaccessible accessor",
        Template: "The '{accessor}' accessor of '{member}' is inaccessible here: {accessibility} accessors are accessible only {scope}.",
        Description: "The property's accessor carries an accessibility modifier that excludes this use site even though the property itself is visible. Widen the accessor's accessibility, or use it from within its permitted scope.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("accessor", SlotKind.Enum), new Slot("member", SlotKind.Name), new Slot("accessibility", SlotKind.Enum), new Slot("scope", SlotKind.Name)]);

    public static readonly MistDiagnostic InitOnlyAssignment = new(
        DiagnosticNumber.Mint("MIST0217"),
        Title: "Init-only assignment",
        Template: "Cannot assign to '{member}' here: it is init-only, so it is assigned only in an object initializer, or on 'this' in a constructor or an 'init' accessor.",
        Description: "An 'init' accessor accepts writes only during construction: in an object initializer, or on 'this' inside a constructor or another 'init' accessor. Move the assignment there, or change the accessor to 'set'.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name)]);

    public static readonly MistDiagnostic InitializerMemberRepeated = new(
        DiagnosticNumber.Mint("MIST0218"),
        Title: "Initializer member repeated",
        Template: "Member '{member}' is assigned more than once in this initializer block.",
        Description: "The initializer block names the same member twice, so one assignment would overwrite the other. Keep a single entry for the member.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name)]);

    public static readonly MistDiagnostic InitializerKindsMixed = new(
        DiagnosticNumber.Mint("MIST0219"),
        Title: "Initializer kinds mixed",
        Template: "An initializer block either assigns members or adds collection elements, not both: this entry {entryKind}, but the block's first entry {firstKind}. Do it in a statement after the construction instead.",
        Description: "An initializer block is either a member initializer or a collection initializer, decided by its first entry, and this block contains both kinds. Move the other kind of entry into a statement after the construction.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("entryKind", SlotKind.Enum), new Slot("firstKind", SlotKind.Enum)]);

    public static readonly MistDiagnostic LocalReadBeforeAssignment = new(
        DiagnosticNumber.Mint("MIST0220"),
        Title: "Local read before assignment",
        Template: "Local variable '{variable}' is read before being definitely assigned.",
        Description: "Some path to this read leaves the local without a value. Assign the local on every path before reading it, or give it an initializer at its declaration.",
        Category: DiagnosticCategory.Flw,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("variable", SlotKind.Name)]);

    public static readonly MistDiagnostic LocalFunctionReadsUnassigned = new(
        DiagnosticNumber.Mint("MIST0221"),
        Title: "Local function reads unassigned local",
        Template: "Local function '{member}' reads '{variable}', which is not definitely assigned at this call.",
        Description: "The local function captures a local of its enclosing scope, and at this call site that local is not definitely assigned on every path. Assign the local before the call, or pass the value as a parameter.",
        Category: DiagnosticCategory.Flw,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name), new Slot("variable", SlotKind.Name)]);

    public static readonly MistDiagnostic FieldReadBeforeAssignment = new(
        DiagnosticNumber.Mint("MIST0222"),
        Title: "Field read before assignment",
        Template: "Field '{field}' is read before being definitely assigned in constructor '{constructor}'.",
        Description: "The constructor reads a field on a path where it has not yet been assigned. Assign the field earlier in the constructor, or give it an initializer.",
        Category: DiagnosticCategory.Flw,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("field", SlotKind.Name), new Slot("constructor", SlotKind.Name)]);

    public static readonly MistDiagnostic FieldNotDefinitelyAssigned = new(
        DiagnosticNumber.Mint("MIST0223"),
        Title: "Field not definitely assigned",
        Template: "Field '{field}' must be definitely assigned before constructor '{constructor}' completes. The field's type '{type}' is not defaultable and has no initializer.",
        Description: "The field's type has no default and the field has no initializer, so the constructor must assign it on every path before completing, and some path does not. Assign it on every path, add an initializer, or use a defaultable type.",
        Category: DiagnosticCategory.Flw,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("field", SlotKind.Name), new Slot("constructor", SlotKind.Name), new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic StampableDeclarationNotSatisfied = new(
        DiagnosticNumber.Mint("MIST0224"),
        Title: "Stampable declaration not satisfied",
        Template: "Struct '{type}' is declared 'stampable', but {reason}. Every instance storage type must be stampable; constrain stored type parameters with 'where T : stampable'.",
        Description: "A stampable struct is copied by stamping its bytes, so every instance storage it holds must itself be stampable, and one of this struct's is not. Change the field's type, or constrain stored type parameters with 'where T : stampable'.",
        Category: DiagnosticCategory.Own,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("reason", SlotKind.Name)]);

    public static readonly MistDiagnostic ArrayRequiresInitializer = new(
        DiagnosticNumber.Mint("MIST0225"),
        Title: "Array requires initializer",
        Template: "Cannot create a bare array 'new {element}[size]' because {reason}. Use 'new Array<{element}, Size>(i => ...)' to supply a value for each element, or use an array literal with explicit elements.",
        Description: "A bare 'new T[size]' fills each element with the element type's default, and this element type provides none. Build the array with 'Array<T, Size>' and an element factory, or write a literal with explicit elements.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("element", SlotKind.Type), new Slot("reason", SlotKind.Name)]);

    public static readonly MistDiagnostic StackallocElementNotStampable = new(
        DiagnosticNumber.Mint("MIST0226"),
        Title: "Stackalloc element not stampable",
        Template: "Cannot use '{element}' as a stackalloc element type because {reason}. Only primitives and structs whose fields are all stampable can be stack-allocated.",
        Description: "Stack allocation lays elements out as raw bytes, so the element type must be a primitive or a struct whose fields are all stampable. Use such a type, or allocate on the heap.",
        Category: DiagnosticCategory.Own,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("element", SlotKind.Type), new Slot("reason", SlotKind.Name)]);

    public static readonly MistDiagnostic UseAfterMove = new(
        DiagnosticNumber.Mint("MIST0227"),
        Title: "Use after move",
        Template: "Variable '{variable}' has been {state} and cannot be accessed here.",
        Description: "The variable's value has been moved or otherwise consumed earlier on this path, so the binding no longer holds anything. Reassign the variable before using it, or clone instead of moving.",
        Category: DiagnosticCategory.Own,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("variable", SlotKind.Name), new Slot("state", SlotKind.Enum)]);

    public static readonly MistDiagnostic LoopMove = new(
        DiagnosticNumber.Mint("MIST0228"),
        Title: "Owned variable moved in loop",
        Template: "Owned variable '{variable}' is moved inside a loop. Re-assign it before the next iteration or remove the move.",
        Description: "An owned variable moved inside a loop body is empty on the next iteration, which would then move nothing. Reassign the variable before the iteration ends, or hoist the move out of the loop.",
        Category: DiagnosticCategory.Own,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("variable", SlotKind.Name)]);

    public static readonly MistDiagnostic LeaseViolation = new(
        DiagnosticNumber.Mint("MIST0229"),
        Title: "Lease violation",
        Template: "Cannot {action} '{variable}' while a {lease} lease of it is outstanding.",
        Description: "A lease of this variable is still outstanding, and the operation would conflict with the access the lease grants. Perform the operation after the lease ends, or take a lease of a compatible kind.",
        Category: DiagnosticCategory.Own,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("variable", SlotKind.Name), new Slot("action", SlotKind.Enum), new Slot("lease", SlotKind.Enum)]);

    public static readonly MistDiagnostic IncompatibleLend = new(
        DiagnosticNumber.Mint("MIST0230"),
        Title: "Incompatible lend",
        Template: "Cannot pass {sourceKind} {source} to {targetKind} parameter '{parameter}': a {targetKind} parameter needs at least {targetKind} access.",
        Description: "The argument is lent with less access than the parameter's discipline requires. Pass a binding that offers at least the parameter's access, or weaken the parameter's requirement.",
        Category: DiagnosticCategory.Own,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("sourceKind", SlotKind.Enum), new Slot("source", SlotKind.Name), new Slot("targetKind", SlotKind.Enum), new Slot("parameter", SlotKind.Name)]);

    public static readonly MistDiagnostic ThisTierMismatch = new(
        DiagnosticNumber.Mint("MIST0231"),
        Title: "Insufficient this access for call",
        Template: "Cannot call '{member}', which requires {required} access to 'this': the receiver '{receiver}' provides {available} access.",
        Description: "The method requires a certain access tier to 'this', and the receiver binding offers a lower one. Call it through a receiver with sufficient access, or lower the method's declared tier if its body permits.",
        Category: DiagnosticCategory.Own,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name), new Slot("required", SlotKind.Enum), new Slot("receiver", SlotKind.Name), new Slot("available", SlotKind.Enum)]);

    public static readonly MistDiagnostic ThisTierInsufficient = new(
        DiagnosticNumber.Mint("MIST0232"),
        Title: "Declared this tier too low",
        Template: "Method '{member}' declares 'with {declared}' but its body requires at least {required} access to 'this'.",
        Description: "The method's 'with' clause declares one access tier to 'this', but the operations in its body need a higher one. Raise the declared tier, or remove the operations that need more access.",
        Category: DiagnosticCategory.Own,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name), new Slot("declared", SlotKind.Enum), new Slot("required", SlotKind.Enum)]);

    public static readonly MistDiagnostic SharedExtract = new(
        DiagnosticNumber.Mint("MIST0233"),
        Title: "Extract through shared binding",
        Template: "Cannot extract owned field '{field}' through unrestricted binding '{holder}': extracting from an owned field requires an owned binding to its holder.",
        Description: "Moving an owned field out of its holder leaves the holder partially empty, which is sound only when the binding to the holder is owned, and this binding is unrestricted. Hold the owner through an owned binding first, or clone the field instead of extracting it.",
        Category: DiagnosticCategory.Own,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("field", SlotKind.Name), new Slot("holder", SlotKind.Name)]);

    public static readonly MistDiagnostic CompositeState = new(
        DiagnosticNumber.Mint("MIST0234"),
        Title: "Composite state conflict",
        Template: "Cannot {action} '{variable}' as a whole while its owned field '{field}' is {state}.",
        Description: "The whole value is being moved, lent, or otherwise used while one of its owned fields has been moved out or is leased, so the composite is not currently whole. Restore the field or end its lease before using the holder as a whole.",
        Category: DiagnosticCategory.Own,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("action", SlotKind.Enum), new Slot("variable", SlotKind.Name), new Slot("field", SlotKind.Name), new Slot("state", SlotKind.Enum)]);

    public static readonly MistDiagnostic FieldStateCall = new(
        DiagnosticNumber.Mint("MIST0235"),
        Title: "Call touches unavailable field",
        Template: "Cannot call '{member}' on '{receiver}': it {action} owned field '{field}', which is {state}.",
        Description: "The called method reads or writes an owned field of the receiver that is currently moved out or leased. Restore the field or end the lease before the call.",
        Category: DiagnosticCategory.Own,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name), new Slot("receiver", SlotKind.Name), new Slot("action", SlotKind.Enum), new Slot("field", SlotKind.Name), new Slot("state", SlotKind.Enum)]);

    public static readonly MistDiagnostic OwnedAbstract = new(
        DiagnosticNumber.Mint("MIST0236"),
        Title: "Owned abstract type",
        Template: "The 'owned' modifier cannot be applied to abstract type '{type}'.",
        Description: "An abstract type does not own a concrete graph of storage, so 'owned' has nothing to govern there. Apply the modifier to the concrete class or struct instead.",
        Category: DiagnosticCategory.Own,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic OwnedFieldType = new(
        DiagnosticNumber.Mint("MIST0237"),
        Title: "Owned type field not ownable",
        Template: "{kind} '{member}' of owned type '{type}' has type '{memberType}', {reason}. Every field of an owned type must be an owned type, implement 'IClonable<T>' (declared or derived from fields that all do), or be a value type, so that the owned type owns its whole graph.",
        Description: "An owned type must own its whole graph, so every field must be an owned type, a value type, or implement 'IClonable<T>', and this field's type is none of these. Change the field's type, or give that type an 'IClonable<T>' implementation.",
        Category: DiagnosticCategory.Own,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("kind", SlotKind.Enum), new Slot("member", SlotKind.Name), new Slot("type", SlotKind.Type), new Slot("memberType", SlotKind.Type), new Slot("reason", SlotKind.Name)]);

    public static readonly MistDiagnostic OwnedSubstructuralBinding = new(
        DiagnosticNumber.Mint("MIST0238"),
        Title: "Owned on substructural type",
        Template: "The 'owned' specifier cannot be applied to '{member}' of {discipline} type '{type}': a substructural type carries its own discipline.",
        Description: "An affine or linear type already dictates how its values move, so marking a binding of it 'owned' is redundant and contradictory. Remove the specifier.",
        Category: DiagnosticCategory.Own,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name), new Slot("discipline", SlotKind.Enum), new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic ConflictingDiscipline = new(
        DiagnosticNumber.Mint("MIST0239"),
        Title: "Conflicting disciplines",
        Template: "'{member}' cannot be both {discipline} and 'owned': {reason}.",
        Description: "A declaration combines 'owned' with another discipline whose rules cannot both hold at once. Keep the one discipline the type is meant to have.",
        Category: DiagnosticCategory.Own,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name), new Slot("discipline", SlotKind.Enum), new Slot("reason", SlotKind.Name)]);

    public static readonly MistDiagnostic FieldDisciplineContainment = new(
        DiagnosticNumber.Mint("MIST0240"),
        Title: "Field discipline containment",
        Template: "{discipline} type '{type}' cannot contain {fieldDiscipline} field '{field}': {reason}.",
        Description: "A type of one discipline may contain fields only of disciplines it can enforce, and this field's discipline is stronger than its container's. Move the field to a type of a matching discipline, or change the container's discipline.",
        Category: DiagnosticCategory.Own,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("discipline", SlotKind.Enum), new Slot("type", SlotKind.Type), new Slot("fieldDiscipline", SlotKind.Enum), new Slot("field", SlotKind.Name), new Slot("reason", SlotKind.Name)]);

    public static readonly MistDiagnostic FieldTypeParameterUnconstrained = new(
        DiagnosticNumber.Mint("MIST0241"),
        Title: "Unconstrained type parameter field",
        Template: "{discipline} type '{type}' contains field '{field}' of unconstrained type parameter '{parameter}'. Add a substructural constraint, such as 'where {parameter} : {required}'.",
        Description: "A disciplined type contains a field whose type parameter has no substructural constraint, so an argument could carry a discipline the container cannot enforce. Add a constraint such as 'where T : affine' matching the container.",
        Category: DiagnosticCategory.Own,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("discipline", SlotKind.Enum), new Slot("type", SlotKind.Type), new Slot("field", SlotKind.Name), new Slot("parameter", SlotKind.Name), new Slot("required", SlotKind.Enum)]);

    public static readonly MistDiagnostic RefOnClass = new(
        DiagnosticNumber.Mint("MIST0242"),
        Title: "Ref modifier on class",
        Template: "The 'ref' modifier is only valid on struct declarations; '{type}' is a class.",
        Description: "Only structs can be confined to the stack, so 'ref' has no meaning on a class declaration. Remove the modifier, or declare the type as a struct.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic RefStructField = new(
        DiagnosticNumber.Mint("MIST0243"),
        Title: "Ref struct field in non-ref type",
        Template: "Ref struct '{type}' cannot be a field of '{container}', which is not a ref struct.",
        Description: "A ref struct must stay on the stack, and storing it in a field of an ordinary type would let it live on the heap. Make the containing type a ref struct, or store a non-ref value.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("container", SlotKind.Type)]);

    public static readonly MistDiagnostic RefStructTypeArgument = new(
        DiagnosticNumber.Mint("MIST0244"),
        Title: "Ref struct as type argument",
        Template: "Ref struct '{type}' cannot be a type argument.",
        Description: "Generic instantiations may place their type arguments on the heap, which a ref struct cannot survive. Use a non-ref type, or write a specialized non-generic type.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic RefStructProperty = new(
        DiagnosticNumber.Mint("MIST0245"),
        Title: "Ref struct property in non-ref type",
        Template: "Ref struct '{type}' cannot be a property of '{container}', which is not a ref struct.",
        Description: "A property backed by a ref struct in a type that is not a ref struct could escape the stack. Make the containing type a ref struct, or expose a non-ref value.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("container", SlotKind.Type)]);

    public static readonly MistDiagnostic RefStructStaticStorage = new(
        DiagnosticNumber.Mint("MIST0246"),
        Title: "Ref struct in static storage",
        Template: "Ref struct '{type}' cannot be stored statically in '{container}': static storage lives on the heap.",
        Description: "Static storage lives on the heap for the program's lifetime, and a ref struct is confined to the stack. Store a non-ref type statically, or create the ref struct locally where it is used.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("container", SlotKind.Type)]);

    public static readonly MistDiagnostic RefStructUnionConstituent = new(
        DiagnosticNumber.Mint("MIST0247"),
        Title: "Ref struct in union",
        Template: "Ref struct '{type}' cannot be a union constituent: a union value can be boxed, and a ref struct cannot leave the stack.",
        Description: "A union value may be boxed, which would move a ref struct constituent to the heap. Remove the ref struct from the union, or use a non-ref wrapper.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic RefStructOptional = new(
        DiagnosticNumber.Mint("MIST0248"),
        Title: "Optional ref struct",
        Template: "Ref struct '{type}' cannot be optional: an optional value can be boxed, and a ref struct cannot leave the stack.",
        Description: "An optional may be boxed, so a ref struct cannot be the value it wraps. Use a non-ref type, or signal absence another way such as an empty span.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic RefStructElement = new(
        DiagnosticNumber.Mint("MIST0249"),
        Title: "Ref struct as element",
        Template: "Ref struct '{type}' cannot be {position}: {reason}.",
        Description: "The ref struct is used as an element of a collection or sequence type whose storage can leave the stack. Use a stack span of the ref struct, or a non-ref element type.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("position", SlotKind.Enum), new Slot("reason", SlotKind.Name)]);

    public static readonly MistDiagnostic RefStructBoxed = new(
        DiagnosticNumber.Mint("MIST0250"),
        Title: "Ref struct boxed by conversion",
        Template: "Ref struct '{source}' cannot be converted to '{target}': the conversion would box it, and a ref struct cannot leave the stack.",
        Description: "Converting a ref struct to this type would box it onto the heap, which a stack-confined value cannot allow. Keep the value in its ref struct type, or convert a copy of the data it refers to.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("source", SlotKind.Type), new Slot("target", SlotKind.Type)]);

    public static readonly MistDiagnostic FieldAssignedInReadonlyMember = new(
        DiagnosticNumber.Mint("MIST0251"),
        Title: "Field assigned in readonly member",
        Template: "Cannot assign to field '{field}' within readonly member '{member}'.",
        Description: "A readonly member promises not to mutate its instance, and its body writes a field. Remove the write, or drop the readonly modifier from the member.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("field", SlotKind.Name), new Slot("member", SlotKind.Name)]);

    public static readonly MistDiagnostic FieldAssignedOnReadonlyParameter = new(
        DiagnosticNumber.Mint("MIST0252"),
        Title: "Field assigned on readonly parameter",
        Template: "Cannot assign to field '{field}' of {receiver}.",
        Description: "The parameter is passed readonly, so the fields of the value it names cannot be written. Take the parameter mutably, or copy the value before modifying it.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("field", SlotKind.Name), new Slot("receiver", SlotKind.Name)]);

    public static readonly MistDiagnostic ReadonlyParameterCallsMutable = new(
        DiagnosticNumber.Mint("MIST0253"),
        Title: "Mutating call on readonly parameter",
        Template: "Cannot call '{member}' on {receiver}: it is not readonly.",
        Description: "A readonly parameter may only invoke members that are themselves readonly, and the called member is not. Call a readonly member, or take the parameter mutably.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name), new Slot("receiver", SlotKind.Name)]);

    public static readonly MistDiagnostic ReadonlyParameterSetsProperty = new(
        DiagnosticNumber.Mint("MIST0254"),
        Title: "Property set on readonly parameter",
        Template: "Cannot set '{member}' on {receiver}: a readonly parameter admits no setter.",
        Description: "Setting a property runs a setter that mutates the instance, which a readonly parameter forbids. Take the parameter mutably, or set the property on a copy.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name), new Slot("receiver", SlotKind.Name)]);

    public static readonly MistDiagnostic FieldAssignedInReadonlyBlock = new(
        DiagnosticNumber.Mint("MIST0255"),
        Title: "Field assigned in readonly block",
        Template: "Cannot assign to field '{field}' inside a readonly block, which admits no field write and suspends a constructor's privilege to make one.",
        Description: "Inside a readonly block no field may be written, even in a constructor that could otherwise assign freely. Move the assignment outside the block.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("field", SlotKind.Name)]);

    public static readonly MistDiagnostic ReadonlyContextWritesThroughIndex = new(
        DiagnosticNumber.Mint("MIST0256"),
        Title: "Index write in readonly context",
        Template: "Cannot write through an index into '{receiver}' from readonly {scope}: a readonly context mutates nothing the value reaches.",
        Description: "A readonly context must leave everything reachable from the value untouched, and an indexed assignment into it mutates reachable storage. Perform the write outside the readonly context, or on an owned copy.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("receiver", SlotKind.Name), new Slot("scope", SlotKind.Name)]);

    public static readonly MistDiagnostic ReadonlyContextCallsMutable = new(
        DiagnosticNumber.Mint("MIST0257"),
        Title: "Mutating call in readonly context",
        Template: "Cannot call '{member}' from readonly {scope}: it is not readonly. A non-readonly call requires a local member on an owned receiver.",
        Description: "A readonly context may call non-readonly members only on receivers it owns locally, and this receiver is not one. Call a readonly member, or make the receiver a local owned value.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name), new Slot("scope", SlotKind.Name)]);

    public static readonly MistDiagnostic ReadonlyContextFormatsMutable = new(
        DiagnosticNumber.Mint("MIST0258"),
        Title: "Formatting mutable type in readonly context",
        Template: "Cannot use '{argument}' as type argument '{parameter}': readonly {scope} formats it, and the 'ToString' of '{type}' is not readonly.",
        Description: "Formatting runs the type argument's 'ToString', which in this case is not readonly and so cannot run inside a readonly context. Mark that 'ToString' readonly, or format outside the context.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("argument", SlotKind.Type), new Slot("parameter", SlotKind.Name), new Slot("scope", SlotKind.Name), new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic ReadonlyContextSetsProperty = new(
        DiagnosticNumber.Mint("MIST0259"),
        Title: "Property set in readonly context",
        Template: "Cannot set '{member}' from readonly {scope}: a readonly context calls no setter.",
        Description: "Running a setter mutates its instance, which a readonly context forbids. Move the assignment outside the context, or set the property on an owned local.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name), new Slot("scope", SlotKind.Name)]);

    public static readonly MistDiagnostic ReadonlyContextAllocatesMutable = new(
        DiagnosticNumber.Mint("MIST0260"),
        Title: "Allocation in readonly context",
        Template: "Cannot allocate '{type}' from readonly {scope}: its constructor is not readonly.",
        Description: "Constructing the type runs a constructor that is not readonly, so the allocation cannot happen inside a readonly context. Mark the constructor readonly, or allocate outside the context.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("scope", SlotKind.Name)]);

    public static readonly MistDiagnostic ReadonlyContextWritesThroughRef = new(
        DiagnosticNumber.Mint("MIST0261"),
        Title: "Ref write in readonly context",
        Template: "Cannot write through '{reference}' from readonly {scope}: it aliases storage outside what the context owns, and a readonly context writes nothing external.",
        Description: "The reference aliases storage the readonly context does not own, and writing through it would mutate something external. Write through the reference outside the context, or operate on an owned local.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("reference", SlotKind.Name), new Slot("scope", SlotKind.Name)]);

    public static readonly MistDiagnostic ReadonlyFieldAssigned = new(
        DiagnosticNumber.Mint("MIST0262"),
        Title: "Readonly field assigned",
        Template: "Cannot assign to readonly field '{field}' outside a constructor of '{type}'.",
        Description: "A readonly field is assigned only while its declaring type is being constructed, and this write is elsewhere. Move the assignment into a constructor or initializer, or remove 'readonly' from the field.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("field", SlotKind.Name), new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic OwnedReturnNotOwned = new(
        DiagnosticNumber.Mint("MIST0263"),
        Title: "Returned value not owned",
        Template: "'{member}' returns owned but this value is not: {reason}. An owned return is a construction, a clone, a call returning owned, a move from an owned binding, or a value of a type that cannot change.",
        Description: "The member promises an owned result, meaning nothing else holds it, and the returned expression does not qualify. Return a fresh construction, a clone, a move from an owned binding, a call returning owned, or a value of an immutable type.",
        Category: DiagnosticCategory.Own,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name), new Slot("reason", SlotKind.Name)]);

    public static readonly MistDiagnostic OwnedSourceNotOwned = new(
        DiagnosticNumber.Mint("MIST0264"),
        Title: "Moved value not owned",
        Template: "Cannot move this value into {destination}: {reason}. An owned destination takes only what nothing else holds: a construction, a clone, a call returning owned, a move from an owned binding, or a value of a type that cannot change.",
        Description: "An owned destination may receive only a value nothing else holds, and the source expression could be aliased. Clone the value, construct a fresh one, or move it from an owned binding.",
        Category: DiagnosticCategory.Own,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("destination", SlotKind.Name), new Slot("reason", SlotKind.Name)]);

    public static readonly MistDiagnostic StackSpanReturned = new(
        DiagnosticNumber.Mint("MIST0265"),
        Title: "Stack span returned",
        Template: "This '{type}' refers to {origin}, so it cannot be returned: it would outlive its stack frame.",
        Description: "The span refers to stack memory of the current frame and would dangle once the frame returns. Return a heap-backed span or a copy of the data instead.",
        Category: DiagnosticCategory.Own,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("origin", SlotKind.Name)]);

    public static readonly MistDiagnostic StackSpanStored = new(
        DiagnosticNumber.Mint("MIST0266"),
        Title: "Stack span stored",
        Template: "This '{type}' refers to {origin}, so it cannot be stored in {destination}: {reason}.",
        Description: "The span refers to stack memory and the storage it is being written to outlives the current frame. Copy the data to heap storage, or keep the span in a local.",
        Category: DiagnosticCategory.Own,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("origin", SlotKind.Name), new Slot("destination", SlotKind.Name), new Slot("reason", SlotKind.Name)]);

    public static readonly MistDiagnostic StackSpanPassed = new(
        DiagnosticNumber.Mint("MIST0267"),
        Title: "Stack span passed with sink",
        Template: "This '{type}' refers to {origin}, so it cannot be passed to '{member}' alongside {sink}, which '{member}' could store it in: {reason}.",
        Description: "The span refers to stack memory and is passed alongside an argument through which the callee could store it, so the callee might make the span outlive its frame. Pass a heap-backed span, or do not pass the storage target in the same call.",
        Category: DiagnosticCategory.Own,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("origin", SlotKind.Name), new Slot("member", SlotKind.Name), new Slot("sink", SlotKind.Name), new Slot("reason", SlotKind.Name)]);

    public static readonly MistDiagnostic RefReturnEscapes = new(
        DiagnosticNumber.Mint("MIST0268"),
        Title: "Ref return escapes frame",
        Template: "Cannot return {value} by reference: it refers to {origin}, which does not outlive the call.",
        Description: "The reference points at storage that ends with the call, so returning it by reference would leave the caller with a dangling reference. Return the value by copy, or reference storage that outlives the call.",
        Category: DiagnosticCategory.Own,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("value", SlotKind.Name), new Slot("origin", SlotKind.Name)]);

    public static readonly MistDiagnostic CapturedBinding = new(
        DiagnosticNumber.Mint("MIST0269"),
        Title: "Disciplined binding captured",
        Template: "Cannot capture {discipline} variable '{variable}' in a {closure}: {reason}.",
        Description: "A lambda or local function captures a binding whose discipline, such as owned or ref, cannot be enforced across the capture. Pass the value as a parameter, clone it, or avoid the capture.",
        Category: DiagnosticCategory.Own,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("discipline", SlotKind.Enum), new Slot("variable", SlotKind.Name), new Slot("closure", SlotKind.Enum), new Slot("reason", SlotKind.Name)]);

    public static readonly MistDiagnostic RegexInvalid = new(
        DiagnosticNumber.Mint("MIST0270"),
        Title: "Invalid regex pattern",
        Template: "Invalid regex pattern: {message}",
        Description: "The regular expression literal fails to parse, for the reason the regex engine reports. Correct the pattern syntax.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("message", SlotKind.Text)]);

    public static readonly MistDiagnostic RefinementViolated = new(
        DiagnosticNumber.Mint("MIST0271"),
        Title: "Refinement violated",
        Template: "Value '{value}' does not satisfy refinement '{predicate}' on type '{type}'.",
        Description: "The constant value is known at compile time and fails the refinement predicate of its target type. Use a value the refinement admits.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("value", SlotKind.Name), new Slot("predicate", SlotKind.Name), new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic RefinementUnproven = new(
        DiagnosticNumber.Mint("MIST0272"),
        Title: "Refinement unproven",
        Template: "Cannot assign '{source}' to '{target}' without proving its refinement '{predicate}': narrow the value with a pattern first.",
        Description: "The value's type is the refined type's base, and nothing on this path establishes that the predicate holds. Test the value with a pattern first, or produce it from a source already typed with the refinement.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("source", SlotKind.Type), new Slot("target", SlotKind.Type), new Slot("predicate", SlotKind.Name)]);

    public static readonly MistDiagnostic RefinementNotImplied = new(
        DiagnosticNumber.Mint("MIST0273"),
        Title: "Refinement not implied",
        Template: "Cannot assign '{source}' to '{target}': '{known}' does not imply '{required}'.",
        Description: "The source carries one refinement and the target demands another, and the first does not entail the second. Narrow the value with a pattern that proves the target refinement.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("source", SlotKind.Type), new Slot("target", SlotKind.Type), new Slot("known", SlotKind.Name), new Slot("required", SlotKind.Name)]);

    public static readonly MistDiagnostic IndexOutOfBounds = new(
        DiagnosticNumber.Mint("MIST0274"),
        Title: "Index out of bounds",
        Template: "Index {index} is outside the bounds of '{target}', which needs {predicate}.",
        Description: "The constant index lies outside the range the indexed collection's dependent bound permits. Use an index within the collection's length.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("index", SlotKind.Name), new Slot("target", SlotKind.Name), new Slot("predicate", SlotKind.Name)]);

    public static readonly MistDiagnostic IndexUnproven = new(
        DiagnosticNumber.Mint("MIST0275"),
        Title: "Index not proven in bounds",
        Template: "Index '{index}' is not proven within the bounds of '{target}', which needs {predicate}.",
        Description: "The indexed collection's length is a dependent type, and nothing on this path proves the index lies within it. Guard the access with a bounds pattern, or use an index typed to the collection's length.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("index", SlotKind.Name), new Slot("target", SlotKind.Name), new Slot("predicate", SlotKind.Name)]);

    public static readonly MistDiagnostic ConstraintViolated = new(
        DiagnosticNumber.Mint("MIST0276"),
        Title: "Refinement constraint violated",
        Template: "Value '{value}' violates the refinement constraint '{predicate}' on parameter '{parameter}'.",
        Description: "The constant argument fails the refinement constraint on the parameter it is passed to. Pass a value the constraint admits.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("value", SlotKind.Name), new Slot("predicate", SlotKind.Name), new Slot("parameter", SlotKind.Name)]);

    public static readonly MistDiagnostic ConstraintUnproven = new(
        DiagnosticNumber.Mint("MIST0277"),
        Title: "Refinement constraint unproven",
        Template: "Argument '{argument}' is not proven to satisfy the refinement constraint '{predicate}' on parameter '{parameter}'.",
        Description: "The argument's type does not establish the refinement constraint on the parameter, and no narrowing on this path proves it. Test the argument with a pattern first, or pass a value already typed with the refinement.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("argument", SlotKind.Name), new Slot("predicate", SlotKind.Name), new Slot("parameter", SlotKind.Name)]);

    public static readonly MistDiagnostic DependentArgumentViolated = new(
        DiagnosticNumber.Mint("MIST0278"),
        Title: "Dependent argument violated",
        Template: "Dependent type argument '{argument}' does not satisfy '{predicate}' on parameter '{parameter}'.",
        Description: "The argument's dependent type does not meet the dependent constraint the parameter places on it. Pass an argument whose dependent type satisfies the constraint.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("argument", SlotKind.Name), new Slot("predicate", SlotKind.Name), new Slot("parameter", SlotKind.Name)]);

    public static readonly MistDiagnostic DependentForwardUnproven = new(
        DiagnosticNumber.Mint("MIST0279"),
        Title: "Dependent forwarding unproven",
        Template: "Dependent parameter '{parameter}' cannot be forwarded to '{target}': {known} is not proven to imply '{required}'.",
        Description: "A dependent parameter is forwarded to another call whose parameter demands more than the forwarder's own constraint guarantees. Strengthen the forwarder's constraint, or prove the stronger condition before the call.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("parameter", SlotKind.Name), new Slot("target", SlotKind.Name), new Slot("known", SlotKind.Name), new Slot("required", SlotKind.Name)]);

    public static readonly MistDiagnostic DependentArgumentNotAssignable = new(
        DiagnosticNumber.Mint("MIST0280"),
        Title: "Dependent argument not assignable",
        Template: "Dependent type argument '{argument}' of type '{argumentType}' is not assignable to dependent parameter '{parameter}' of type '{parameterType}'.",
        Description: "The dependent type argument has a type that does not convert to the dependent parameter's declared type. Pass an argument of the parameter's type.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("argument", SlotKind.Name), new Slot("argumentType", SlotKind.Type), new Slot("parameter", SlotKind.Name), new Slot("parameterType", SlotKind.Type)]);

    public static readonly MistDiagnostic DependentForwardNotConvertible = new(
        DiagnosticNumber.Mint("MIST0281"),
        Title: "Dependent forwarding not convertible",
        Template: "Dependent type parameter '{parameter}' of type '{type}' on '{source}' cannot be forwarded to parameter '{targetParameter}' of type '{targetType}' on '{target}' because '{type}' is not implicitly convertible to '{targetType}'.",
        Description: "The dependent parameter being forwarded has a type that does not implicitly convert to the type of the callee's dependent parameter. Convert the value explicitly, or align the two parameter types.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("parameter", SlotKind.Name), new Slot("type", SlotKind.Type), new Slot("source", SlotKind.Name), new Slot("targetParameter", SlotKind.Name), new Slot("targetType", SlotKind.Type), new Slot("target", SlotKind.Name)]);

    public static readonly MistDiagnostic DependentParameterConflictsWithMember = new(
        DiagnosticNumber.Mint("MIST0282"),
        Title: "Dependent parameter name conflict",
        Template: "Dependent type parameter '{parameter}' conflicts with member '{parameter}' declared in type '{type}'. Rename either the parameter or the member.",
        Description: "A dependent type parameter shares its name with a member of the declaring type, so references inside the type would be ambiguous. Rename one of them.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("parameter", SlotKind.Name), new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic ConstraintStrengthened = new(
        DiagnosticNumber.Mint("MIST0283"),
        Title: "Implementation strengthens constraint",
        Template: "'{member}' constrains parameter '{parameter}' more than the member it implements: '{known}' does not imply '{required}'.",
        Description: "The implementing member places a refinement constraint on a parameter that the member it implements does not, so a caller through the base could pass a value the implementation rejects. Weaken the constraint, or add it to the implemented member.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name), new Slot("parameter", SlotKind.Name), new Slot("known", SlotKind.Name), new Slot("required", SlotKind.Name)]);

    public static readonly MistDiagnostic RefinementReturnUnproven = new(
        DiagnosticNumber.Mint("MIST0284"),
        Title: "Refinement return unproven",
        Template: "Member '{member}' produces '{type}', which is not proven to satisfy the refinement contract '{predicate}' required by '{slot}'.",
        Description: "The member implements a contract whose result carries a refinement, and the value it produces is not proven to satisfy that refinement. Return a value typed with the refinement, or narrow it with a pattern before returning.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name), new Slot("type", SlotKind.Type), new Slot("predicate", SlotKind.Name), new Slot("slot", SlotKind.Name)]);

    public static readonly MistDiagnostic BaseConstructorUnsatisfied = new(
        DiagnosticNumber.Mint("MIST0285"),
        Title: "Base constructor arguments missing",
        Template: "Base class '{type}' requires constructor arguments that are not provided.",
        Description: "The base class has no parameterless constructor, and the derived constructor passes no arguments to any of its constructors. Add a base constructor call with arguments, or give the base a parameterless constructor.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic PropertyHasNoDefault = new(
        DiagnosticNumber.Mint("MIST0286"),
        Title: "Property without default",
        Template: "Property '{property}' of '{type}' has no default, which '{type}' needs because it declares {declaration}: its type '{propertyType}' has none. Give the property an initializer, or give '{type}' a constructor that assigns it and no default block.",
        Description: "The containing type promises a default, which must give every property a value, and this property's type has none. Give the property an initializer, or replace the type's default with a constructor that assigns it.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("property", SlotKind.Name), new Slot("type", SlotKind.Type), new Slot("declaration", SlotKind.Enum), new Slot("propertyType", SlotKind.Type)]);

    public static readonly MistDiagnostic RefinementZeroNotAdmitted = new(
        DiagnosticNumber.Mint("MIST0287"),
        Title: "Refined default rejects zero",
        Template: "'{member}' of '{type}' has no default, which '{type}' needs because it declares {declaration}: its type '{refined}' would start from the zero of '{base}', which '{refined}' does not admit. Give '{member}' a value '{refined}' admits, by an initializer or a default block entry, or give '{type}' a constructor that assigns it and no default block.",
        Description: "The containing type promises a default, and a refined member of it would start from the zero of its underlying type, which the refinement does not admit. Initialize the member with an admitted value, through an initializer or a default block entry, or replace the type's default with a constructor that assigns it.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name), new Slot("type", SlotKind.Type), new Slot("declaration", SlotKind.Enum), new Slot("refined", SlotKind.Type), new Slot("base", SlotKind.Type)]);
}