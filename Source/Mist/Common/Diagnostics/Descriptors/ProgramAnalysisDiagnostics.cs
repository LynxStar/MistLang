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
        Template: "Inconsistent accessibility: {exposedKind} '{exposed}{exposedMember}' is less accessible than {ownerKind} '{owner}'.",
        Description: "A declaration exposes a type that is less accessible than the declaration itself, so some callers could see the member but not its type. Widen the type's accessibility, or narrow the member's to match.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("exposedKind", SlotKind.Enum), new Slot("exposed", SlotKind.Optional, SlotKind.Type), new Slot("exposedMember", SlotKind.Optional, SlotKind.Symbol), new Slot("ownerKind", SlotKind.Enum), new Slot("owner", SlotKind.Name)]);

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
}