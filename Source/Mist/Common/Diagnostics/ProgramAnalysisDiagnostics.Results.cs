namespace Mist.Common.Diagnostics;

public static partial class ProgramAnalysisDiagnostics
{
    public static readonly MistDiagnostic ResultPropagationDestination = new(
        DiagnosticNumber.Mint("MIST0483"),
        Title: "Propagation needs an error return destination",
        Template: "Error propagation requires a compatible error return destination in this callable.",
        Description: "Use a Result-returning factory instead of a constructor, or declare a return type that accepts the propagated error.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ResultPropagationError = new(
        DiagnosticNumber.Mint("MIST0480"),
        Title: "Incompatible propagated error",
        Template: "The propagated error is not assignable to this callable's error return destination. Map it explicitly.",
        Description: "Propagation selects the failure alternative and requires ordinary error assignability, including admitted error unions.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ResultErrorIsNotIdentity = new(
        DiagnosticNumber.Mint("MIST0482"),
        Title: "An error is not propagation identity",
        Template: "A proven error alternative is not a successful plain value. Return it explicitly as a failure or map it.",
        Description: "Identity propagation preserves a plain success value; it cannot reinterpret an inspected failure as success.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic TupleCorrelationUnproven = new(
        DiagnosticNumber.Mint("MIST0481"),
        Title: "Tuple alternative unproven",
        Template: "This pattern does not establish one correlated tuple alternative before binding its payload.",
        Description: "Match the tuple's discriminant to establish the payload type of the same evaluated value.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);
}