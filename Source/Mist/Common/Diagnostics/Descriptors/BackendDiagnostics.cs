#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

/// <summary>
/// Diagnostic descriptors for the C# transpiler backend, compilation driver,
/// and assembly type provider. Covers emission template errors, Roslyn compilation
/// failures, assembly load failures, and general backend exceptions.
/// </summary>
public static class BackendDiagnostics
{
    public static readonly MistDiagnostic MissingIntrinsicEmissionTemplate = new(
        DiagnosticNumber.Mint("MIST0001"),
        Title: "Missing intrinsic emission template",
        Template: "No emission template found for intrinsic '{intrinsic}' in backend 'CSharp'.",
        Description: "An intrinsic member was invoked but the C# backend has no emission template registered for it, so the compilation stops with no output artifact. Register a template for the intrinsic in the backend, or avoid the member until one exists; the gap is in the backend, not in the program.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("intrinsic", SlotKind.Symbol)]);

    public static readonly MistDiagnostic BuildError = new(
        DiagnosticNumber.Mint("MIST0002"),
        Title: "No C# sources to compile",
        Template: "No C# source files found to compile.",
        Description: "The downstream Roslyn build found an empty output directory, so there was nothing to compile into an assembly. Check that the Mist compilation emitted its C# files and that the output path handed to the build is the one they were written to.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic CSharpError = new(
        DiagnosticNumber.Mint("MIST0003"),
        Title: "C# compilation error",
        Template: "{message}",
        Description: "Roslyn rejected the emitted C# while building it into an assembly, and the message is Roslyn's own, reported against a generated file. Such an error usually means the backend emitted a shape it does not fully support; rewrite the Mist construct another way, or report it, since a well-formed Mist program should never surface a Roslyn error.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("message", SlotKind.Text)]);

    public static readonly MistDiagnostic ReferenceAssemblyConflict = new(
        DiagnosticNumber.Mint("MIST0004"),
        Title: "Conflicting reference assembly versions",
        Template: "Assembly '{assembly}' is supplied twice with different versions: '{first}' ({firstVersion}) and '{second}' ({secondVersion}).",
        Description: "Two references bring the same assembly into the build at different versions, and the backend cannot pass both to the compiler. Align the referenced projects or packages on one version of that assembly, or drop the reference that pulls in the second copy.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("assembly", SlotKind.Name), new Slot("first", SlotKind.Path), new Slot("firstVersion", SlotKind.Name), new Slot("second", SlotKind.Path), new Slot("secondVersion", SlotKind.Name)]);

    public static readonly MistDiagnostic ReferenceFrameworkMissing = new(
        DiagnosticNumber.Mint("MIST0005"),
        Title: "Undeclared shared framework reference",
        Template: "Referenced C# project '{project}' needs shared framework '{framework}', which '[framework_references]' does not declare.",
        Description: "A referenced C# project depends on a shared framework that the Mist project does not list, so its types cannot be resolved for the build. Add the framework to the '[framework_references]' section of the project file.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("project", SlotKind.Name), new Slot("framework", SlotKind.Name)]);

    public static readonly MistDiagnostic AssemblyLoadFailed = new(
        DiagnosticNumber.Mint("MIST0006"),
        Title: "Assembly load failed",
        Template: "Failed to load assembly '{assembly}': {message}",
        Description: "A referenced assembly could not be opened through the metadata load context while the ambient type provider was initializing, so none of its types are available to the program. Check that the file exists at the resolved path, is a managed assembly, and matches the target framework of the build.",
        Category: DiagnosticCategory.Dep,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("assembly", SlotKind.Path), new Slot("message", SlotKind.Text)]);

    public static readonly MistDiagnostic UnsupportedStatement = new(
        DiagnosticNumber.Mint("MIST0007"),
        Title: "Unsupported statement",
        Template: "This statement is not supported by the compiler yet, so no C# can be emitted for it.",
        Description: "The emitter reached a statement the binder never resolved, which means the compiler does not implement the construct yet, not that the program is wrong. It is reported even though the placeholder comment it would leave is legal C#, because a silently omitted statement yields a program that compiles and runs with a piece of itself missing. Express the statement with constructs the backend already handles.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic UnsupportedExpression = new(
        DiagnosticNumber.Mint("MIST0008"),
        Title: "Unsupported expression",
        Template: "This expression is not supported by the compiler yet, so no C# can be emitted for it.",
        Description: "The emitter reached an expression the binder never resolved, which means the compiler does not implement the construct yet, not that the program is wrong. The error is reported at emission rather than at binding because most unresolved sentinels are never reached, and reporting here keeps it on the Mist source rather than on generated C# that would not parse. Express the expression with constructs the backend already handles.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ReadonlyRefReturn = new(
        DiagnosticNumber.Mint("MIST0009"),
        Title: "Ambiguous readonly ref return",
        Template: "Method '{method}' is declared both readonly and ref. On a struct 'readonly' already means the method does not mutate the receiver, so it is unclear whether the returned reference is itself readonly. Drop one of the two.",
        Description: "A struct method carries both 'readonly' and 'ref', and Mist spells two different things that way: a non-mutating method that returns a writable reference, or a method that returns a read-only reference. The compiler refuses to guess because one reading silently widens a read-only alias into a writable one and the other discards a modifier the author wrote; keep whichever modifier states the intent and remove the other. Parameters and locals are unaffected, since 'readonly' means only one thing there.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("method", SlotKind.Symbol)]);

    public static readonly MistDiagnostic UnsupportedUnionPattern = new(
        DiagnosticNumber.Mint("MIST0010"),
        Title: "Unsupported union pattern",
        Template: "Only a type test can narrow the union type '{type}' yet. Combining patterns or comparing against a constant is not supported by the experimental emitter.",
        Description: "A pattern combinator or a constant comparison was applied to a union-typed value. A type test narrows through one carrier accessor and emits as one C# pattern, but each operand here would need a different accessor, and a C# pattern has a single scrutinee, so the test would have to become several comparisons joined by '||', an emission shape the backend has not built. Narrow with a type test first and then compare the narrowed value; the limit is the backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic UnsupportedEnumPattern = new(
        DiagnosticNumber.Mint("MIST0011"),
        Title: "Unsupported enum pattern combinator",
        Template: "The enum variant '{variant}' cannot be combined with 'and', 'or' or 'not' yet, because matching one is an equality comparison rather than a C# pattern.",
        Description: "An enum variant appeared under a pattern combinator. A Mist enum lowers to a record class whose variants are static fields, so matching one is an equality comparison rather than a C# constant pattern, and the backend has no shape for combining comparisons inside a pattern. Test the bare variant in an 'is' or a switch arm and join several tests with ordinary boolean operators; the limit is the backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("variant", SlotKind.Symbol)]);

    public static readonly MistDiagnostic UnsupportedUnknownPropertyPattern = new(
        DiagnosticNumber.Mint("MIST0012"),
        Title: "Unsupported unknown property pattern",
        Template: "A property pattern on an 'unknown' value cannot be combined with 'or' or 'not' yet, because its members are read through a binding of the value that neither establishes.",
        Description: "A property pattern on an 'unknown' scrutinee sat under a combinator. C# resolves the pattern's member names against a static type that an object lacks, so the backend reads them through a binding of the scrutinee, and the combinator provides no such binding. Bind the value first with a type test or a plain property pattern and apply the combinator to the result; the limit is the backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic UnsupportedSizedElementTest = new(
        DiagnosticNumber.Mint("MIST0013"),
        Title: "Unsupported sized element test",
        Template: "A value cannot be tested against '{type}' at run time yet, because its elements carry a length that only a check of every element could establish.",
        Description: "A sequence whose element type carries its own length was tested at run time. The backend's erased representation could check only the outer length while promising the inner one, so it refuses rather than emit a test that passes values it did not inspect. Check the element lengths explicitly in a loop, or test against a type whose elements carry no length; the limit is the backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic UnsupportedImplicitSlice = new(
        DiagnosticNumber.Mint("MIST0014"),
        Title: "Unsupported implicit slice",
        Template: "'{receiver}' cannot be sliced here yet: C# calls its '{member}' itself, and {reason}.",
        Description: "A user type was sliced with the 'receiver[range]' form, which C# lowers by calling the type's count and slice members itself, and at this spot that call needs something C# cannot supply there, such as an await inside a list pattern or an unsafe accessor in a clone conversion. Because the backend never writes the call, there is nothing it can adjust; invoke the slice member explicitly instead. The limit is the backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("receiver", SlotKind.Type), new Slot("member", SlotKind.Symbol), new Slot("reason", SlotKind.Enum)]);

    public static readonly MistDiagnostic StructInheritanceFieldCollision = new(
        DiagnosticNumber.Mint("MIST0015"),
        Title: "Inherited struct field collision",
        Template: "Field '{field}' is declared on both '{declaring}' and '{ancestor}'. Inherited struct fields are flattened into the derived struct, so a name cannot be reused across the inheritance chain.",
        Description: "A struct and one of its ancestors declare a field with the same name. Mist allows this, since a private field is invisible to other levels, but the C# backend merges the ancestors' fields into a single struct where the name can resolve to only one of them. Rename one of the fields; a backend with native struct inheritance would have no such conflict, so the limit is this backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("field", SlotKind.Name), new Slot("declaring", SlotKind.Type), new Slot("ancestor", SlotKind.Type)]);

    public static readonly MistDiagnostic ForeignConstructorConstraintClosure = new(
        DiagnosticNumber.Mint("MIST0016"),
        Title: "Foreign constructor constraint closure",
        Template: "Type '{type}' is not declared in Mist, so it cannot close the constructor constraint 'new({parameters})'. The C# backend satisfies that constraint by adding an interface to the closing type, which a precompiled type cannot receive.",
        Description: "A type from another assembly was used as the argument for a parameterized constructor constraint. C# has no such constraint, so the backend lowers it to a synthesized interface with a static abstract factory and attaches that interface to the closing type's declaration, and a type compiled elsewhere has no declaration to attach it to. Close the constraint with a Mist-declared type, or wrap the foreign type in one; the parameterless 'new()' form is unaffected, and the limit is this backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("parameters", SlotKind.Sequence, SlotKind.Type)]);

    public static readonly MistDiagnostic MethodGroupReachedEmission = new(
        DiagnosticNumber.Mint("MIST0017"),
        Title: "Method group reached emission",
        Template: "Internal compiler error: the method group '{name}' reached emission without having been resolved to a single overload. This is a bug in the compiler, not an error in your source.",
        Description: "A method group arrived at the emitter unresolved, which cannot happen in a correct compiler: a group's only consumers are an invocation and a conversion to a function type, and a site that can do neither must report an error and bind an error expression in its place. Nothing in the source causes this; report it as a compiler bug together with the program that triggered it.",
        Category: DiagnosticCategory.Ice,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name)]);

    public static readonly MistDiagnostic IntrinsicStructBase = new(
        DiagnosticNumber.Mint("MIST0018"),
        Title: "Intrinsic struct base",
        Template: "Struct '{derived}' cannot inherit intrinsic struct '{base}'. An intrinsic type's representation belongs to the backend, so its state cannot be flattened into a derived struct.",
        Description: "A struct inherits an intrinsic struct. The backend copies an ancestor's fields into the derived struct, and an intrinsic type declares no fields of its own in the model, so the derived struct would silently drop whatever the ancestor holds and widen to a zeroed value. Hold the intrinsic value in a field instead of inheriting it; reaching its private state by guessing at undocumented runtime field names is not an option.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("derived", SlotKind.Type), new Slot("base", SlotKind.Type)]);

    public static readonly MistDiagnostic ForeignConformance = new(
        DiagnosticNumber.Mint("MIST0019"),
        Title: "Foreign type conformance",
        Template: "Type '{type}' is not declared in Mist, so it cannot be declared to conform to '{conformance}'. The C# backend satisfies a retroactive conformance by adding the interface to the conforming type's declaration, which a precompiled type cannot receive.",
        Description: "A type from another assembly was declared to conform to an abstract type. The model records the conformance and assignability honours it, but the C# backend expresses conformance by attaching an interface to the conforming type's declaration, which a type compiled elsewhere has no declaration to receive, and dropping it would leave every abstract-typed use failing in generated C# with no Mist-level cause. Wrap the foreign type in a Mist-declared type that conforms; the limit is this backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("conformance", SlotKind.Type)]);

    public static readonly MistDiagnostic UnboundedParameterizedTypeSlot = new(
        DiagnosticNumber.Mint("MIST0020"),
        Title: "Unbounded parameterized type slot",
        Template: "The associated type slot '{owner}.{slot}' declares its own type parameters but no abstract type bound. C# has no higher-kinded types, so such a slot is emitted as its bound; give it one, or remove its type parameters.",
        Description: "An associated type slot takes type parameters of its own without naming an abstract type it must satisfy. The backend cannot lift such a slot the way it lifts a plain one and instead erases it to its bound, so an unbounded slot leaves nothing to erase to; erasing to 'object' would compile and then reject every member the slot's users call. Give the slot an abstract bound, or drop its type parameters; a plain unbounded slot such as 'type Element;' is fine.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("owner", SlotKind.Type), new Slot("slot", SlotKind.Name)]);

    public static readonly MistDiagnostic ParameterizedTypeSlotInParameter = new(
        DiagnosticNumber.Mint("MIST0021"),
        Title: "Parameterized type slot in parameter",
        Template: "Parameter '{parameter}' of '{owner}.{method}' is typed by the parameterized associated type slot '{slot}'. The experimental emitter erases such a slot to its bound, which it can bridge for a return type but not for a parameter.",
        Description: "A parameter takes its type from an associated type slot that has type parameters of its own. The backend erases such a slot to its bound and connects the interface to the implementor through a forwarding explicit implementation, which covariance permits for a return type but not for a parameter, where the forwarder would need a downcast the compiler cannot prove safe. Use the slot in return position only, or declare the parameter with the bound type directly; a backend with higher-kinded types would need no erasure, so the limit is this backend's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("parameter", SlotKind.Name), new Slot("owner", SlotKind.Type), new Slot("method", SlotKind.Name), new Slot("slot", SlotKind.Type)]);

    public static readonly MistDiagnostic UnboundRefinementPredicate = new(
        DiagnosticNumber.Mint("MIST0022"),
        Title: "Unbound refinement predicate",
        Template: "The refinement '{refinement}' cannot be tested here: its predicate over '{base}' did not bind, and testing the base type alone would succeed for every value.",
        Description: "A value was tested against a refinement type whose predicate failed to bind, so there is nothing to test with. The backend refuses rather than fall back to the base type, because a check of an int against int holds for every value and would silently pass the test it was meant to perform. Fix the predicate expression on the refinement declaration, which usually carries its own error.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("refinement", SlotKind.Type), new Slot("base", SlotKind.Type)]);

    public static readonly MistDiagnostic DependentTypeParameter = new(
        DiagnosticNumber.Mint("MIST0023"),
        Title: "Dependent type parameter",
        Template: "Type parameter '{parameter}' of '{owner}' is dependent: it ranges over values, not types. C# has no dependent type parameter, and the experimental emitter does not yet encode one.",
        Description: "A type parameter has a value domain rather than 'Standard.Type', as in 'FixedArray<Size:int>'. Emitting it as an ordinary type parameter would compile the declaration and then fail at every use of the value inside the bodies, so the backend refuses at the parameter instead. Pass the value as a constructor argument or a field until the backend gains a value-witness encoding; the limit is the backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("parameter", SlotKind.Name), new Slot("owner", SlotKind.Type)]);

    public static readonly MistDiagnostic UnhoistedUnitCall = new(
        DiagnosticNumber.Mint("MIST0024"),
        Title: "Unhoisted unit call",
        Template: "'{member}' yields nothing and its value is taken {context}. The experimental emitter supplies that value by running the call as a statement, and has nowhere to put one there.",
        Description: "A call whose result is unit is used as a value in a position where the backend cannot hoist the call into a preceding statement. Move the call into its own statement ahead of the expression that uses it; the limit is the backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Symbol), new Slot("context", SlotKind.Enum)]);

    public static readonly MistDiagnostic UnspecializedPack = new(
        DiagnosticNumber.Mint("MIST0025"),
        Title: "Unspecialized pack in signature",
        Template: "'{declaration}' mentions the pack '{pack}' where no arity is fixed, so no C# signature can be written for it.",
        Description: "A pack survived into a signature that no specialization binds, so there is no width to write it at. The backend refuses rather than emit C# naming a type parameter that does not exist there, which would be valid-looking output that fails to compile in a file the user never wrote. Instantiate or call the declaration with concrete arguments so every pack width is fixed.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("declaration", SlotKind.Symbol), new Slot("pack", SlotKind.Name)]);

    public static readonly MistDiagnostic UnionOverPackElement = new(
        DiagnosticNumber.Mint("MIST0026"),
        Title: "Union over pack element",
        Template: "'{written}' is spelled differently in C# for each type argument, and it names '{element}', an element of a pack declared where the C# backend cannot yet emit a declaration once per instantiation, as it does a type and a type's methods. An optional becomes spellable when '{pack}' is constrained with 'class' or 'struct'.",
        Description: "A union, or a construction holding one, ranges over an element of a pack that belongs to an extension block's method, and the backend can only write such a union per arity for a type and a type's methods; written per arity for an extension method, the union loses its optional for a value type. Constrain the pack's type parameter with 'class' or 'struct' so the optional has one spelling, or move the declaration onto a type; the limit is the backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("written", SlotKind.Type), new Slot("element", SlotKind.Name), new Slot("pack", SlotKind.Name)]);

    public static readonly MistDiagnostic UnboundedSpecialization = new(
        DiagnosticNumber.Mint("MIST0027"),
        Title: "Unbounded union specialization",
        Template: "'{declaration}' writes a union over its own type parameters, so it is emitted once per instantiation, and its instantiations keep producing new ones past {limit}: an instantiation that needs a larger one of itself never finishes.",
        Description: "A declaration whose union ranges over its own type parameters is specialized per instantiation, and each instantiation demanded a larger instantiation of itself, so the chain never closes and the emitter stopped at its ceiling. Restructure the type so the union does not grow with each instantiation, for example by introducing a non-generic base case; the limit is the backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("declaration", SlotKind.Symbol), new Slot("limit", SlotKind.Quantity)]);

    public static readonly MistDiagnostic DelegateCeilingExceeded = new(
        DiagnosticNumber.Mint("MIST0028"),
        Title: "Delegate parameter ceiling exceeded",
        Template: "{subject} takes {inputs} inputs, and System.Func and System.Action hold at most {limit}. The limit is the C# backend's, which has no delegate to emit this function as, and not the language's.",
        Description: "A function has more parameters than the largest System.Func or System.Action arity, so the C# backend has no delegate type to represent it as a value. Group some of the parameters into a tuple or a struct, or split the function; a backend with its own function representation would have no such ceiling.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("subject", SlotKind.Symbol), new Slot("inputs", SlotKind.Quantity), new Slot("limit", SlotKind.Quantity)]);

    public static readonly MistDiagnostic EmissionFallback = new(
        DiagnosticNumber.Mint("MIST0424"),
        Title: "Unhandled emission",
        Template: "Internal compiler error: no emission for {kind} '{detail}'. This is a compiler bug; please report it with the program that caused it.",
        Description: "The backend reached a model shape it cannot emit and stopped artifact production. Report the compiler bug with the source program.",
        Category: DiagnosticCategory.Ice,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("kind", SlotKind.Enum), new Slot("detail", SlotKind.Name)]);

    public static readonly MistDiagnostic StaticStorageOnValueRoute = new(
        DiagnosticNumber.Mint("MIST0491"),
        Title: "Static storage in a value-routed type",
        Template: "'{member}' is static storage of '{type}', whose dependent parameter '{parameter}' is closed with a value known only when the program runs. The C# backend carries that value in each instance and has no storage keyed by it yet.",
        Description: "A type with a dependent parameter is closed somewhere with a runtime value, so the backend carries the parameter's value in the instance rather than in the C# type, and a static field or property of the type has no closing to belong to. Each closing owns its own static storage (runtime-dependent-values Requirement 10), which needs a table keyed by the slot's value that the backend does not emit yet. Move the storage onto an instance, or close the type only with literals; the limit is the backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name), new Slot("type", SlotKind.Type), new Slot("parameter", SlotKind.Name)]);

    public static readonly MistDiagnostic TypeValueOfValueRoutedClosing = new(
        DiagnosticNumber.Mint("MIST0492"),
        Title: "Type value of a value-routed closing",
        Template: "'{type}' cannot be taken as a type value: its dependent parameter '{parameter}' is carried in each instance rather than in the C# type, so a CLR Type cannot hold the value.",
        Description: "A closing of a type whose dependent parameter is closed somewhere with a runtime value was used as a value of type Type. The backend carries such a parameter in the instance and the C# type no longer names it, so the CLR Type of the closing would stand for every value of the slot at once. Take the type value of a closing whose parameters are all carried in the C# type, or compare instances through their slot reads instead; the limit is the backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("parameter", SlotKind.Name)]);

    public static readonly MistDiagnostic ValueRoutedAbstractAtTwoValues = new(
        DiagnosticNumber.Mint("MIST0493"),
        Title: "Value-routed abstract implemented at two values",
        Template: "'{type}' implements '{abstract}' at two values of '{parameter}', which the C# backend carries in one instance member because a closing of '{abstract}' is written somewhere with a value known only when the program runs.",
        Description: "An abstract with a dependent parameter is closed somewhere with a runtime value, so the backend answers that parameter through one hidden instance member of each implementing type rather than through the C# interface's type arguments. A type that implements the abstract at two values of the slot would need two answers from one member. Implement the abstract at one value, or close it only with literals everywhere; the limit is the backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("abstract", SlotKind.Type), new Slot("parameter", SlotKind.Name)]);

    public static readonly MistDiagnostic ValueRoutedConstructorConstraintClosure = new(
        DiagnosticNumber.Mint("MIST0495"),
        Title: "Value-routed type closes constructor constraint",
        Template: "'{type}' cannot close the constructor constraint 'new({signature})': its dependent parameter '{parameter}' is carried in each instance as a hidden constructor argument, which neither the C# constraint nor the factory the backend synthesizes for it can supply.",
        Description: "A constructor constraint is lowered to C#'s own 'new()' constraint, which requires a public parameterless constructor, and to a synthesized interface whose static factory constructs the closing type from the constraint's parameters alone. A type whose dependent parameter is closed somewhere with a runtime value takes that value as a hidden leading argument of every constructor, so it has no parameterless constructor and the factory has no value to pass. Close the constraint with a type whose parameters are all carried in the C# type, or construct the instance yourself and pass it; the limit is the backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type), new Slot("parameter", SlotKind.Name), new Slot("signature", SlotKind.Sequence, SlotKind.Type)]);

    public static readonly MistDiagnostic CoincidingOverloads = new(
        DiagnosticNumber.Mint("MIST0497"),
        Title: "Overloads coincide in emitted C#",
        Template: "'{member}' is declared again with C# parameters that coincide with '{earlier}': the two differ only in '{parameter}' of '{type}', which the C# backend does not carry in the type.",
        Description: "Two overloads of one name differ in Mist by a slot value that the C# backend erases from the type, a length or a dependent parameter closed somewhere with a runtime value, so their emitted parameter lists are the same and C# refuses the second. Give the overloads different names or different parameter types, or close the type only with literals everywhere; the limit is the backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name), new Slot("earlier", SlotKind.Name), new Slot("parameter", SlotKind.Name), new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic InferredSlotNestedInArgument = new(
        DiagnosticNumber.Mint("MIST0498"),
        Title: "Inferred slot nested in an argument",
        Template: "'{method}' cannot infer '{parameter}' from '{type}': the slot sits inside another type's argument with no value known, and nothing carries one for the C# backend to read back.",
        Description: "A method's or an extension block's dependent parameter is inferred from a closing that sits inside another type's argument, such as the element type of a list, and the slot of that closing holds 'unknown'. A value known where the type was written travels with it, but a slot nested under 'unknown' has no value anywhere: the CLR container keeps nothing of its element's slots, and may hold no element to read one from. Test an element against a closing, state the argument between angle brackets, or hold the closing directly; the limit is the backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("method", SlotKind.Name), new Slot("parameter", SlotKind.Name), new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic ValueRoutedSlotOutOfReach = new(
        DiagnosticNumber.Mint("MIST0499"),
        Title: "Value-routed slot read out of reach",
        Template: "'{parameter}' of '{owner}' cannot be read in {site}: it is closed somewhere with a value known only when the program runs, which the C# backend carries in each instance and in a hidden parameter of the owner's own members, and neither reaches here.",
        Description: "A dependent parameter closed somewhere with a runtime value leaves the C# type, and its value lives in a hidden field of each instance and in a hidden leading parameter of the owner's constructors, static members and methods. A C# operator or conversion takes no hidden parameter, so it reads the value from an operand whose type carries the slot, and a conversion into the type from one that does not carry it has no operand to read. A type nested in the owner is not an instance of it, an attribute argument is a constant, and a declaration the backend copies per instantiation with a closing of the owner standing in a type argument is one copy for every value of the slot, a value that belongs to whatever named the instance and not to the copy's receiver or hidden parameter; when the copied declaration is the owner or one of its members, the copy cannot tell the argument's value from its own and reads neither. Read the value through an operand or an instance, or close the type only with literals everywhere; the limit is the backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("parameter", SlotKind.Name), new Slot("owner", SlotKind.Name), new Slot("site", SlotKind.Enum)]);

    public static readonly MistDiagnostic ValueRoutedParameterAsType = new(
        DiagnosticNumber.Mint("MIST0500"),
        Title: "Value-routed parameter spelled as a type",
        Template: "'{parameter}' of '{owner}' cannot stand where a C# type is needed: it is closed somewhere with a value known only when the program runs, which the C# backend carries in each instance rather than in a C# type parameter, so no C# type names it.",
        Description: "A dependent parameter closed somewhere with a runtime value leaves the C# type parameter list, and its value lives in a hidden field of each instance and in a hidden parameter of the owner's members. Its name written as a type, in a declared type, a type argument, a type test or switch pattern, a construction or a type value, names a C# type parameter the backend no longer declares. Write the parameter where its value is wanted and compare against that value, or close the type only with literals everywhere; the limit is the backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("parameter", SlotKind.Name), new Slot("owner", SlotKind.Name)]);

    public static readonly MistDiagnostic CoincidingUnionConstituents = new(
        DiagnosticNumber.Mint("MIST0501"),
        Title: "Union constituents coincide in C#",
        Template: "'{union}' cannot be carried by the C# backend: '{first}' and '{second}' are one C# type, so its carrier cannot tell them apart.",
        Description: "A union is carried by a C# struct with one entry, one test and one accessor per constituent, named for the constituent's C# type. A dependent parameter closed somewhere with a runtime value leaves the C# type, so every closing of that type is the one C# type, and two closings of it in one union name the same entry twice. Hold one closing in the union and compare the slot's value where the test is made, or close the type only with literals everywhere; the limit is the backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("union", SlotKind.Type), new Slot("first", SlotKind.Type), new Slot("second", SlotKind.Type)]);

    public static readonly MistDiagnostic SlotSensitiveClosingArgument = new(
        DiagnosticNumber.Mint("MIST0502"),
        Title: "Value-routed closing fills slot-sensitive parameter",
        Template: "'{argument}' cannot fill '{parameter}' of '{declaration}': it mentions '{type}', whose dependent parameter '{slot}' is closed somewhere with a value known only when the program runs, so every closing of '{type}' is one C# type, and '{declaration}' {reason}.",
        Description: "A generic declaration's C# body is shared by every closing of its type parameters. A dependent parameter closed somewhere with a runtime value leaves the C# type, so every closing of its type is one C# type, and a body that tests a value against a type mentioning the parameter, creates storage of such a type from the CLR zero, declares static storage, reads the parameter as a type value, or passes it on to a parameter of another declaration that does one of these cannot tell one closing from another and would answer for the wrong one. A test is exempt when the value's own type admits only the tested closing. Fill the parameter with a type whose parameters are all carried in the C# type, or close the dependent type only with literals everywhere; the limit is the backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("argument", SlotKind.Type), new Slot("parameter", SlotKind.Name), new Slot("declaration", SlotKind.Name), new Slot("type", SlotKind.Type), new Slot("slot", SlotKind.Name), new Slot("reason", SlotKind.Enum)]);
}