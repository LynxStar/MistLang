namespace Mist.Common.Diagnostics;

public static partial class ProgramModelDiagnostics
{
    public static readonly MistDiagnostic SpreadValueParameter = new(
        DiagnosticNumber.Mint("MIST_PM_SPREAD_VALUE_PARAMETER"),
        Title: "Spread suffix on value parameter",
        Template: "'{0}' uses a spread suffix in a value-parameter declaration. Declare a value pack with 'params' instead: 'params TArgs {0}'.",
        Description: "A value parameter is written with the '...' suffix, which belongs to type-argument positions; a parameter that accepts a variable number of values is declared with 'params' over a type pack. Rewrite the parameter in the 'params' form.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic SpreadNonPackArgument = new(
        DiagnosticNumber.Mint("MIST_PM_SPREAD_NON_PACK_ARGUMENT"),
        Title: "Spread into non-pack slot",
        Template: "'{0}...' cannot fill a fixed type-argument slot of '{1}'. A symbolic spread requires a pack slot that accepts every permitted width.",
        Description: "A symbolic spread was supplied where the generic declares a single type-argument slot, but a spread stands for any permitted number of arguments and only a pack slot can take every width. Pass one type argument, or change the declaration to take a pack in that position.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);
}