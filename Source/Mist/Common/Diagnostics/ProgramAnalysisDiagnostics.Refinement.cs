namespace Mist.Common.Diagnostics;

public static partial class ProgramAnalysisDiagnostics
{
    public static readonly MistDiagnostic RegexInvalid = new(
        DiagnosticNumber.Mint("MIST0270"),
        Title: "Invalid regex pattern",
        Template: "Invalid regex pattern: {message}",
        Description: "The regular expression literal fails to parse, for the reason the regex engine reports. Correct the pattern syntax.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("message", SlotKind.Text)]);

    public static readonly MistDiagnostic RefinementViolated = new(
        DiagnosticNumber.Mint("MIST0271"),
        Title: "Refinement violated",
        Template: "Value '{value}' does not satisfy refinement '{predicate}' on type '{type}'.",
        Description: "The constant value is known at compile time and fails the refinement predicate of its target type. Use a value the refinement admits.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("value", SlotKind.Name), new Slot("predicate", SlotKind.Name), new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic RefinementUnproven = new(
        DiagnosticNumber.Mint("MIST0272"),
        Title: "Refinement unproven",
        Template: "Cannot assign '{source}' to '{target}' without proving its refinement '{predicate}': narrow the value with a pattern first.",
        Description: "The value's type is the refined type's base, and nothing on this path establishes that the predicate holds. Test the value with a pattern first, or produce it from a source already typed with the refinement.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("source", SlotKind.Type), new Slot("target", SlotKind.Type), new Slot("predicate", SlotKind.Name)]);

    public static readonly MistDiagnostic RefinementNotImplied = new(
        DiagnosticNumber.Mint("MIST0273"),
        Title: "Refinement not implied",
        Template: "Cannot assign '{source}' to '{target}': '{known}' does not imply '{required}'.",
        Description: "The source carries one refinement and the target demands another, and the first does not entail the second. Narrow the value with a pattern that proves the target refinement.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("source", SlotKind.Type), new Slot("target", SlotKind.Type), new Slot("known", SlotKind.Name), new Slot("required", SlotKind.Name)]);

    public static readonly MistDiagnostic IndexOutOfBounds = new(
        DiagnosticNumber.Mint("MIST0274"),
        Title: "Index out of bounds",
        Template: "Index {index} is outside the bounds of '{target}', which needs {predicate}.",
        Description: "The constant index lies outside the range the indexed collection's dependent bound permits. Use an index within the collection's length.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("index", SlotKind.Name), new Slot("target", SlotKind.Name), new Slot("predicate", SlotKind.Name)]);

    public static readonly MistDiagnostic IndexUnproven = new(
        DiagnosticNumber.Mint("MIST0275"),
        Title: "Index not proven in bounds",
        Template: "Index '{index}' is not proven within the bounds of '{target}', which needs {predicate}.",
        Description: "The indexed collection's length is a dependent type, and nothing on this path proves the index lies within it. Guard the access with a bounds pattern, or use an index typed to the collection's length.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("index", SlotKind.Name), new Slot("target", SlotKind.Name), new Slot("predicate", SlotKind.Name)]);

    public static readonly MistDiagnostic ConstraintViolated = new(
        DiagnosticNumber.Mint("MIST0276"),
        Title: "Refinement constraint violated",
        Template: "Value '{value}' violates the refinement constraint '{predicate}' on parameter '{parameter}'.",
        Description: "The constant argument fails the refinement constraint on the parameter it is passed to. Pass a value the constraint admits.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("value", SlotKind.Name), new Slot("predicate", SlotKind.Name), new Slot("parameter", SlotKind.Name)]);

    public static readonly MistDiagnostic ConstraintUnproven = new(
        DiagnosticNumber.Mint("MIST0277"),
        Title: "Refinement constraint unproven",
        Template: "Argument '{argument}' is not proven to satisfy the refinement constraint '{predicate}' on parameter '{parameter}'.",
        Description: "The argument's type does not establish the refinement constraint on the parameter, and no narrowing on this path proves it. Test the argument with a pattern first, or pass a value already typed with the refinement.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("argument", SlotKind.Name), new Slot("predicate", SlotKind.Name), new Slot("parameter", SlotKind.Name)]);

    public static readonly MistDiagnostic DependentArgumentViolated = new(
        DiagnosticNumber.Mint("MIST0278"),
        Title: "Dependent argument violated",
        Template: "Dependent type argument '{argument}' does not satisfy '{predicate}' on parameter '{parameter}'.",
        Description: "The argument's dependent type does not meet the dependent constraint the parameter places on it. Pass an argument whose dependent type satisfies the constraint.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("argument", SlotKind.Name), new Slot("predicate", SlotKind.Name), new Slot("parameter", SlotKind.Name)]);

    public static readonly MistDiagnostic DependentForwardUnproven = new(
        DiagnosticNumber.Mint("MIST0279"),
        Title: "Dependent forwarding unproven",
        Template: "Dependent parameter '{parameter}' cannot be forwarded to '{target}': {known} is not proven to imply '{required}'.",
        Description: "A dependent parameter is forwarded to another call whose parameter demands more than the forwarder's own constraint guarantees. Strengthen the forwarder's constraint, or prove the stronger condition before the call.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("parameter", SlotKind.Name), new Slot("target", SlotKind.Name), new Slot("known", SlotKind.Name), new Slot("required", SlotKind.Name)]);

    public static readonly MistDiagnostic DependentArgumentNotAssignable = new(
        DiagnosticNumber.Mint("MIST0280"),
        Title: "Dependent argument not assignable",
        Template: "Dependent type argument '{argument}{argumentValue}' of type '{argumentType}' is not assignable to dependent parameter '{parameter}' of type '{parameterType}'.",
        Description: "The dependent type argument has a type that does not convert to the dependent parameter's declared type. Pass an argument of the parameter's type.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("argument", SlotKind.Optional, SlotKind.Type), new Slot("argumentValue", SlotKind.Optional, SlotKind.Name), new Slot("argumentType", SlotKind.Type), new Slot("parameter", SlotKind.Name), new Slot("parameterType", SlotKind.Type)]);

    public static readonly MistDiagnostic DependentForwardNotConvertible = new(
        DiagnosticNumber.Mint("MIST0281"),
        Title: "Dependent forwarding not convertible",
        Template: "Dependent type parameter '{parameter}' of type '{type}' on '{source}{sourceName}' cannot be forwarded to parameter '{targetParameter}' of type '{targetType}' on '{target}{targetName}' because '{type}' is not implicitly convertible to '{targetType}'.",
        Description: "The dependent parameter being forwarded has a type that does not implicitly convert to the type of the callee's dependent parameter. Convert the value explicitly, or align the two parameter types.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("parameter", SlotKind.Name), new Slot("type", SlotKind.Type), new Slot("source", SlotKind.Optional, SlotKind.Type), new Slot("sourceName", SlotKind.Optional, SlotKind.Name), new Slot("targetParameter", SlotKind.Name), new Slot("targetType", SlotKind.Type), new Slot("target", SlotKind.Optional, SlotKind.Type), new Slot("targetName", SlotKind.Optional, SlotKind.Name)]);

    public static readonly MistDiagnostic DependentParameterConflictsWithMember = new(
        DiagnosticNumber.Mint("MIST0282"),
        Title: "Dependent parameter name conflict",
        Template: "Dependent type parameter '{parameter}' conflicts with member '{parameter}' declared in type '{type}'. Rename either the parameter or the member.",
        Description: "A dependent type parameter shares its name with a member of the declaring type, so references inside the type would be ambiguous. Rename one of them.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("parameter", SlotKind.Name), new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic ConstraintStrengthened = new(
        DiagnosticNumber.Mint("MIST0283"),
        Title: "Implementation strengthens constraint",
        Template: "'{member}' constrains parameter '{parameter}' more than the member it implements: '{known}{knownPredicate}' does not imply '{required}{requiredPredicate}'.",
        Description: "The implementing member places a refinement constraint on a parameter that the member it implements does not, so a caller through the base could pass a value the implementation rejects. Weaken the constraint, or add it to the implemented member.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name), new Slot("parameter", SlotKind.Name), new Slot("known", SlotKind.Optional, SlotKind.Type), new Slot("knownPredicate", SlotKind.Optional, SlotKind.Name), new Slot("required", SlotKind.Optional, SlotKind.Type), new Slot("requiredPredicate", SlotKind.Optional, SlotKind.Name)]);

    public static readonly MistDiagnostic RefinementReturnUnproven = new(
        DiagnosticNumber.Mint("MIST0284"),
        Title: "Refinement return unproven",
        Template: "Member '{member}' produces '{type}', which is not proven to satisfy the refinement contract '{predicate}' required by '{slot}'.",
        Description: "The member implements a contract whose result carries a refinement, and the value it produces is not proven to satisfy that refinement. Return a value typed with the refinement, or narrow it with a pattern before returning.",
        Category: DiagnosticCategory.Typ,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name), new Slot("type", SlotKind.Type), new Slot("predicate", SlotKind.Type), new Slot("slot", SlotKind.Type)]);

    public static readonly MistDiagnostic BaseConstructorUnsatisfied = new(
        DiagnosticNumber.Mint("MIST0285"),
        Title: "Base constructor arguments missing",
        Template: "Base class '{type}' requires constructor arguments that are not provided.",
        Description: "The base class has no parameterless constructor, and the derived constructor passes no arguments to any of its constructors. Add a base constructor call with arguments, or give the base a parameterless constructor.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic PropertyHasNoDefault = new(
        DiagnosticNumber.Mint("MIST0286"),
        Title: "Property without default",
        Template: "Property '{property}' of '{type}' has no default, which '{type}' needs because it declares {declaration}: its type '{propertyType}' has none. Give the property an initializer, or give '{type}' a constructor that assigns it and no default block.",
        Description: "The containing type promises a default, which must give every property a value, and this property's type has none. Give the property an initializer, or replace the type's default with a constructor that assigns it.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("property", SlotKind.Name), new Slot("type", SlotKind.Type), new Slot("declaration", SlotKind.Enum), new Slot("propertyType", SlotKind.Type)]);

    public static readonly MistDiagnostic RefinementZeroNotAdmitted = new(
        DiagnosticNumber.Mint("MIST0287"),
        Title: "Refined default rejects zero",
        Template: "'{member}' of '{type}' has no default, which '{type}' needs because it declares {declaration}: its type '{refined}' would start from the zero of '{base}', which '{refined}' does not admit. Give '{member}' a value '{refined}' admits, by an initializer or a default block entry, or give '{type}' a constructor that assigns it and no default block.",
        Description: "The containing type promises a default, and a refined member of it would start from the zero of its underlying type, which the refinement does not admit. Initialize the member with an admitted value, through an initializer or a default block entry, or replace the type's default with a constructor that assigns it.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name), new Slot("type", SlotKind.Type), new Slot("declaration", SlotKind.Enum), new Slot("refined", SlotKind.Type), new Slot("base", SlotKind.Type)]);
}