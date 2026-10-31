#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

/// <summary>
/// Diagnostic descriptors for the C# backend's component translator: the markup parser's refusals
/// of malformed component files, and the translator's refusals of components it cannot lower.
/// </summary>
public static class RazorDiagnostics
{
    public static readonly MistDiagnostic UnclosedElement = new(
        DiagnosticNumber.Mint("MIST0462"),
        Title: "Unclosed element",
        Template: "The element '{element}' is opened here and never closed.",
        Description: "A start tag in a component file has no matching end tag before its parent closes or the file ends, so the markup parser cannot tell where its children stop and produces no component for the file. Close the element with its end tag, or write it self-closing when it has no children; void HTML elements such as 'br' and 'input' need neither.",
        Category: DiagnosticCategory.Syn,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("element", SlotKind.Name)]);

    public static readonly MistDiagnostic MismatchedEndTag = new(
        DiagnosticNumber.Mint("MIST0463"),
        Title: "Mismatched end tag",
        Template: "The end tag '</{name}>' matches no open element{open?; the innermost open element is '<{open}>'}.",
        Description: "An end tag in a component file names no element that is open at that point, so it cannot close anything and the markup parser produces no component for the file. Correct the end tag's name to the element it was meant to close, or remove it if the element was already closed.",
        Category: DiagnosticCategory.Syn,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name), new Slot("open", SlotKind.Optional, SlotKind.Name)]);

    public static readonly MistDiagnostic UnterminatedCodeRegion = new(
        DiagnosticNumber.Mint("MIST0464"),
        Title: "Unterminated code region",
        Template: "The {region} opened by this transition is never closed.",
        Description: "A transition into Mist code, an explicit expression, a statement or control block, a code block or a comment, has no balancing close before the end of the file, so the markup parser cannot tell where the code stops and produces no component for the file. Brackets inside Mist string, character and regex literals and line comments are not counted, so look for a bracket the code itself leaves open.",
        Category: DiagnosticCategory.Syn,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("region", SlotKind.Enum)]);

    public static readonly MistDiagnostic UnknownDirective = new(
        DiagnosticNumber.Mint("MIST0465"),
        Title: "Unknown directive",
        Template: "'@{name}' begins a directive line but names no directive a component accepts.",
        Description: "A line at the top level of a component file has the shape of a directive, a lowercase name after '@' followed by an argument, but the name is not one the backend's component dialect defines. Correct the directive's name, or write '@(name)' when the line is meant to render an expression followed by text.",
        Category: DiagnosticCategory.Syn,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name)]);

    public static readonly MistDiagnostic DirectiveNotValidInComponent = new(
        DiagnosticNumber.Mint("MIST0466"),
        Title: "Directive not valid in a component",
        Template: "'@{name}' is a directive of MVC views and pages, which a component does not accept.",
        Description: "The directive belongs to Razor views and pages, such as '@model', '@section' or '@addTagHelper', and has no meaning in a component, so the file produces no component. Remove it; a component receives data through parameters, and tag helpers do not apply to components.",
        Category: DiagnosticCategory.Syn,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name)]);

    public static readonly MistDiagnostic FunctionsDirectiveNotSupported = new(
        DiagnosticNumber.Mint("MIST0467"),
        Title: "Functions directive not supported",
        Template: "'@functions' is not supported; declare the members in an '@code' block.",
        Description: "Razor accepts '@functions' as an older spelling of '@code', but the backend's component dialect keeps one member-declaring directive so that a component's members have one home. Rename the directive to '@code'; its body is copied into the component unchanged.",
        Category: DiagnosticCategory.Syn,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic InvalidTransition = new(
        DiagnosticNumber.Mint("MIST0468"),
        Title: "Invalid transition",
        Template: "'@' is not followed by anything a transition admits here.",
        Description: "In markup, '@' must be followed by an identifier, a control keyword, '(', '{', '*' or a second '@', and inside a code block also by ':'; anything else, such as whitespace or a digit, leaves the parser without a region to enter, so the file produces no component. Write '@@' for a literal '@', or complete the expression the transition was meant to start.",
        Category: DiagnosticCategory.Syn,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);

    public static readonly MistDiagnostic UnknownComponent = new(
        DiagnosticNumber.Mint("MIST0469"),
        Title: "Unknown component",
        Template: "'<{name}>' names no component in scope.",
        Description: "A tag whose name begins with an uppercase letter is taken for a component, but no component type of that name is reachable through the file's '@using' directives, its imports files, the project's global usings or its own namespace. Add the '@using' for the component's namespace or correct the name; an HTML element is written in lowercase.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name)]);

    public static readonly MistDiagnostic AmbiguousComponentName = new(
        DiagnosticNumber.Mint("MIST0470"),
        Title: "Ambiguous component name",
        Template: "'<{name}>' could be any of {candidates}.",
        Description: "More than one component type of the tag's name is reachable through the namespaces in scope for the file, and the translator will not pick one. Qualify the tag with the namespace of the intended component, or remove the '@using' that brings the other into scope.",
        Category: DiagnosticCategory.Nam,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name), new Slot("candidates", SlotKind.Sequence, SlotKind.Type)]);

    public static readonly MistDiagnostic NestedTypeInCodeBlock = new(
        DiagnosticNumber.Mint("MIST0471"),
        Title: "Nested type in code block",
        Template: "'{name}' is a type declared inside an '@code' block; declare it in a .mist file.",
        Description: "The members of an '@code' block are copied into the component's class, and a component file declares no type or namespace of its own outside them. Move the type declaration to a .mist file in the project, where the component can use it like any other type.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name)]);

    public static readonly MistDiagnostic ReservedIdentifier = new(
        DiagnosticNumber.Mint("MIST0472"),
        Title: "Reserved identifier",
        Template: "'{name}' begins with two underscores, a prefix reserved for identifiers the translator generates.",
        Description: "The translator writes its render method and every other synthesized identifier with a double-underscore prefix, so a member of an '@code' block that uses the prefix could collide with one of them. Rename the member without the leading double underscore.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("name", SlotKind.Name)]);

    public static readonly MistDiagnostic ExpressionParameterNotSupported = new(
        DiagnosticNumber.Mint("MIST0473"),
        Title: "Expression parameter not supported",
        Template: "'@bind-{parameter}' cannot bind '{component}', which declares '{parameter}Expression'; Mist has no expression trees.",
        Description: "Razor fills a component's expression parameter from a bind with a lambda converted to an expression tree, and Mist has no expression trees to convert it to. Bind the value and its change callback as two ordinary parameters instead, or use a component that declares no expression parameter.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("parameter", SlotKind.Name), new Slot("component", SlotKind.Type)]);

    public static readonly MistDiagnostic GenericComponentNotSupported = new(
        DiagnosticNumber.Mint("MIST0474"),
        Title: "Generic component not supported",
        Template: "'{component}' declares type parameters, and a generic component cannot be used from markup yet.",
        Description: "Using a generic component from markup needs its type arguments, which Razor infers from the parameter values or reads from type-argument attributes, and the translator does not yet decide them. Wrap the component in a non-generic one declared in Mist, or construct its render fragment in code.",
        Category: DiagnosticCategory.Bck,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error,
        Slots: [new Slot("component", SlotKind.Type)]);

    public static readonly MistDiagnostic ConstructorBesideInject = new(
        DiagnosticNumber.Mint("MIST0475"),
        Title: "Constructor beside inject",
        Template: "The component declares a constructor and also '@inject'; take the injected services as the constructor's parameters.",
        Description: "Each '@inject' becomes a parameter of one constructor the translator synthesizes, and a constructor written in the '@code' block would be a second one that leaves the injected fields unassigned. Remove the '@inject' directives and declare the services among the written constructor's parameters, assigning them to fields there.",
        Category: DiagnosticCategory.Dec,
        Policy: PolicyClass.Fixed,
        DefaultSeverity: DiagnosticSeverity.Error);
}