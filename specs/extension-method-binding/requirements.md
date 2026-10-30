# Requirements Document

## Introduction

This feature adds support for C# extension methods from referenced .NET assemblies to the Mist ambient type system. Currently, when a `using` directive imports a namespace, the compiler resolves types from that namespace but does not discover extension methods defined in static classes within that namespace. After this feature, extension methods decorated with `[System.Runtime.CompilerServices.ExtensionAttribute]` in imported namespaces become available as instance methods on their target types — enabling LINQ, string helpers, and other .NET extension method libraries to work naturally in Mist code.

This feature bridges the gap between Mist's native extension blocks (which are source-level) and the .NET ecosystem's convention of extension methods in static classes. It operates entirely within the ambient type system (AssemblyTypeProvider + TypeProjector) and the existing extension member lookup infrastructure (ExtensionRegistry).

## Glossary

- **Ambient_Extension_Method**: A static method in a .NET assembly that is decorated with `[System.Runtime.CompilerServices.ExtensionAttribute]` and whose first parameter is the receiver type. These methods are callable as instance methods on the receiver type when their containing namespace is imported.
- **Extension_Static_Class**: A static class in a .NET assembly that is decorated with `[System.Runtime.CompilerServices.ExtensionAttribute]`, indicating it contains extension methods.
- **Assembly_Type_Provider**: The compiler component (`AssemblyTypeProvider`) that reads .NET assembly metadata and produces Mist `IType` and `ISymbol` instances for types discovered in referenced assemblies.
- **Type_Projector**: The component (`TypeProjector`) that translates CLR type metadata (methods, properties, fields) into Mist symbols with projected type signatures.
- **Extension_Registry**: The data structure in the semantic model that indexes extension blocks by receiver type for efficient lookup during member resolution.
- **Receiver_Type**: The first parameter type of an ambient extension method — the type on which the method becomes callable as an instance method.
- **Imported_Namespace**: A namespace brought into scope via a `using` directive or global using, making its types and extension methods available for resolution.
- **Semantic_Model_Builder**: The component that constructs the semantic model (types, symbols, scopes) from the AST and ambient type information.
- **Type_Checker**: The member resolution logic within the Semantic_Model_Builder that resolves dot-access expressions, including fallback to extension methods.
- **Emitter**: The C# transpiler backend that produces C# source from the semantic model.

## Requirements

### Requirement 1: Detect Extension Methods in .NET Assemblies

**User Story:** As a compiler developer, I want the Assembly_Type_Provider to identify extension methods in loaded .NET assemblies, so that they can be registered for instance-method binding.

#### Acceptance Criteria

1. WHEN the Assembly_Type_Provider scans a loaded assembly's exported types, THE Assembly_Type_Provider SHALL identify static classes decorated with `[System.Runtime.CompilerServices.ExtensionAttribute]` as Extension_Static_Classes.
2. WHEN an Extension_Static_Class is found, THE Assembly_Type_Provider SHALL identify public static methods within that class that are also decorated with `[System.Runtime.CompilerServices.ExtensionAttribute]` as Ambient_Extension_Methods.
3. THE Assembly_Type_Provider SHALL record each Ambient_Extension_Method's containing namespace, method name, receiver type (first parameter type), remaining parameter types, return type, and generic type parameters.
4. THE Assembly_Type_Provider SHALL NOT treat a static method as an Ambient_Extension_Method if it lacks the `[System.Runtime.CompilerServices.ExtensionAttribute]`, even if it resides in an Extension_Static_Class.
5. THE Assembly_Type_Provider SHALL NOT treat instance methods or non-public methods as Ambient_Extension_Methods.

---

### Requirement 2: Project Ambient Extension Methods into the Extension Registry

**User Story:** As a compiler developer, I want ambient extension methods to be registered in the Extension_Registry, so that the existing member lookup fallback discovers them during type checking.

#### Acceptance Criteria

