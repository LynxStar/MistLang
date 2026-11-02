namespace Mist.Common.Diagnostics;

public static partial class ProgramModelDiagnostics
{
    public static readonly MistDiagnostic InvalidArithmetic = new(
        DiagnosticNumber.Mint("MIST0484"),
        Title: "Operation proven invalid",
        Template: "This operation is statically proven to violate its panic-prevention conditions, so it is refused rather than emitted.",
        Description: "An integer division or remainder is proven at compile time to divide by zero or to overflow (the signed minimum divided by negative one). Return a Result failure explicitly, or establish operands that satisfy the operation's preconditions.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);
}