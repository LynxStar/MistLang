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
        DiagnosticNumber.Mint("MIST_BE_0002"),
        Title: "Missing intrinsic emission template",
        Template: "No emission template found for intrinsic '{0}' in backend 'CSharp'.",
        Description: "An intrinsic member was invoked but the C# backend has no emission template registered for it, so the compilation stops with no output artifact. Register a template for the intrinsic in the backend, or avoid the member until one exists; the gap is in the backend, not in the program.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic BuildError = new(
        DiagnosticNumber.Mint("MIST_BUILD_ERROR"),
        Title: "No C# sources to compile",
        Template: "No C# source files found to compile.",
        Description: "The downstream Roslyn build found an empty output directory, so there was nothing to compile into an assembly. Check that the Mist compilation emitted its C# files and that the output path handed to the build is the one they were written to.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic CSharpError = new(
        DiagnosticNumber.Mint("MIST_CS_ERROR"),
        Title: "C# compilation error",
        Template: "{0}",
        Description: "Roslyn rejected the emitted C# while building it into an assembly, and the message is Roslyn's own, reported against a generated file. Such an error usually means the backend emitted a shape it does not fully support; rewrite the Mist construct another way, or report it, since a well-formed Mist program should never surface a Roslyn error.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ReferenceAssemblyConflict = new(
        DiagnosticNumber.Mint("MIST_REFERENCE_ASSEMBLY_CONFLICT"),
        Title: "Conflicting reference assembly versions",
        Template: "Assembly '{0}' is supplied twice with different versions: '{1}' ({2}) and '{3}' ({4}).",
        Description: "Two references bring the same assembly into the build at different versions, and the backend cannot pass both to the compiler. Align the referenced projects or packages on one version of that assembly, or drop the reference that pulls in the second copy.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ReferenceFrameworkMissing = new(
        DiagnosticNumber.Mint("MIST_REFERENCE_FRAMEWORK_MISSING"),
        Title: "Undeclared shared framework reference",
        Template: "Referenced C# project '{0}' needs shared framework '{1}', which '[framework_references]' does not declare.",
        Description: "A referenced C# project depends on a shared framework that the Mist project does not list, so its types cannot be resolved for the build. Add the framework to the '[framework_references]' section of the project file.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic AssemblyLoadFailed = new(
        DiagnosticNumber.Mint("MIST_ASM_LOAD_FAILED"),
        Title: "Assembly load failed",
        Template: "Failed to load assembly '{0}': {1}",
        Description: "A referenced assembly could not be opened through the metadata load context while the ambient type provider was initializing, so none of its types are available to the program. Check that the file exists at the resolved path, is a managed assembly, and matches the target framework of the build.",
        Category: DiagnosticCategory.Dep,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic UnresolvedAmbientType = new(
        DiagnosticNumber.Mint("MIST_AMBIENT_UNRESOLVED"),
        Title: "Unresolved ambient type",
        Template: "Cannot resolve ambient type '{0}'. The assembly containing this type may not be referenced.",
        Description: "A CLR signature names a type that neither the referenced assemblies nor the Standard Library define, so the member using it cannot be projected into Mist. Add a reference to the assembly that declares the type.",
        Category: DiagnosticCategory.Interop,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic UnsupportedStatement = new(
        DiagnosticNumber.Mint("MIST_BE_UNSUPPORTED_STATEMENT"),
        Title: "Unsupported statement",
        Template: "This statement is not supported by the compiler yet, so no C# can be emitted for it.",
        Description: "The emitter reached a statement the binder never resolved, which means the compiler does not implement the construct yet, not that the program is wrong. It is reported even though the placeholder comment it would leave is legal C#, because a silently omitted statement yields a program that compiles and runs with a piece of itself missing. Express the statement with constructs the backend already handles.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic UnsupportedExpression = new(
        DiagnosticNumber.Mint("MIST_BE_UNSUPPORTED_EXPRESSION"),
        Title: "Unsupported expression",
        Template: "This expression is not supported by the compiler yet, so no C# can be emitted for it.",
        Description: "The emitter reached an expression the binder never resolved, which means the compiler does not implement the construct yet, not that the program is wrong. The error is reported at emission rather than at binding because most unresolved sentinels are never reached, and reporting here keeps it on the Mist source rather than on generated C# that would not parse. Express the expression with constructs the backend already handles.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ReadonlyRefReturn = new(
        DiagnosticNumber.Mint("MIST_BE_READONLY_REF_RETURN"),
        Title: "Ambiguous readonly ref return",
        Template: "Method '{0}' is declared both readonly and ref. On a struct 'readonly' already means the method does not mutate the receiver, so it is unclear whether the returned reference is itself readonly. Drop one of the two.",
        Description: "A struct method carries both 'readonly' and 'ref', and Mist spells two different things that way: a non-mutating method that returns a writable reference, or a method that returns a read-only reference. The compiler refuses to guess because one reading silently widens a read-only alias into a writable one and the other discards a modifier the author wrote; keep whichever modifier states the intent and remove the other. Parameters and locals are unaffected, since 'readonly' means only one thing there.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic UnsupportedUnionPattern = new(
        DiagnosticNumber.Mint("MIST_BE_UNSUPPORTED_UNION_PATTERN"),
        Title: "Unsupported union pattern",
        Template: "Only a type test can narrow the union type '{0}' yet. Combining patterns or comparing against a constant is not supported by the experimental emitter.",
        Description: "A pattern combinator or a constant comparison was applied to a union-typed value. A type test narrows through one carrier accessor and emits as one C# pattern, but each operand here would need a different accessor, and a C# pattern has a single scrutinee, so the test would have to become several comparisons joined by '||', an emission shape the backend has not built. Narrow with a type test first and then compare the narrowed value; the limit is the backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic UnsupportedEnumPattern = new(
        DiagnosticNumber.Mint("MIST_BE_UNSUPPORTED_ENUM_PATTERN"),
        Title: "Unsupported enum pattern combinator",
        Template: "The enum variant '{0}' cannot be combined with 'and', 'or' or 'not' yet, because matching one is an equality comparison rather than a C# pattern.",
        Description: "An enum variant appeared under a pattern combinator. A Mist enum lowers to a record class whose variants are static fields, so matching one is an equality comparison rather than a C# constant pattern, and the backend has no shape for combining comparisons inside a pattern. Test the bare variant in an 'is' or a switch arm and join several tests with ordinary boolean operators; the limit is the backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic UnsupportedUnknownPropertyPattern = new(
        DiagnosticNumber.Mint("MIST_BE_UNSUPPORTED_UNKNOWN_PROPERTY_PATTERN"),
        Title: "Unsupported unknown property pattern",
        Template: "A property pattern on an 'unknown' value cannot be combined with 'or' or 'not' yet, because its members are read through a binding of the value that neither establishes.",
        Description: "A property pattern on an 'unknown' scrutinee sat under a combinator. C# resolves the pattern's member names against a static type that an object lacks, so the backend reads them through a binding of the scrutinee, and the combinator provides no such binding. Bind the value first with a type test or a plain property pattern and apply the combinator to the result; the limit is the backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic UnsupportedSizedElementTest = new(
        DiagnosticNumber.Mint("MIST_BE_UNSUPPORTED_SIZED_ELEMENT_TEST"),
        Title: "Unsupported sized element test",
        Template: "A value cannot be tested against '{0}' at run time yet, because its elements carry a length that only a check of every element could establish.",
        Description: "A sequence whose element type carries its own length was tested at run time. The backend's erased representation could check only the outer length while promising the inner one, so it refuses rather than emit a test that passes values it did not inspect. Check the element lengths explicitly in a loop, or test against a type whose elements carry no length; the limit is the backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic UnsupportedImplicitSlice = new(
        DiagnosticNumber.Mint("MIST_BE_UNSUPPORTED_IMPLICIT_SLICE"),
        Title: "Unsupported implicit slice",
        Template: "'{0}' cannot be sliced here yet: C# calls its '{1}' itself, and {2}.",
        Description: "A user type was sliced with the 'receiver[range]' form, which C# lowers by calling the type's count and slice members itself, and at this spot that call needs something C# cannot supply there, such as an await inside a list pattern or an unsafe accessor in a clone conversion. Because the backend never writes the call, there is nothing it can adjust; invoke the slice member explicitly instead. The limit is the backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic StructInheritanceFieldCollision = new(
        DiagnosticNumber.Mint("MIST_BE_STRUCT_FIELD_COLLISION"),
        Title: "Inherited struct field collision",
        Template: "Field '{0}' is declared on both '{1}' and '{2}'. Inherited struct fields are flattened into the derived struct, so a name cannot be reused across the inheritance chain.",
        Description: "A struct and one of its ancestors declare a field with the same name. Mist allows this, since a private field is invisible to other levels, but the C# backend merges the ancestors' fields into a single struct where the name can resolve to only one of them. Rename one of the fields; a backend with native struct inheritance would have no such conflict, so the limit is this backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ForeignConstructorConstraintClosure = new(
        DiagnosticNumber.Mint("MIST_BE_FOREIGN_CTOR_CONSTRAINT"),
        Title: "Foreign constructor constraint closure",
        Template: "Type '{0}' is not declared in Mist, so it cannot close the constructor constraint 'new({1})'. The C# backend satisfies that constraint by adding an interface to the closing type, which a precompiled type cannot receive.",
        Description: "A type from another assembly was used as the argument for a parameterized constructor constraint. C# has no such constraint, so the backend lowers it to a synthesized interface with a static abstract factory and attaches that interface to the closing type's declaration, and a type compiled elsewhere has no declaration to attach it to. Close the constraint with a Mist-declared type, or wrap the foreign type in one; the parameterless 'new()' form is unaffected, and the limit is this backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic MethodGroupReachedEmission = new(
        DiagnosticNumber.Mint("MIST_INTERNAL_METHOD_GROUP_AT_EMISSION"),
        Title: "Method group reached emission",
        Template: "Internal compiler error: the method group '{0}' reached emission without having been resolved to a single overload. This is a bug in the compiler, not an error in your source.",
        Description: "A method group arrived at the emitter unresolved, which cannot happen in a correct compiler: a group's only consumers are an invocation and a conversion to a function type, and a site that can do neither must report an error and bind an error expression in its place. Nothing in the source causes this; report it as a compiler bug together with the program that triggered it.",
        Category: DiagnosticCategory.Ice,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic IntrinsicStructBase = new(
        DiagnosticNumber.Mint("MIST_BE_INTRINSIC_STRUCT_BASE"),
        Title: "Intrinsic struct base",
        Template: "Struct '{0}' cannot inherit intrinsic struct '{1}'. An intrinsic type's representation belongs to the backend, so its state cannot be flattened into a derived struct.",
        Description: "A struct inherits an intrinsic struct. The backend copies an ancestor's fields into the derived struct, and an intrinsic type declares no fields of its own in the model, so the derived struct would silently drop whatever the ancestor holds and widen to a zeroed value. Hold the intrinsic value in a field instead of inheriting it; reaching its private state by guessing at undocumented runtime field names is not an option.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ForeignConformance = new(
        DiagnosticNumber.Mint("MIST_BE_FOREIGN_CONFORMANCE"),
        Title: "Foreign type conformance",
        Template: "Type '{0}' is not declared in Mist, so it cannot be declared to conform to '{1}'. The C# backend satisfies a retroactive conformance by adding the interface to the conforming type's declaration, which a precompiled type cannot receive.",
        Description: "A type from another assembly was declared to conform to an abstract type. The model records the conformance and assignability honours it, but the C# backend expresses conformance by attaching an interface to the conforming type's declaration, which a type compiled elsewhere has no declaration to receive, and dropping it would leave every abstract-typed use failing in generated C# with no Mist-level cause. Wrap the foreign type in a Mist-declared type that conforms; the limit is this backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic UnboundedParameterizedTypeSlot = new(
        DiagnosticNumber.Mint("MIST_BE_UNBOUNDED_PARAMETERIZED_SLOT"),
        Title: "Unbounded parameterized type slot",
        Template: "The associated type slot '{0}.{1}' declares its own type parameters but no abstract type bound. C# has no higher-kinded types, so such a slot is emitted as its bound; give it one, or remove its type parameters.",
        Description: "An associated type slot takes type parameters of its own without naming an abstract type it must satisfy. The backend cannot lift such a slot the way it lifts a plain one and instead erases it to its bound, so an unbounded slot leaves nothing to erase to; erasing to 'object' would compile and then reject every member the slot's users call. Give the slot an abstract bound, or drop its type parameters; a plain unbounded slot such as 'type Element;' is fine.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ParameterizedTypeSlotInParameter = new(
        DiagnosticNumber.Mint("MIST_BE_PARAMETERIZED_SLOT_PARAMETER"),
        Title: "Parameterized type slot in parameter",
        Template: "Parameter '{0}' of '{1}.{2}' is typed by the parameterized associated type slot '{3}'. The experimental emitter erases such a slot to its bound, which it can bridge for a return type but not for a parameter.",
        Description: "A parameter takes its type from an associated type slot that has type parameters of its own. The backend erases such a slot to its bound and connects the interface to the implementor through a forwarding explicit implementation, which covariance permits for a return type but not for a parameter, where the forwarder would need a downcast the compiler cannot prove safe. Use the slot in return position only, or declare the parameter with the bound type directly; a backend with higher-kinded types would need no erasure, so the limit is this backend's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic UnboundRefinementPredicate = new(
        DiagnosticNumber.Mint("MIST_BE_REFINED_PREDICATE"),
        Title: "Unbound refinement predicate",
        Template: "The refinement '{0}' cannot be tested here: its predicate over '{1}' did not bind, and testing the base type alone would succeed for every value.",
        Description: "A value was tested against a refinement type whose predicate failed to bind, so there is nothing to test with. The backend refuses rather than fall back to the base type, because a check of an int against int holds for every value and would silently pass the test it was meant to perform. Fix the predicate expression on the refinement declaration, which usually carries its own error.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic DependentTypeParameter = new(
        DiagnosticNumber.Mint("MIST_BE_DEPENDENT_TYPE_PARAMETER"),
        Title: "Dependent type parameter",
        Template: "Type parameter '{0}' of '{1}' is dependent: it ranges over values, not types. C# has no dependent type parameter, and the experimental emitter does not yet encode one.",
        Description: "A type parameter has a value domain rather than 'Standard.Type', as in 'FixedArray<Size:int>'. Emitting it as an ordinary type parameter would compile the declaration and then fail at every use of the value inside the bodies, so the backend refuses at the parameter instead. Pass the value as a constructor argument or a field until the backend gains a value-witness encoding; the limit is the backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic UnhoistedUnitCall = new(
        DiagnosticNumber.Mint("MIST_BE_UNHOISTED_UNIT_CALL"),
        Title: "Unhoisted unit call",
        Template: "'{0}' yields nothing and its value is taken {1}. The experimental emitter supplies that value by running the call as a statement, and has nowhere to put one there.",
        Description: "A call whose result is unit is used as a value in a position where the backend cannot hoist the call into a preceding statement. Move the call into its own statement ahead of the expression that uses it; the limit is the backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic UnspecializedPack = new(
        DiagnosticNumber.Mint("MIST_BE_UNSPECIALIZED_PACK"),
        Title: "Unspecialized pack in signature",
        Template: "'{0}' mentions the pack '{1}' where no arity is fixed, so no C# signature can be written for it.",
        Description: "A pack survived into a signature that no specialization binds, so there is no width to write it at. The backend refuses rather than emit C# naming a type parameter that does not exist there, which would be valid-looking output that fails to compile in a file the user never wrote. Instantiate or call the declaration with concrete arguments so every pack width is fixed.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic UnionOverPackElement = new(
        DiagnosticNumber.Mint("MIST_BE_UNION_OVER_PACK_ELEMENT"),
        Title: "Union over pack element",
        Template: "'{0}' is spelled differently in C# for each type argument, and it names '{1}', an element of a pack declared where the C# backend cannot yet emit a declaration once per instantiation, as it does a type and a type's methods. An optional becomes spellable when '{2}' is constrained with 'class' or 'struct'.",
        Description: "A union, or a construction holding one, ranges over an element of a pack that belongs to an extension block's method, and the backend can only write such a union per arity for a type and a type's methods; written per arity for an extension method, the union loses its optional for a value type. Constrain the pack's type parameter with 'class' or 'struct' so the optional has one spelling, or move the declaration onto a type; the limit is the backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic UnboundedSpecialization = new(
        DiagnosticNumber.Mint("MIST_BE_UNBOUNDED_SPECIALIZATION"),
        Title: "Unbounded union specialization",
        Template: "'{0}' writes a union over its own type parameters, so it is emitted once per instantiation, and its instantiations keep producing new ones past {1}: an instantiation that needs a larger one of itself never finishes.",
        Description: "A declaration whose union ranges over its own type parameters is specialized per instantiation, and each instantiation demanded a larger instantiation of itself, so the chain never closes and the emitter stopped at its ceiling. Restructure the type so the union does not grow with each instantiation, for example by introducing a non-generic base case; the limit is the backend's, not the language's.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic DelegateCeilingExceeded = new(
        DiagnosticNumber.Mint("MIST_VAR_0010"),
        Title: "Delegate parameter ceiling exceeded",
        Template: "{0} takes {1} inputs, and System.Func and System.Action hold at most {2}. The limit is the C# backend's, which has no delegate to emit this function as, and not the language's.",
        Description: "A function has more parameters than the largest System.Func or System.Action arity, so the C# backend has no delegate type to represent it as a value. Group some of the parameters into a tuple or a struct, or split the function; a backend with its own function representation would have no such ceiling.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);
}