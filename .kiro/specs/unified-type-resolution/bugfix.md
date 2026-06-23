# Bugfix Requirements Document

## Introduction

The Mist compiler's type resolution pipeline suffers from a phase ordering violation. Explicit type annotations on fields, properties, and method signatures are resolved concurrently with expression-level type inference in a single file-by-file pass. When a cross-file expression depends on a field or property type from a file that hasn't been processed yet, the type is still `null` (as registered during symbol registration), causing inference failures for type arguments, lambda return types, and other dependent constructs.

The root invariant being violated: **all explicit types must be resolved before any implicit types can be resolved.** Expression-level type inference (implicit types) depends on knowing the declared types of fields, properties, and method signatures (explicit types). The current pipeline interleaves these two concerns, making results dependent on file processing order.

## Bug Analysis

### Current Behavior (Defect)

1.1 WHEN a field with an explicit type annotation in File A is referenced from an expression in File B, and File B is processed before File A, THEN the system sees the field's `DeclaredType` as `null` and type inference fails

1.2 WHEN a property with an explicit type annotation in File A is referenced from an expression in File B, and File B is processed before File A, THEN the system sees the property's `DeclaredType` as `null` and type inference fails

1.3 WHEN a method's return type or parameter types are used for type argument inference in a different file, and that file is processed first, THEN the method's `DeclaredType` may still be `null` because signature pre-resolution only runs within the same class declaration

1.4 WHEN the resolution of a field's initializer expression requires knowing the type of another field (e.g., for type argument inference on a generic method call), THEN the system may fail because both the field type and its initializer expression are resolved without guaranteed ordering across types

1.5 WHEN the caller-supplied scope used for type resolution differs from the scope where the type was declared (e.g., due to using directives or namespace context), THEN the ad-hoc scope-supply mechanism can fail to resolve types that would be visible in the declaration's own scope

1.6 WHEN an interface or trait member type is used for type inference in a class implementing that interface (in a different file processed earlier), THEN the interface/trait member type may not yet be resolved

### Expected Behavior (Correct)

2.1 WHEN expression-level type inference begins for any compilation unit THEN the system SHALL guarantee that every explicitly-annotated field, property, method parameter, method return type, indexer type, operator type, cast operator target type, and constructor parameter type across ALL compilation units already has its `DeclaredType` populated

2.2 WHEN a field or property type is looked up during expression resolution THEN it SHALL be non-null regardless of which file the field/property was declared in and which file the expression lives in

2.3 WHEN a method signature is used for type argument inference or lambda target-type propagation THEN it SHALL be fully resolved (return type and all parameter types populated) regardless of cross-file reference direction

2.4 WHEN interface or trait member types are used for implementing-type inference THEN they SHALL be resolved before any expression-level inference begins in implementing types

2.5 WHEN resolving an explicit type annotation THEN the system SHALL use the declaration-site scope context (the namespace, file, and type parameter scope where the declaration lives) rather than depending on a caller-supplied ad-hoc scope

2.6 WHEN type aliases are referenced by field, property, or method type annotations THEN the alias SHALL be resolved before the referencing annotations are resolved (nesting level is irrelevant — a nested type alias is just a type with a longer FQN, like any other nested type)

### Unchanged Behavior (Regression Prevention)

3.1 WHEN all type references are within a single file THEN the system SHALL CONTINUE TO resolve all types correctly as it does today

3.2 WHEN method signatures reference types from the standard library or ambient type resolver THEN the system SHALL CONTINUE TO resolve them correctly

3.3 WHEN base types, interface lists, and constraint clauses are resolved THEN the system SHALL CONTINUE TO resolve them before member type resolution (same relative ordering)

3.4 WHEN extension method receiver types and member signatures are resolved THEN the system SHALL CONTINUE TO have them available before expression-level inference

3.5 WHEN type parameters, generics, and constructed types are resolved THEN the system SHALL CONTINUE TO produce identical type representations

3.6 WHEN expression-level type inference resolves lambdas, type arguments, and return types THEN the system SHALL CONTINUE TO produce identical inference results for programs that already compile successfully

3.7 WHEN the compiler encounters truly unresolvable types (typos, missing imports) THEN the system SHALL CONTINUE TO emit the same diagnostic errors

3.8 WHEN storage-ban checks detect `unknown` in field/property types THEN the system SHALL CONTINUE TO emit the same diagnostic errors

3.9 WHEN type alias cycle detection encounters a cyclic alias THEN the system SHALL CONTINUE TO detect and report the cycle correctly
