namespace Mist.Common.Diagnostics;

public static partial class ProgramModelDiagnostics
{
    public static readonly MistDiagnostic PatternTypeNotAConstituent = new(
        DiagnosticNumber.Mint("MIST0345"),
        Title: "Pattern type not a constituent",
        Template: "'{tested}' is not one of the types '{union}' can hold, so this pattern can never match.",
        Description: "A type pattern tests a union-typed value for a type that none of the union's constituents can hold. Narrowing a union goes through the accessor for the matching constituent, so a type outside the union has no lowering rather than merely a test that always fails; test for one of the constituent types instead.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("tested", SlotKind.Type), new Slot("union", SlotKind.Type)]);

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
        Template: "The arms of the switch expression have no common type ('{first}' and '{second}'). Annotate the target with the type you want.",
        Description: "The arms of a switch expression produce different types and no target type says which is wanted; the compiler does not yet compute a best common type by conversion or numeric promotion, so it asks rather than guesses. Annotate the target with the intended type, or convert the arms to one type.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("first", SlotKind.Type), new Slot("second", SlotKind.Type)]);

    public static readonly MistDiagnostic PatternBindingNotEstablished = new(
        DiagnosticNumber.Mint("MIST0348"),
        Title: "Binding inside or/not pattern",
        Template: "The pattern variable '{name}' cannot be introduced inside 'or' or 'not', because matching there does not establish a value for it.",
        Description: "A pattern nested under 'or' or 'not' declares a variable, but a disjunction only proves that some operand matched and a negation only proves its operand did not, so no value exists for the name to hold; allowing it would declare a local that no emitted pattern produces. Hoist the binding above the 'or' or 'not', or drop it.",
        Category: DiagnosticCategory.Flw,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name)]);

    public static readonly MistDiagnostic PatternSubpatternNotStorage = new(
        DiagnosticNumber.Mint("MIST0349"),
        Title: "Subpattern target not storage",
        Template: "'{member}' on '{type}' is not a field or a property, so a property pattern cannot match against it.",
        Description: "A property pattern names a member of the scrutinee that is a method, nested type or other non-storage member, which holds no value for the subpattern to be tested against. Match against a field or a property instead.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("member", SlotKind.Name)]);

    public static readonly MistDiagnostic PatternSubpatternNotInstance = new(
        DiagnosticNumber.Mint("MIST0350"),
        Title: "Subpattern target is static",
        Template: "'{member}' on '{type}' is static, so a property pattern cannot match against it.",
        Description: "A property pattern names a static member, but a pattern tests the state of the particular value being matched and a static member belongs to no instance. Match against an instance field or property.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("member", SlotKind.Name)]);

    public static readonly MistDiagnostic TuplePatternArityMismatch = new(
        DiagnosticNumber.Mint("MIST0351"),
        Title: "Tuple pattern arity mismatch",
        Template: "The pattern spells {actual} elements but '{type}' holds {expected}, so this pattern can never match.",
        Description: "A tuple pattern lists a different number of element patterns than the matched tuple has elements; the arity pairs each element pattern with the type it narrows, so a mismatch leaves positions typed by nothing. Make the pattern's element count equal to the tuple's.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("expected", SlotKind.Quantity), new Slot("actual", SlotKind.Quantity)]);

    public static readonly MistDiagnostic TuplePatternScrutineeNotATuple = new(
        DiagnosticNumber.Mint("MIST0352"),
        Title: "Tuple pattern on non-tuple",
        Template: "'{type}' is not a tuple, so a tuple pattern has no elements to match against it.",
        Description: "A tuple pattern is applied to a value that is neither a tuple nor 'unknown', and Mist has no user-declared deconstruction to read element positions out of other types. Match the value with a type or property pattern instead.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic ReceiverNotSliceable = new(
        DiagnosticNumber.Mint("MIST0353"),
        Title: "Receiver not sliceable",
        Template: "'{type}' cannot be sliced by a range. A sub-sequence comes from an array, a span or a string, or from a type with an int Length or Count and either a Range indexer or a Slice(int, int) method.",
        Description: "A range index is applied to a value whose type offers no way to take a sub-sequence. Slice an array, span or string, or give the type an int Length or Count together with a Range indexer or a Slice(int, int) method.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic ReceiverNotIndexable = new(
        DiagnosticNumber.Mint("MIST0354"),
        Title: "Receiver not indexable",
        Template: "'{type}' cannot be indexed because it declares no indexer and is not an array.",
        Description: "An element access is applied to a value that is not an array and whose type declares no indexer. Index a collection type, or declare an indexer on the type.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic ReceiverNotCountable = new(
        DiagnosticNumber.Mint("MIST0355"),
        Title: "Receiver not countable",
        Template: "'{type}' has no int Length or Count, so it cannot be indexed from its end or matched by a list pattern.",
        Description: "An index from the end or a list pattern is applied to a value whose type has no int Length or Count, so there is no count to position against; the model refuses it here rather than hand the program to the backend to reject. Add a Length or Count property to the type, or index from the front.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic ListPatternScrutineeNotIndexable = new(
        DiagnosticNumber.Mint("MIST0356"),
        Title: "List pattern on non-indexable",
        Template: "'{type}' cannot be indexed, so a list pattern has no elements to match against it.",
        Description: "A list pattern is applied to a value that cannot be indexed, and Mist has no user-declared deconstruction, so the pattern has no elements to read and nothing for its arity to constrain. Match an array or an indexable type with a count.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type)]);

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
        Template: "'{type}' takes {expected} parameters but the lambda declares {actual}.",
        Description: "A lambda declares a different number of parameters than the function type it is assigned to takes. Match the lambda's parameter list to the target type, or change the target.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("expected", SlotKind.Quantity), new Slot("actual", SlotKind.Quantity)]);

    public static readonly MistDiagnostic LambdaParameterNeedsAType = new(
        DiagnosticNumber.Mint("MIST0360"),
        Title: "Untyped lambda parameter",
        Template: "The type of lambda parameter '{name}' cannot be inferred. Annotate it, or assign the lambda to a function type.",
        Description: "A lambda parameter has no type annotation and no target function type supplies one; the body is checked against the parameter types, so inferring them from the body would be circular. Annotate the parameter, or assign the lambda to a function type.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name)]);

    public static readonly MistDiagnostic LambdaReturnsNoCommonType = new(
        DiagnosticNumber.Mint("MIST0361"),
        Title: "Lambda returns lack common type",
        Template: "The lambda's returns have no common type ('{first}' and '{second}'). Annotate the target with a function type to select the result.",
        Description: "The return statements of a block-bodied lambda produce values of different types and nothing selects a result type. Assign the lambda to a function type that names the result, or convert the returns to one type.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("first", SlotKind.Type), new Slot("second", SlotKind.Type)]);

    public static readonly MistDiagnostic FunctionCallArityMismatch = new(
        DiagnosticNumber.Mint("MIST0362"),
        Title: "Function call arity mismatch",
        Template: "'{type}' takes {expected} arguments but {actual} were passed.",
        Description: "A call through a function-typed value passes a number of arguments different from the number its type declares. Pass exactly as many arguments as the function type takes.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("expected", SlotKind.Quantity), new Slot("actual", SlotKind.Quantity)]);

    public static readonly MistDiagnostic NotCallable = new(
        DiagnosticNumber.Mint("MIST0363"),
        Title: "Not callable",
        Template: "'{name}' is not a method, and its type '{type}' is not a function type, so it cannot be called.{namespace? An extension method named '{name}' is declared in {namespace}, which is not imported here.}",
        Description: "A call is made on something that is neither a method nor a value of function type. Call a method or a function-typed value, or check that the name resolves to what you intended.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name), new Slot("type", SlotKind.Type), new Slot("namespace", SlotKind.Optional, SlotKind.Sequence)],
        Related: [DiagnosticNumber.Mint("MIST0364")]);

    public static readonly MistDiagnostic ValueNotCallable = new(
        DiagnosticNumber.Mint("MIST0364"),
        Title: "Value not callable",
        Template: "A value of type '{type}' is not a function, so it cannot be called.",
        Description: "A call is applied to a value whose type is not a function type. Only methods and function-typed values can be invoked; check that the expression is the callable you meant.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type)],
        Related: [DiagnosticNumber.Mint("MIST0363")]);

    public static readonly MistDiagnostic DeconstructionArityMismatch = new(
        DiagnosticNumber.Mint("MIST0365"),
        Title: "Deconstruction arity mismatch",
        Template: "The deconstruction declares {targetCount} targets but '{type}' holds {elementCount} elements.",
        Description: "A deconstruction declares a different number of targets than the tuple it reads has elements. Declare exactly one target per element.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("elementCount", SlotKind.Quantity), new Slot("targetCount", SlotKind.Quantity)]);

    public static readonly MistDiagnostic DeconstructionSourceNotATuple = new(
        DiagnosticNumber.Mint("MIST0366"),
        Title: "Deconstruction of non-tuple",
        Template: "'{type}' is not a tuple, so it cannot be deconstructed.",
        Description: "A deconstruction reads from a value that is not a tuple, and Mist has no user-declared deconstruction, so a non-tuple has no elements to distribute among the targets. Deconstruct a tuple, or read the members individually.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic DuplicateLocalVariable = new(
        DiagnosticNumber.Mint("MIST0367"),
        Title: "Duplicate local variable",
        Template: "A variable named '{name}' is already declared in this scope.",
        Description: "A local variable is declared with a name already used by another local in the same scope. Rename one of them.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name)]);

    public static readonly MistDiagnostic DuplicateLocalFunction = new(
        DiagnosticNumber.Mint("MIST0368"),
        Title: "Duplicate local function",
        Template: "A local function named '{name}' is already declared in this scope.",
        Description: "Two local functions in the same scope share a name, and local functions do not overload. Rename one of them.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name)]);

    public static readonly MistDiagnostic LocalFunctionModifier = new(
        DiagnosticNumber.Mint("MIST0369"),
        Title: "Modifier on local function",
        Template: "A local function cannot be declared '{modifier}'.",
        Description: "A local function carries a modifier it cannot have: it has no accessibility to restrict, no 'this' of its own to be readonly or local for, no discipline to declare, and no compiler-supplied body to mark intrinsic. Remove the modifier.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("modifier", SlotKind.Enum)]);

    public static readonly MistDiagnostic LocalFunctionGeneric = new(
        DiagnosticNumber.Mint("MIST0370"),
        Title: "Generic local function",
        Template: "Local function '{name}' cannot declare type parameters.",
        Description: "A local function declares type parameters, which local functions do not support. Move the function onto a type as a generic method, or drop the type parameters.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name)]);

}