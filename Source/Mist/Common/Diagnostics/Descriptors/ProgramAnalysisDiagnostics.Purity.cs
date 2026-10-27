#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

public static partial class ProgramAnalysisDiagnostics
{
    public static readonly MistDiagnostic PureScopeWrites = new(
        DiagnosticNumber.Mint("MIST0137"),
        Title: "Pure scope writes",
        Template: "Cannot write {target} from {scope}: {reason}.",
        Description: "Code running under a purity restriction mutates state it does not own, and a restricted scope may change only what it created itself. Return the new value instead of mutating in place, or drop the purity modifier from the scope if the write is intended.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("target", SlotKind.Name), new Slot("scope", SlotKind.Name), new Slot("reason", SlotKind.Name)]);

    public static readonly MistDiagnostic PureScopeReadsMutable = new(
        DiagnosticNumber.Mint("MIST0138"),
        Title: "Pure scope reads mutable state",
        Template: "Cannot read {target} from {scope}: {reason}.",
        Description: "Code running under a purity restriction observes state that can change between calls, so the scope's result would depend on when it runs. Pass the value in as an argument, make the state immutable, or drop the purity modifier if the dependency is intended.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("target", SlotKind.Name), new Slot("scope", SlotKind.Name), new Slot("reason", SlotKind.Name)]);

    public static readonly MistDiagnostic PureScopeCallsImpure = new(
        DiagnosticNumber.Mint("MIST0139"),
        Title: "Pure scope calls impure member",
        Template: "Cannot {action} from {scope}: it is {actual}; {required} would be needed.",
        Description: "Code running under a purity restriction invokes a member whose declared purity is weaker than the scope demands, and effects reached through a call count as the caller's own. Call a member that meets the requirement, mark the callee with the stronger level if it qualifies, or relax the caller's restriction.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("action", SlotKind.Name), new Slot("scope", SlotKind.Name), new Slot("actual", SlotKind.Enum), new Slot("required", SlotKind.Enum)]);

    public static readonly MistDiagnostic LocalScopeReadsArgument = new(
        DiagnosticNumber.Mint("MIST0140"),
        Title: "Local scope reads through argument",
        Template: "Cannot read {target} through argument '{argument}' from {scope}: a local scope uses its arguments as values and reads nothing reachable through a reference-typed or by-reference one.",
        Description: "A local scope dereferences one of its arguments to reach state outside itself, which the strictest restriction forbids because such a read observes memory the scope was not handed as a value. Pass the needed data by value, or declare the scope with a weaker restriction.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("target", SlotKind.Name), new Slot("scope", SlotKind.Name), new Slot("argument", SlotKind.Name)]);

    public static readonly MistDiagnostic PureScopeStoresUnowned = new(
        DiagnosticNumber.Mint("MIST0141"),
        Title: "Pure scope stores unowned value",
        Template: "Cannot store {value} into an instance the scope owns from {scope}: it is {reason} and its type can change, so the instance would outlive what it holds; only what the scope owns, or a value that cannot change, may be stored.",
        Description: "Code running under a purity restriction places a reference it does not own, to a value whose type permits mutation, inside an instance the scope created, so outside mutable state would escape through the result. Store a copy, store something the scope owns, or store a value of an immutable type.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("value", SlotKind.Name), new Slot("scope", SlotKind.Name), new Slot("reason", SlotKind.Name)]);

    public static readonly MistDiagnostic PureFunctionRequired = new(
        DiagnosticNumber.Mint("MIST0142"),
        Title: "Pure function required",
        Template: "Cannot convert '{source}' to '{target}': it is {reason}, and a method group converts to a pure function type only when its method is pure.",
        Description: "A method group is being turned into a value of a pure function type, but the method behind it is not declared pure, and the conversion is the one place the promise can be checked. Mark the method 'pure' if it qualifies, or convert to a function type without the purity requirement.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("source", SlotKind.Name), new Slot("target", SlotKind.Type), new Slot("reason", SlotKind.Name)]);

    public static readonly MistDiagnostic DefaultNotConsistent = new(
        DiagnosticNumber.Mint("MIST0143"),
        Title: "Default value not consistent",
        Template: "{scope} would not give the same default every time. Every default of '{type}' is evaluated as a pure scope with no inputs.",
        Description: "A default expression could produce a different value on different occasions, because each default runs as a pure scope with no inputs and must yield an identical result on every evaluation. Replace it with a constant or a pure computation over constants.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("scope", SlotKind.Name), new Slot("type", SlotKind.Type)]);

    public static readonly MistDiagnostic PurityContractNotMet = new(
        DiagnosticNumber.Mint("MIST0144"),
        Title: "Purity contract not met",
        Template: "Member '{member}' in type '{type}' must be {required} to satisfy the {required} contract on '{slot}' of abstract '{abstractType}': it is {actual}.",
        Description: "A member filling a slot on an abstract type is less pure than the slot declares, so a caller trusting the abstract contract could observe effects it was promised would not occur. Give the implementation the purity the slot requires, or weaken the contract on the abstract type.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("member", SlotKind.Name), new Slot("type", SlotKind.Type), new Slot("required", SlotKind.Enum), new Slot("slot", SlotKind.Name), new Slot("abstractType", SlotKind.Type), new Slot("actual", SlotKind.Enum)]);

    public static readonly MistDiagnostic PureFunctionTypeUntestable = new(
        DiagnosticNumber.Mint("MIST0145"),
        Title: "Pure function type untestable",
        Template: "'{source}' cannot be tested for or cast to '{target}': a pure function type is a promise the runtime cannot see, so only a value whose static type already states it can be read as one.",
        Description: "A value is type-tested against, or cast to, a pure function type. Purity is a compile-time guarantee with no runtime representation, so the check could not distinguish a pure function from a plain one of the same shape. Rely on the static type instead: accept the pure function type where it is needed, or test against the unqualified function type.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("source", SlotKind.Type), new Slot("target", SlotKind.Type)],
        Related: [DiagnosticNumber.Mint("MIST0146")]);

    public static readonly MistDiagnostic PureFunctionTypeUntestableClosing = new(
        DiagnosticNumber.Mint("MIST0146"),
        Title: "Untestable pure function type argument",
        Template: "Cannot close '{parameter}' of '{generic}' with '{argument}': '{member}' tests a value of type '{source}' against '{target}', and '{pureType}' is a promise the runtime cannot see, so the test could read a plain function value as one.",
        Description: "A generic is instantiated with a pure function type as a type argument, and a member inside it type-tests a value against that type parameter. Purity has no runtime representation, so the instantiated check could accept a plain function as a pure one. Close the generic with the unqualified function type, or restructure the generic so it does not test values against its type parameter.",
        Category: DiagnosticCategory.Eff,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("parameter", SlotKind.Name), new Slot("generic", SlotKind.Name), new Slot("argument", SlotKind.Type), new Slot("member", SlotKind.Name), new Slot("source", SlotKind.Type), new Slot("target", SlotKind.Type), new Slot("pureType", SlotKind.Type)],
        Related: [DiagnosticNumber.Mint("MIST0145")]);
}