1. WHEN the Semantic_Model_Builder processes imported namespaces (from `using` directives and global usings), THE Semantic_Model_Builder SHALL query the Assembly_Type_Provider for Ambient_Extension_Methods in each imported namespace.
2. WHEN Ambient_Extension_Methods are found for an imported namespace, THE Semantic_Model_Builder SHALL create an ExtensionBlockInfo for each distinct receiver type, grouping all extension methods for that receiver type into a single member scope.
3. THE Semantic_Model_Builder SHALL project the receiver type (first parameter) of each Ambient_Extension_Method into a Mist IType using the Type_Projector.
4. THE Semantic_Model_Builder SHALL project the remaining parameters (excluding the receiver) and return type of each Ambient_Extension_Method into Mist types using the Type_Projector.
5. THE Semantic_Model_Builder SHALL register each projected ExtensionBlockInfo in the Extension_Registry with the projected receiver type as the key.
6. THE Semantic_Model_Builder SHALL set the `ContainingNamespace` of each registered ExtensionBlockInfo to the CLR namespace of the Extension_Static_Class, so that namespace visibility filtering applies correctly.
7. WHEN an Ambient_Extension_Method has generic type parameters, THE Semantic_Model_Builder SHALL project those type parameters and register them on the ExtensionBlockInfo, enabling generic type inference during member lookup.

---

### Requirement 3: Namespace-Scoped Visibility of Ambient Extensions

**User Story:** As a Mist developer, I want ambient extension methods to only be available when their containing namespace is imported via a `using` directive, so that I have explicit control over which extensions are in scope.

#### Acceptance Criteria

1. WHEN a file imports a namespace via `using` that contains Ambient_Extension_Methods, THE Type_Checker SHALL include those extension methods in the candidate set during member lookup fallback.
2. WHEN a file does NOT import the namespace containing an Ambient_Extension_Method, THE Type_Checker SHALL NOT include that extension method in the candidate set.
3. WHEN a global using imports a namespace containing Ambient_Extension_Methods, THE Type_Checker SHALL include those extension methods in the candidate set for all files in the compilation.
4. THE Type_Checker SHALL apply the same namespace visibility filtering to ambient extension methods as it does to Mist-native extension blocks (using the `ContainingNamespace` field on ExtensionBlockInfo).

---

### Requirement 4: Member Lookup Integration

**User Story:** As a Mist developer, I want to call ambient extension methods using dot-notation on instances, so that .NET extension methods feel like native instance methods.

#### Acceptance Criteria

1. WHEN member lookup on a type fails to find a matching member in the type's own scope, THE Type_Checker SHALL search the Extension_Registry for ambient extension methods whose projected receiver type is assignable from the value's type.
2. WHEN an ambient extension method is found, THE Type_Checker SHALL validate the call arguments against the method's parameter list (excluding the receiver parameter).
3. THE Type_Checker SHALL prefer a type's own members over ambient extension methods with the same name.
4. THE Type_Checker SHALL prefer Mist-native extension block members over ambient extension methods with the same name and compatible signatures.
5. WHEN multiple ambient extension methods with the same name are applicable, THE Type_Checker SHALL apply the same overload resolution and specificity rules as for Mist-native extensions.
6. WHEN a generic ambient extension method is a candidate, THE Type_Checker SHALL attempt to infer type arguments from the receiver type and call-site arguments, consistent with the existing generic extension inference logic.

---

### Requirement 5: Generic Ambient Extension Methods

