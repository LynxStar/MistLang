namespace Mist.Common.Diagnostics;

public static partial class ProgramAnalysisDiagnostics
{
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
}