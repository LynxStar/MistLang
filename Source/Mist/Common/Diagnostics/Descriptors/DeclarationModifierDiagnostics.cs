#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

public static class DeclarationModifierDiagnostics
{
    public static readonly MistDiagnostic RefOnOperator = new(
        DiagnosticNumber.Mint("MIST0040"),
        Title: "Ref modifier on operator",
        Template: "The 'ref' modifier is not valid on an operator: an operator returns its result by value, and only a method can return a reference.",
        Description: "An operator declaration is marked as returning a reference, but operators always produce their result by value and reference returns are a capability of methods alone. Remove the modifier, or declare a named method if a reference-returning member is wanted.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic RefOnOperatorParameter = new(
        DiagnosticNumber.Mint("MIST0041"),
        Title: "Ref parameter on operator",
        Template: "The 'ref' modifier is not valid on parameter '{0}' of {1}: an operator is applied without an argument list, so no use of it can write the 'ref' a reference argument needs. Take the parameter by value.",
        Description: "An operator parameter is declared by reference, but an operator is invoked through its symbol rather than a call, so no use site has a place to mark an operand as a reference argument. Declare the parameter by value.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ReadonlyRefOnOperatorParameter = new(
        DiagnosticNumber.Mint("MIST0042"),
        Title: "Readonly ref parameter on operator",
        Template: "The 'readonly ref' modifier is not valid on parameter '{0}' of {1}: an operator takes its operands by value. Take the parameter by value.",
        Description: "An operator parameter is declared as a read-only reference, but operands reach an operator as copies and the syntax of an operator application has no place to pass an alias. Declare the parameter by value.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic SealedOnOperator = new(
        DiagnosticNumber.Mint("MIST0043"),
        Title: "Sealed modifier on operator",
        Template: "The 'sealed' modifier is not valid on an operator declaration.",
        Description: "An operator is marked 'sealed', a modifier that only means something on a member that could otherwise be overridden, and operators are never virtual. Remove the modifier.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic ReadonlyOnOperator = new(
        DiagnosticNumber.Mint("MIST0044"),
        Title: "Readonly modifier on operator",
        Template: "The 'readonly' modifier is not valid on an operator declaration; use 'pure' for a pure operator.",
        Description: "An operator is marked 'readonly', which on a method promises not to mutate the receiver, and an operator has no receiver to protect. Remove the modifier, or write 'pure' if the intent is to declare the operator free of effects.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic OwnedOnOperator = new(
        DiagnosticNumber.Mint("MIST0045"),
        Title: "Owned modifier on operator",
        Template: "The 'owned' modifier is not valid on an operator declaration.",
        Description: "An operator is marked 'owned', an ownership modifier that describes how a value or receiver is held and has no meaning on an operator. Remove the modifier.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic AffineOnOperator = new(
        DiagnosticNumber.Mint("MIST0046"),
        Title: "Affine modifier on operator",
        Template: "The 'affine' modifier is not valid on an operator declaration.",
        Description: "An operator is marked 'affine', an ownership modifier that constrains how often a value may be used and has no meaning on an operator. Remove the modifier.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic RefOnField = new(
        DiagnosticNumber.Mint("MIST0047"),
        Title: "Ref modifier on field",
        Template: "The 'ref' modifier is not valid on a field: a reference is a stack-only alias, so no field can store one. Declare the field by value, or take the reference as a ref local where it is used.",
        Description: "A field is declared by reference, but a reference cannot outlive the frame that produced it and a field lives as long as its containing instance. Store the value itself in the field and create a ref local at the point of use when an alias is needed.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic StaticOnAbstractMember = new(
        DiagnosticNumber.Mint("MIST0048"),
        Title: "Static member on abstract type",
        Template: "The 'static' modifier is not valid on {0} '{1}' of abstract type '{2}': an abstract type declares the instance members a conforming type supplies, and a static member is no part of that contract. Declare it on a concrete type instead.",
        Description: "An abstract type declares a static member, but an abstract type exists only to list the instance members a conforming type must provide, so there is no conforming type for a static member to bind to. Move the member to a concrete type, or make it an instance member of the abstract type.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);
}