**User Story:** As a Mist developer, I want generic ambient extension methods (like LINQ's `Where<T>`, `Select<T, TResult>`) to work with type inference, so that I do not need to specify type arguments explicitly.

#### Acceptance Criteria

1. WHEN an Ambient_Extension_Method has generic type parameters on the method itself, THE Assembly_Type_Provider SHALL record those type parameters separately from any type parameters on the containing static class.
2. WHEN a generic ambient extension method is a candidate during member lookup, THE Type_Checker SHALL infer the method's type parameters from the receiver type and the call-site argument types.
3. WHEN the receiver type is `IEnumerable<int>` and the extension method signature is `Where<T>(IEnumerable<T>, Func<T, bool>)`, THE Type_Checker SHALL infer `T = int`.
4. IF type inference fails for a generic ambient extension method, THEN THE Type_Checker SHALL exclude it from the candidate set without emitting a diagnostic.
5. WHEN type arguments are inferred, THE Type_Checker SHALL substitute them into the method's return type and parameter types for validation and expression type assignment.

---

### Requirement 6: C# Emission of Ambient Extension Method Calls

**User Story:** As a compiler developer, I want calls to ambient extension methods to emit as static method invocations in the generated C#, so that the output compiles correctly against the referenced assemblies.

#### Acceptance Criteria

1. WHEN the Emitter encounters a call to an ambient extension method, THE Emitter SHALL emit it as a static method invocation on the original Extension_Static_Class, passing the receiver as the first argument.
2. THE Emitter SHALL emit the fully-qualified static class name (namespace + class name) for the static method invocation to avoid ambiguity.
3. WHEN the ambient extension method has inferred type arguments, THE Emitter SHALL emit explicit type arguments on the static method call if they cannot be inferred by the C# compiler from the arguments alone.
4. THE Emitter SHALL ensure the `using` directive for the extension method's namespace is present in the generated C# file, so that C# extension method syntax is also valid as an alternative emission strategy.

---

### Requirement 7: Diagnostics

**User Story:** As a Mist developer, I want clear error messages when ambient extension method resolution fails, so that I understand why a method call does not compile.

#### Acceptance Criteria

1. WHEN member lookup fails and no ambient extension method is found in any imported namespace, THE Type_Checker SHALL emit the standard `MIST_UNDEFINED_MEMBER (historical)` diagnostic.
2. WHEN multiple ambient extension methods from different namespaces are equally applicable (ambiguity), THE Type_Checker SHALL emit a diagnostic listing all candidate methods and their containing namespaces.
3. WHEN an ambient extension method is found but the call-site arguments do not match the parameter types, THE Type_Checker SHALL emit a type mismatch diagnostic referencing the extension method's full signature.

---

### Requirement 8: Sample Application

**User Story:** As a Mist developer, I want a comprehensive sample application demonstrating ambient extension method binding, so that I can see LINQ and other .NET extension methods working in Mist code and verify correctness.

#### Acceptance Criteria

1. A sample project SHALL exist at `samples/ExtensionMethodBinding/Program.mist`.
2. The sample SHALL demonstrate calling LINQ extension methods (`Where`, `Select`, `ToList`, `Count`, `First`, `Any`) on `IEnumerable<T>` collections.
3. The sample SHALL demonstrate calling string extension methods (e.g., from a referenced NuGet package or custom assembly) if available in the SDK.
4. The sample SHALL demonstrate that extension methods are NOT available when the containing namespace is not imported (compile-time verification via a separate negative test or commented demonstration).
5. Each demonstrated usage SHALL print labeled output to stdout so correctness is observable.
6. `samples/AllFeatures/Program.mist` SHALL call `ExtensionMethodBinding.Program.RunDemo()` to include the feature in the full-suite sample run.
7. The sample SHALL compile and run without errors using the Mist compiler.

#### Proof Code

```mist
using Standard;
using System.Linq;

namespace ExtensionMethodBinding;

public class Program
{
    public static void RunDemo()
    {
        Console.WriteLine("--- Extension Method Binding ---");

        // PROOF: LINQ Count<T>() extension method resolves on int[] (IEnumerable<int>)
        // PROOF: Type inference deduces T=int from the receiver type
        // PROOF: Parameterless extension method works (only receiver param in CLR terms)
        int[] numbers = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        int count = numbers.Count();
        Console.WriteLine($"Count: {count}");

        // PROOF: LINQ First<T>() extension method (parameterless overload)
        // PROOF: Return type int is correctly inferred from IEnumerable<int>
        int first = numbers.First();
        Console.WriteLine($"First: {first}");

        // PROOF: LINQ Last<T>() extension method (parameterless overload)
        int last = numbers.Last();
        Console.WriteLine($"Last: {last}");

        // PROOF: LINQ Any<T>() extension method (parameterless overload)
        // PROOF: Returns bool — proves return type projection works
        bool hasAny = numbers.Any();
        Console.WriteLine($"Any: {hasAny}");

        // PROOF: LINQ extension methods resolve on empty array
        // PROOF: Count returns 0 for empty collection
        int[] empty = [];
        int emptyCount = empty.Count();
        Console.WriteLine($"Empty count: {emptyCount}");

        // PROOF: LINQ Any() returns false on empty collection
        bool emptyAny = empty.Any();
        Console.WriteLine($"Empty any: {emptyAny}");

        // PROOF: Chained extension method calls resolve correctly
        // PROOF: Each intermediate result type feeds into the next extension lookup
        // PROOF: Skip<T>(int) returns IEnumerable<int>, then First<T>() resolves on that
        int chainResult = numbers.Skip(5).First();
        Console.WriteLine($"Skip then First: {chainResult}");

        // PROOF: LINQ extension methods work on string[] (IEnumerable<string>)
        // PROOF: Generic type inference works for T=string
        string[] words = ["hello", "world", "mist"];
        int wordCount = words.Count();
        Console.WriteLine($"Word count: {wordCount}");

        string firstWord = words.First();
        Console.WriteLine($"First word: {firstWord}");

        Console.WriteLine("--- Extension Method Binding Done ---");
    }

    public static void Main(string[] args)
    {
        RunDemo();
    }
}
```

**Expected output:**
```
--- Extension Method Binding ---
Count: 10
First: 1
Last: 10
Any: True
Empty count: 0
Empty any: False
Skip then First: 6
Word count: 3
First word: hello
--- Extension Method Binding Done ---
```

---

### Requirement 9: Interaction with Mist-Native Extension Blocks

**User Story:** As a Mist developer, I want ambiguity between Mist-native extension blocks and ambient extension methods to be reported as an error, so that I am aware of conflicts and can resolve them explicitly.

#### Acceptance Criteria

1. WHEN both a Mist-native extension block and an ambient extension method provide a member with the same name and compatible signatures for the same receiver type, THE Type_Checker SHALL emit an ambiguity diagnostic listing both candidates (the same behavior as two ambient extensions from different namespaces conflicting).
2. WHEN only an ambient extension method provides the member, THE Type_Checker SHALL use the ambient extension method.
3. WHEN only a Mist-native extension block provides the member, THE Type_Checker SHALL use the Mist-native extension block member.

---

### Requirement 10: Book Documentation

**User Story:** As a Mist developer reading the language book, I want documentation explaining how .NET extension methods become available through `using` directives, so that I understand how to use LINQ and other .NET extension libraries in my Mist code.

#### Acceptance Criteria

1. THE book chapter at `book/src/object-model/extension-methods.md` SHALL include a section explaining ambient extension method binding — how importing a .NET namespace via `using` makes its extension methods available as instance methods.
2. THE book section SHALL include a snippet at `book/snippets/object-model/extension-methods/ambient-extensions.mist` demonstrating calling a LINQ extension method on a collection.
3. THE book section SHALL explain that ambient extension methods follow the same visibility rules as Mist-native extensions (only available when the namespace is imported).
4. THE book section SHALL note that if a Mist-native extension and an ambient extension provide the same member name for the same type, the compiler reports an ambiguity error.

---

### Requirement 11: Lazy Discovery and Caching

**User Story:** As a compiler developer, I want ambient extension method discovery to be lazy and cached, so that compilation performance is not degraded by scanning all assemblies eagerly.

#### Acceptance Criteria

1. THE Assembly_Type_Provider SHALL NOT scan all assemblies for extension methods at startup. Extension method discovery SHALL occur when a namespace is first imported via a `using` directive.
2. WHEN a namespace's extension methods have been discovered, THE Assembly_Type_Provider SHALL cache the results so that subsequent imports of the same namespace (in other files) reuse the cached data.
3. THE Assembly_Type_Provider SHALL only scan exported types in the target namespace (not all types in the assembly) when discovering extension methods for that namespace.
