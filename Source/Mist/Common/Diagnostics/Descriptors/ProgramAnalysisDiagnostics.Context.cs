#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

public static partial class ProgramAnalysisDiagnostics
{
    public static readonly MistDiagnostic MissingContext = new(
        DiagnosticNumber.Mint("MIST_CTX_0001"),
        Title: "Missing context",
        Template: "{0} '{1}' requires context of type '{2}', but no matching context declaration is in scope at {3}.",
        Description: "A callable or type asks for a context value through its 'with' clause, and nothing in the enclosing scopes binds a value of that type at the use site. Bind the value with a context declaration in an enclosing scope, or add the same 'with' clause to the enclosing callable so the requirement is passed up to its callers.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic DuplicateContextBinding = new(
        DiagnosticNumber.Mint("MIST_CTX_0002"),
        Title: "Duplicate context binding",
        Template: "{0} '{1}' has duplicate context binding for type '{2}': '{3}' already binds it at {4}.",
        Description: "A callable or type binds two context values of the same type, so a requirement for that type would have two candidates and no rule for choosing between them. Remove one of the bindings, or give the two values distinct types.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ContextTypeNotNameable = new(
        DiagnosticNumber.Mint("MIST_CTX_0004"),
        Title: "Context type not nameable",
        Template: "Cannot determine a context type from this expression: its type '{0}' holds an anonymous type, which no `with` clause can name.",
        Description: "The value offered as context has a type built from an anonymous type, and a context requirement can only be written for a type that has a spelling. Give the value a declared type, for instance by projecting the anonymous members into a named struct or class.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ContextVariableNotReassignable = new(
        DiagnosticNumber.Mint("MIST_CTX_0005"),
        Title: "Context variable not reassignable",
        Template: "Context-bound variable '{0}' is not reassignable{1}.",
        Description: "A variable introduced by a context declaration was assigned after its binding, but a context value is fixed for the scope that binds it so every consumer observes the same instance. Compute the final value before binding it, or copy it into an ordinary local if a mutable version is needed.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ContextContractNotMet = new(
        DiagnosticNumber.Mint("MIST_CTX_0006"),
        Title: "Context contract not met",
        Template: "Method '{0}' on type '{1}' must declare the same `with` clauses as the slot it fills on abstract '{2}': {3}.",
        Description: "A method filling an abstract slot requires different context than the slot advertises, so a caller holding only the abstract type could not know what to supply. Make the implementing method's context requirements identical to the slot's, or amend the abstract declaration.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);
}