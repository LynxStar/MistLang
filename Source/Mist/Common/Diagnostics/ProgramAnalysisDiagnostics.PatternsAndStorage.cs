namespace Mist.Common.Diagnostics;

public static partial class ProgramAnalysisDiagnostics
{
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
}