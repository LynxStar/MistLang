# Requirements Document

## Introduction

A Blazor application written in Mist today is a C# web project that happens to reference a Mist
library. The Virtue spike of 2026-10-21 (`task-6`) measured the alternative directly: `mist build`
discovers only `*.mist`, a `.razor` file renamed to `.mist` fails lexing at the first `@`, and the stock
Razor compiler copies any `@` region into C# unchanged, so valid Mist such as `int.Name`, `absent is void`
and a `pure` member reaches Roslyn and fails there. The goal this feature serves is the one the spike could
not reach: an ordinary Blazor application, with ordinary Razor markup, in which every position that would
hold C# in a C# Blazor application holds Mist instead: the host program, `@code` blocks, `@` expressions,
control-flow headers, handler lambdas, directive arguments and the services and models behind them, all in
one Mist project built by `mist build`.

Razor is a feature of the C# backend, not of the language. A Rust backend has no Razor, so the compiler core
must learn nothing about markup. The mechanism specified here is a **backend-claimed input**: a backend
declares the file extensions it accepts beyond `.mist` and, for each such file, hands the driver Mist source
with a map back to the original text. The C# backend claims `.razor` and translates each component into a
Mist class deriving `ComponentBase` whose `@code` members are copied as written and whose markup becomes a
render method built from the same `RenderTreeBuilder` calls the Razor compiler itself emits. From that point
the ordinary lexer, parser, program model, analyzers and emitter run unchanged, so Mist's own rules judge the
component and Mist's own diagnostics report it, at the `.razor` line. No Razor source generator, Web SDK or
MSBuild takes part in the compile.

Probes on 2026-10-24 and 2026-10-30 against a compiler built from `main` fixed what the translation can rely
on. The whole ASP.NET host compiles from Mist and emits the template's C#. A `ComponentBase` subclass that
builds a render tree by hand, creates `EventCallback` values from a method group or a lambda, declares
`RenderFragment` locals and casts, carries `[Route("/x")]` and `[Layout(MainLayout)]`, and declares an
`async` lifecycle hook compiles with zero warnings and emits the Razor-identical C#. Two gaps are outside this
feature and gate it. A Mist member that fills a CLR virtual or abstract slot is emitted without `override`
(a method on `ComponentBase` is silently hidden; a property on `RenderModeAttribute` is refused as a
redeclaration), which `clr-slot-filling` specifies. A property whose type has no default and no initializer
is refused by defaultability; Requirement 10 records how a component stays inside that rule: an injected
service arrives through the constructor, and a parameter the parent may omit is optional.

This spec layers on `inferred-namespaces` (a component's namespace), `delegate-func-conversion` (lambdas
and method groups into `RenderFragment` and `EventCallback`), `abstract-type-unification` Requirement 25
and `clr-slot-filling` (lifecycle overrides), `native-runtime-assets` (deploying files beside the output)
and `node-origin-tracking` and `diagnostics` (locations). Where it restates one of their rules it does so
only to extend it.

## Glossary

- **Backend**: the component selected by `BackendId`; today only `csharp`, realized by
  `CSharpEmitterV2` and `CSharpCompilationDriver`.
- **Claimed_Extension**: a file extension other than `.mist` that a Backend declares it translates.
- **Claimed_Input**: a source file under a project folder whose extension is a Claimed_Extension of the
  selected Backend.
- **Source_Translator**: the Backend component that turns the Claimed_Inputs of one extension into
  Translated_Units.
- **Translated_Unit**: a Mist `SourceText` produced by a Source_Translator, together with its Origin_Map,
  that enters the compilation as a `CompilationUnit` exactly as a `.mist` file does.
- **Origin_Map**: the ordered list of segments that say, for each range of a Translated_Unit, which range of
  which original file it was copied from or synthesized for.
- **Translation_Stage**: the pipeline step, after the `.mist` units are parsed and before the program model
  is built, in which every Source_Translator runs.
- **Component_File**: a Claimed_Input with the extension `.razor`.
- **Imports_File**: a Component_File named `_Imports.razor`, which contributes directives to every
  Component_File in its folder and below and declares no component.
- **Component_Class**: the Mist class a Component_File translates to.
- **Render_Method**: the `BuildRenderTree(RenderTreeBuilder)` member of a Component_Class that the
  translation synthesizes from the markup.
- **Code_Block**: the body of a `@code { ... }` directive, Mist member declarations copied into the
  Component_Class verbatim.
- **Code_Region**: any span of a Component_File that holds Mist text: an expression, a control-flow header,
  a statement block, an attribute value expression, a directive argument or a Code_Block.
- **Markup_Region**: any span of a Component_File that is not a Code_Region: elements, attributes, text,
  comments and component tags.
- **Component_Catalog**: the facts the Source_Translator gathers before translating: which type names in
  scope are components, each component's parameters with their declared types, its type parameters, and
  the event-argument type of each DOM event name.
- **Render_Protocol**: the sequence of `RenderTreeBuilder` calls a Markup_Region lowers to, in the shape the
  Razor compiler emits for the same markup.
- **Host_Assigned_Member**: a property the runtime assigns after construction rather than the program: a
  `[Parameter]` or `[CascadingParameter]` property. An injected service is not one; it arrives through the
  constructor (Requirement 8.4).
- **Framework_Script**: `_framework/blazor.web.js`, served to the browser by the host and supplied by the
  `Microsoft.AspNetCore.App.Internal.Assets` package.

## Requirements

### Requirement 1: Backend-claimed inputs

**User Story:** As a compiler maintainer, I want a backend to declare which files beyond `.mist` it accepts,
so that a target-specific input such as Razor lives entirely in its backend and the core needs no knowledge
of it.

#### Acceptance Criteria

1. THE Backend SHALL expose its Source_Translators, each naming one or more Claimed_Extensions, through a
   contract under `Source/Mist/Backends/Contracts/`, and the Compiler_Driver SHALL obtain them from a
   registry keyed by `BackendId` rather than from a type it names.
2. WHEN the Compiler_Driver or the CLI enumerates a project's sources, THE enumeration SHALL include every
   file whose extension is a Claimed_Extension of the selected Backend, under the root project and under
   every referenced Mist project, excluding generated and output folders exactly as `*.mist` enumeration
   does today.
3. THE core SHALL name no extension other than `.mist`: `SourcePathSelection`, the driver, the pipeline and
   the diagnostics SHALL be written against Claimed_Extension and Translated_Unit, never against `.razor`.
4. WHEN a project holds no `.mist` file but holds at least one Claimed_Input, THE CLI SHALL compile it;
   the "no source files" refusal SHALL fire only when neither kind is present.
5. IF a project under a Backend that claims no extensions holds files that another Backend would claim, THEN
   THE Compiler_Driver SHALL ignore them as it does today, since nothing claims them; no diagnostic is owed.
6. THE registry SHALL hold exactly the `csharp` Backend, and the unknown-backend diagnostic SHALL be decided
   by the registry rather than by a comparison against `BackendId.CSharp`.

### Requirement 2: The translation stage

**User Story:** As a compiler maintainer, I want translation to run at one defined point in the pipeline with
the facts it needs, so that a translator can consult parsed Mist declarations and ambient types without the
pipeline knowing what it does with them.

#### Acceptance Criteria

1. THE Translation_Stage SHALL run after Stage 3 has parsed every `.mist` unit and before Stage 4 builds the
   program model, and SHALL run every Source_Translator of the selected Backend once.
2. THE Compiler_Driver SHALL give each Source_Translator: its Claimed_Inputs as `SourceText`s; the parsed
   `.mist` `CompilationUnit`s of the root and referenced Mist projects, read-only; the compile's
   `IAmbientTypeSource`; the lexer and parser, so that a translator may parse a Mist fragment; the project
   facts a `.mist` unit receives (namespace root, project folder, global usings); and a `DiagnosticBag`.
3. THE Compiler_Driver SHALL create the compile's `IAmbientTypeSource` before the Translation_Stage and
   SHALL hand the same instance to Stage 4, which today creates it.
4. WHEN a Source_Translator returns Translated_Units, THE Compiler_Driver SHALL lex and parse them as Stages
   2 and 3 do for a `.mist` unit and SHALL add them to the compilation, grouped with the project that held
   the Claimed_Input, before Stage 4 runs.
5. IF a Source_Translator reports an error, THEN THE pipeline SHALL stop at the same gate a parse error
   stops at, with every translator diagnostic reported.
6. THE Compiler_Driver SHALL write each Translated_Unit's text under the output directory, at
   `translated/<path of the Claimed_Input relative to its project>.mist`, and the Translated_Unit's
   `SourceText.Path` SHALL be that file, so that a generated location names a file a developer can open.
7. THE progress reporter SHALL report the Translation_Stage as a stage of its own, with the count of
   Claimed_Inputs and of Translated_Units.

### Requirement 3: Origin mapping of diagnostics

**User Story:** As a Mist developer, I want an error in my component reported at the `.razor` line I wrote,
so that generated Mist is an implementation detail I never have to read.

#### Acceptance Criteria

1. A Translated_Unit SHALL carry an Origin_Map whose segments cover its whole text; each segment SHALL name
   either an original `SourceText` and offset, for text copied verbatim, or an original `SourceText` and the
   span of the construct it was synthesized for, for text the translator wrote.
2. WHEN a diagnostic's location falls in a Translated_Unit, THE origin resolver SHALL produce a
   `GeneratedLocation` whose `GeneratedPath` and `Native` name the translated file and whose `Mapped` is the
   `SourceRange` in the original file that the Origin_Map gives for the diagnostic's start offset.
3. THE text formatter SHALL render a `GeneratedLocation` that has a `Mapped` range as the original path,
   line and column, in the form it uses for a `SourceRange`, and SHALL render one without a `Mapped` range
   as today.
4. THE fault excerpt beside a diagnostic SHALL come from the original file, which `DiagnosticHelper` already
   does for a mapped `GeneratedLocation`.
5. THE JSON document SHALL carry both the generated and the mapped positions for such a diagnostic.
6. WHEN verbose output is requested, THE CLI SHALL append the translated file and line after the mapped
   location, so that a translator defect can be traced.
7. THE Origin_Map SHALL be a type under `Source/Mist/Common/Source/` that knows nothing of Razor; the
   translator fills it, the core reads it.

### Requirement 4: The component file contract

**User Story:** As a Mist developer, I want a `.razor` file in my Mist project to be an ordinary Razor
component whose code is Mist, so that I write Blazor the way its documentation shows and change only the
language of the code.

#### Acceptance Criteria

1. THE C# Backend SHALL claim `.razor`; a Component_File is a Claimed_Input by being present in a project
   compiled by the C# Backend, with no manifest field and no directive to opt in.
2. THE embedded language of every Code_Region SHALL be Mist: expressions, control-flow headers, statement
   blocks, attribute value expressions, directive arguments and Code_Blocks are parsed by the Mist parser
   and judged by the Mist analyzers, and no Code_Region is ever compiled as C#.
3. EACH Component_File other than an Imports_File SHALL translate to exactly one Component_Class whose name
   is the file name without its extension, which derives from `Microsoft.AspNetCore.Components.ComponentBase`
   unless `@inherits` names another base, and which is `public`.
4. THE Component_Class SHALL take its namespace by the same rule a `.mist` file without a namespace
   declaration takes it under `inferred-namespaces`: the project's namespace root followed by the folder
   segments of the Component_File, so that `Components/Pages/Counter.razor` in a project rooted at `App`
   declares `App.Components.Pages.Counter`; `@namespace` overrides it.
5. THE Imports_Files on the path from the project folder to a Component_File's folder SHALL contribute
   their directives to that Component_File, outermost first, before the file's own directives, for
   `@using`, `@inherits`, `@layout`, `@implements`, `@inject`, `@attribute` and `@rendermode`.
6. THE members of every Code_Block SHALL be copied into the Component_Class verbatim, in order, with
   their original spans mapped; two Code_Blocks in one file concatenate.
7. THE Render_Method SHALL be declared `protected void BuildRenderTree(RenderTreeBuilder __builder)`,
   which fills the virtual slot of `ComponentBase` under `clr-slot-filling`; the parameter name and every
   other synthesized identifier SHALL begin with two underscores, the prefix the Razor compiler reserves
   for generated identifiers, and this contract reserves it likewise: IF a Code_Block declares an
   identifier beginning with two underscores, THEN THE Source_Translator SHALL report it as reserved.
8. A Component_File SHALL declare no namespace, class or member outside its Code_Blocks and directives;
   IF the translator finds a Code_Block whose members include a type declaration or a `namespace`, THEN it
   SHALL report that nested types are declared in a `.mist` file.
9. IF the markup is malformed, with an unclosed element, a mismatched end tag, an unterminated Code_Region
   or a transition the grammar does not admit, THEN THE Source_Translator SHALL report the error at the
   original position and SHALL produce no Translated_Unit for that file.

### Requirement 5: Markup lowering

**User Story:** As a Mist developer, I want my markup to render exactly as the same markup renders in a C#
component, so that Blazor's diffing, event wiring and prerendering behave identically.

#### Acceptance Criteria

1. THE Render_Method body SHALL follow the Render_Protocol: `OpenElement`, `AddAttribute`, `AddContent`,
   `AddMarkupContent`, `CloseElement`, `OpenComponent<T>`, `AddComponentParameter`, `CloseComponent`,
   `SetKey`, `AddMultipleAttributes`, `AddElementReferenceCapture`, `AddComponentReferenceCapture` and
   `AddComponentRenderMode`, with sequence numbers that are compile-time constants increasing in source
   order, one per call, as the Razor compiler assigns them.
2. A run of markup with no Code_Region and no component tag SHALL lower to one `AddMarkupContent` call
   holding the text verbatim; an element containing a Code_Region or a component SHALL lower to
   `OpenElement`, one `AddAttribute` per attribute and `CloseElement`, with its children in between.
3. An attribute whose value holds no Code_Region SHALL lower to `AddAttribute(seq, name, "literal")`; one
   whose value is a single Code_Region SHALL pass the expression; one that mixes literal text and
   Code_Regions SHALL pass their concatenation as a Mist string expression.
4. THE implicit expression `@name`, followed by any number of `.member`, `(...)` and `[...]` with balanced
   brackets, SHALL lower to `AddContent(seq, <expression>)`; THE explicit expression `@( ... )` SHALL lower
   likewise with the parenthesized text; `@@` SHALL be the literal `@`.
5. A control-flow block SHALL be any Mist statement that opens a block: `if` with its `else if` and `else`
   arms, `while`, `for`, `foreach`, `readonly`, the `with` statement forms and a bare `@{ ... }` block;
   markup inside the block SHALL lower to Render_Protocol calls inside the Mist statement; Mist has no
   `switch`, `do`, `try`, `using` or `lock` statement and the translator SHALL recognize none.
6. Inside a block, a line beginning `@:` SHALL be text, and a `<text>...</text>` element SHALL be markup
   with the `text` tags dropped, as in Razor.
7. A Razor comment `@* ... *@` SHALL produce nothing; an HTML comment SHALL be markup.
8. Whitespace-only text at the start and end of a block body and between the children of the file's root
   SHALL be dropped as the Razor compiler drops it by default; `@preservewhitespace` is later work.
9. A DOM event attribute `@on<event>="<handler>"` SHALL lower to
   `AddAttribute(seq, "on<event>", EventCallback.Factory.Create<TArgs>(this, <handler>))` where `TArgs` is
   the event-argument type the Component_Catalog records for `<event>` and `<handler>` is the Code_Region
   verbatim, a method group or a lambda; `@on<event>:preventDefault` and `@on<event>:stopPropagation` SHALL
   lower to `AddEventPreventDefaultAttribute` and `AddEventStopPropagationAttribute`.
10. `@key="<expr>"` SHALL lower to `SetKey(<expr>)` after the element or component is opened; `@ref="<field>"`
    on an element SHALL lower to `AddElementReferenceCapture(seq, (ElementReference __r) => { <field> = __r; })`
    and on a component to `AddComponentReferenceCapture(seq, (object __r) => { <field> = (T)__r; })` where
    `T` is the component type; `@attributes="<expr>"` SHALL lower to `AddMultipleAttributes(seq, <expr>)`.
11. `@bind="<field>"` on an element SHALL lower to the `value` attribute through `BindConverter.FormatValue`
    and the `onchange` handler through `EventCallback.Factory.CreateBinder(this, (T __v) => { <field> = __v; }, <field>)`
    where `T` is the field's declared type; `@bind:event="<name>"` SHALL replace `onchange`;
    `@bind:format` and `@bind:culture` SHALL pass through to `FormatValue` and `CreateBinder`.

### Requirement 6: Components, parameters and templates

**User Story:** As a Mist developer, I want to compose components with parameters, child content and named
templates the way Blazor's documentation shows, so that `Routes.razor`, layouts and my own components work
without a Razor-specific dialect.

#### Acceptance Criteria

1. A tag SHALL be a component tag when its name resolves in the Component_Catalog to a component type in
   scope, and an element otherwise, which is the Razor compiler's rule; a tag whose name begins with an
   uppercase letter and resolves to nothing SHALL be reported as an unknown component rather than lowered
   as an element.
2. A component tag SHALL lower to `OpenComponent<T>(seq)`, one `AddComponentParameter(seq, "Name", value)`
   per attribute, child content and template, and `CloseComponent()`.
3. AN attribute of a component tag whose value holds no Code_Region SHALL be a string literal when the
   Component_Catalog records the parameter's type as `string` or `string?`, and SHALL be a Mist expression
   otherwise, so that `Count="count"` passes the field and `Title="Counter says"` passes the text, exactly
   as Razor decides; an attribute value with a Code_Region SHALL be that expression.
4. IF an attribute names no parameter of the component, THEN THE Source_Translator SHALL pass it through
   `AddComponentParameter` as Razor does, leaving the runtime's unmatched-parameter behavior unchanged.
5. THE child content of a component tag SHALL lower to a `RenderFragment` local declared from a lambda
   `(RenderTreeBuilder __b) => { ... }` and passed as the `ChildContent` parameter; a child tag whose name
   the Component_Catalog records as a `RenderFragment` or `RenderFragment<TValue>` parameter of the parent
   SHALL lower to that parameter, with a `RenderFragment<TValue>` written as
   `(TValue context) => (RenderTreeBuilder __b) => { ... }` and the lambda parameter named by the child
   tag's `Context` attribute, defaulting to `context`.
6. `@rendermode <mode>` on a component tag SHALL lower to `AddComponentRenderMode(<mode>)`.
7. `@bind-<Param>="<field>"` on a component tag SHALL lower to the `<Param>` parameter and a
   `<Param>Changed` parameter of `EventCallback.Factory.Create<T>(this, (T __v) => { <field> = __v; })`
   where `T` is the parameter's type from the Component_Catalog; IF the component also declares a
   `<Param>Expression` parameter, THEN THE Source_Translator SHALL report that expression-tree parameters
   are later work, since Mist has no expression trees.
8. A component whose Component_Catalog entry declares type parameters SHALL be reported as later work when
   used from markup; generic components are Requirement 14.

### Requirement 7: The component catalog

**User Story:** As a compiler maintainer, I want the translator to know which names are components and what
their parameters are, from the same two sources Razor's compiler uses, so that attribute typing and template
detection are Razor-faithful without the program model existing yet.

#### Acceptance Criteria

1. THE Component_Catalog SHALL resolve a tag name against the namespaces in scope for the Component_File:
   its `@using` directives, those of its Imports_Files, the project's global usings and the file's own
   inferred namespace; a name SHALL resolve to at most one component type, and two candidates SHALL be
   reported as ambiguous at the tag.
2. FOR a CLR type, THE Component_Catalog SHALL ask the `IAmbientTypeSource` for each candidate full name
   and SHALL read from the returned type whether it implements `IComponent`, its properties carrying
   `[Parameter]` or `[CascadingParameter]` with their types, and its generic arity.
3. FOR a Mist type, THE Component_Catalog SHALL read the parsed `.mist` units and the Code_Blocks of every
   Component_File: a class whose base list names a component type, transitively, is a component, every
   Component_Class is a component, and a property carrying `[Parameter]` or `[CascadingParameter]` is a
   parameter whose type is the declared type's text.
4. THE Component_Catalog SHALL record, from the `[EventHandler]` attributes of
   `Microsoft.AspNetCore.Components.Web.EventHandlers` read through the `IAmbientTypeSource`, the
   event-argument type of each DOM event name, and SHALL fall back to `EventArgs` for a name it does not
   find, as Razor does.
5. THE Component_Catalog SHALL be built once per compile, before any Component_File is translated, and
   SHALL parse a Code_Block with the Mist parser as a class body rather than by any text heuristic.
6. THE Component_Catalog SHALL be a pure function of its inputs so that Tier 1 tests can build one from
   hand-written facts.

### Requirement 8: Directives

**User Story:** As a Mist developer, I want the Blazor directives I use every day to work, so that routing,
layouts, injection and render modes are declared where Blazor's documentation puts them.

#### Acceptance Criteria

1. `@page "<route>"` SHALL lower to `[Route("<route>")]` on the Component_Class; a file may carry several.
2. `@layout <Type>` SHALL lower to `[Layout(<Type>)]`, which Mist emits as `typeof`.
3. `@inherits <Type>` SHALL replace the base type; `@implements <Type>` SHALL add `<Type>` to the base list
   as a conformance, which Mist judges as it judges any abstract type.
4. `@inject <Type> <Name>` SHALL lower to a private readonly field `<Name>` of `<Type>` and to a parameter
   of one synthesized public constructor that assigns every injected field, in directive order; Blazor's
   activator resolves constructor parameters from the service container (Requirement 10.2). WHEN a file
   has an `@inject` and its Code_Block also declares a constructor, THE translator SHALL report *Constructor
   beside inject* at the constructor, since the services then belong among its parameters.
5. `@using <Namespace>` SHALL lower to a `using` directive of the Translated_Unit; `@using static` and
   `@using <alias> = <type>` SHALL lower to their Mist forms where Mist has them and be reported otherwise.
6. `@namespace <Namespace>` SHALL replace the inferred namespace.
7. `@attribute [<Attribute>]` SHALL place the attribute on the Component_Class.
8. `@rendermode <mode>` at file level SHALL lower to an attribute on the Component_Class that derives
   `RenderModeAttribute` and fills its `Mode` slot with `<mode>`, which the Razor compiler does through a
   nested private attribute class and which depends on `clr-slot-filling` for the property slot.
9. `@code` SHALL be the only member-declaring directive; `@functions` SHALL be reported as not supported.
10. `@typeparam` and `@preservewhitespace` SHALL be reported as later work (Requirement 14); `@model`,
    `@section`, `@addTagHelper` and every other MVC-only directive SHALL be reported as not valid in a
    component.
11. A directive the translator does not know SHALL be reported as unknown at its line.

### Requirement 9: Lifecycle members and the slot-filling dependency

**User Story:** As a Mist developer, I want to write `OnInitialized`, `OnParametersSet` and their async forms
in my `@code` block, so that lifecycle hooks are Mist methods and not a C# code-behind.

#### Acceptance Criteria

1. A Code_Block member whose name and signature match a virtual member of the component's base type SHALL
   fill that slot and be emitted with `override`, which is `clr-slot-filling`'s rule; this feature adds no
   rule of its own and SHALL NOT ship before that one.
2. AN async lifecycle hook SHALL be written with Mist's `async` convention, `protected async void
   OnInitializedAsync()`, which the emitter already lowers to `async Task`, and SHALL fill the
   `Task`-returning slot.
3. THE Render_Method the translator synthesizes SHALL fill `BuildRenderTree` by the same rule, so that a
   Component_Class renders only once `clr-slot-filling` has landed; until then a translated component
   compiles and renders nothing, and the Tier 5 test of Requirement 13 SHALL be what proves the dependency
   met.

### Requirement 10: Host-assigned members

**User Story:** As a Mist developer, I want injected services and parameters declared without inventing
initial values the runtime overwrites, and without weakening Mist's rule that no instance holds a value its
type says cannot exist.

#### Acceptance Criteria

1. THE defaultability analyzer refuses a property whose type has no default and no initializer when the
   class declares no constructor: measured on 2026-10-30 at `main` for `[Inject] NavigationManager Nav`
   and `[Inject] IJSRuntime JS`, each reported as having no default. A `string` or `int` parameter is
   admitted because its type has a default.
2. Decided by the owner on 2026-10-30: this spec changes no language rule. An injected service SHALL arrive
   through the constructor (Requirement 8.4), where defaultability Requirement 10 already guarantees the
   field. Measured on 2026-10-30: a `net10.0` `HtmlRenderer` renders a component whose only constructor
   takes a service, so Blazor's activator resolves constructor parameters from the container, and the
   compiler at `main` admits a `ComponentBase` subclass with such a constructor and `readonly` service
   fields with no diagnostic.
3. A Host_Assigned_Member whose type has no default SHALL be declared optional, `RenderFragment? ChildContent`,
   and narrowed at each read. That is the member's true contract: a parent may omit a parameter and a
   cascading value may be absent, and the runtime then leaves the member untouched. `[EditorRequired]` is
   an analyzer hint, not a runtime guarantee, and SHALL NOT change the declared type.
4. THE translator SHALL NOT lower anything to an `[Inject]` property. A Code_Block that declares one is
   judged by defaultability as any Mist class is.
5. A storage facility for members a host assigns after construction was set aside, not rejected: the
   assignment and the reads happen in different host calls, so definite assignment of such a field becomes
   state tracked across lifecycle members, and the host's call order stays a declared trust. Its shape is
   recorded under Alternatives Considered in the design and on Carbonek MIST-1126 with `clr-slot-filling`,
   since state on lifecycle members would attach to the slot kinds that spec defines. Nothing in this feature needs it.

### Requirement 11: Hosting and static assets

**User Story:** As a Mist developer, I want `mist run` to start my Blazor application and the browser to
load it, so that the host, too, is Mist and nothing in the project is a C# project file.

#### Acceptance Criteria

1. THE host program SHALL be an ordinary Mist `Main` using the ASP.NET Core builder; this compiles today and
   this spec adds no requirement to the frontend for it.
2. WHEN an executable project has Component_Files and the `Microsoft.AspNetCore.App` framework reference,
   THE C# Backend SHALL resolve the `Microsoft.AspNetCore.App.Internal.Assets` pack without a `[packages]`
   entry, at the version of the ASP.NET Core targeting pack the framework reference resolved to, and SHALL
   deploy its `_framework/` folder to `wwwroot/_framework/` beside the primary output, by the deployment
   machinery `native-runtime-assets` added, so that `UseStaticFiles` serves the Framework_Script. This is
   the SDK's own rule: `ProcessFrameworkReferences` turns the same framework reference into that pack
   through `KnownAspNetCorePack`, and the pack's targets add `blazor.web.js` only for an `Exe`. A
   `[packages]` entry naming the pack SHALL pin its version in place of the inferred one.
3. THE C# Backend SHALL deploy the project's own `wwwroot/` folder beside the primary output.
4. `mist run` SHALL start the program with a content root in which `wwwroot/` is the deployed folder, so
   that the default `WebApplication` web root finds it.
5. THE static-asset features of the Web SDK are out of scope: fingerprinted asset paths (`@Assets[...]`),
   import maps, `MapStaticAssets`, CSS isolation bundles (`.razor.css`) and collocated scripts (`.razor.js`)
   are not produced, and a Component_File that references them is an ordinary unresolved-name error.

### Requirement 12: Diagnostics

**User Story:** As a Mist developer, I want every error in a component to be a Mist diagnostic at a `.razor`
position, so that nothing about Razor's C# compiler reaches me.

#### Acceptance Criteria

1. THE translator's own diagnostics SHALL be descriptors in the registry with Numbers minted at
   implementation under `diagnostics` Requirement 1.4; this spec names them by Title: *Unclosed element*,
   *Mismatched end tag*, *Unterminated code region*, *Invalid transition*, *Unknown directive*,
   *Directive not valid in a component*, *Unknown component*, *Ambiguous component name*, *Nested type
   in code block*, *Reserved identifier*, *Expression parameter not supported*, *Generic component not
   supported*, *Functions directive not supported*, *Constructor beside inject*.
2. EVERY diagnostic a Translated_Unit produces in Stages 2 to 6, including a Roslyn error the backend
   admits, SHALL carry a mapped location in the Component_File.
3. A valid Component_File SHALL produce no Roslyn error; IF one appears, THEN it is a translator or emitter
   defect to fix at its tier, never a Component_File error to document.
4. THE translator SHALL report every error in a Component_File before stopping, not the first alone.

### Requirement 13: Verification

**User Story:** As a compiler maintainer, I want each part proven at its own tier, so that the system test
is one sample and the rules are proven beside the code that decides them.

#### Acceptance Criteria

1. THE Razor markup parser SHALL have Tier 1 tests over text: elements, attributes with and without
   Code_Regions, implicit and explicit expressions with balanced brackets, each control-flow block form,
   `@:`, `<text>`, comments, `@@`, components with child content and templates, each directive, and each
   malformed shape of Requirement 4.9 with its reported position.
2. THE Origin_Map SHALL have Tier 1 tests over hand-built segments: a verbatim offset, a synthesized span,
   a boundary between segments, and an offset past the end.
3. THE Component_Catalog SHALL have Tier 1 tests over hand-written facts for Requirement 7.1 and 7.3 and
   Tier 2 tests over Code_Blocks parsed by the real parser.
4. THE translator SHALL have Tier 2 tests that take a parsed Component_File and a hand-built
   Component_Catalog and assert the Mist text and Origin_Map for each lowering of Requirements 5, 6 and 8,
   with the fixture's `Counter.razor` and `Greeting.razor` among them.
5. THE driver seam of Requirements 1 and 2 SHALL have one Tier 4 test with a stub Source_Translator claiming
   an invented extension, proving discovery, ordering, grouping and mapped diagnostics with no Razor
   involved.
6. THE fixture under `specs/razor-components/fixture/` SHALL be banked as a sample under `samples/` when
   the feature lands, rendering its components to HTML through `HtmlRenderer` in a console program so that
   `AllFeaturesV2` holds its output, and the sample's designation SHALL be added to this document then,
   under `samples/AGENTS.md`.
7. INTERACTIVE SERVER acceptance, a browser establishing the circuit and a click changing state through a
   Mist handler without a reload, SHALL be proven once by hand with the spike's verifier,
   `probes/runtime/verify_browser.py` on Virtue, against the fixture's host, and recorded as a finding;
   no test below Tier 5 starts a server or a browser.

### Requirement 14: Later work and non-goals

1. Later phases, each its own task: generic components (`@typeparam` and type-argument attributes), which
   need the Component_Catalog to decide type arguments from parameter values as Razor does;
   `@bind-<Param>` where the component declares a `<Param>Expression`, which needs an answer for
   expression trees; code-behind as a sibling `<Name>.razor.mist` whose members the translator folds into
   the Component_Class, which needs no `partial`; `@preservewhitespace`; cascading values; sections.
2. Non-goals: IDE classification, completion and hot reload for Component_Files; CSS isolation and
   collocated JavaScript; Web SDK static-asset fingerprinting and import maps; Razor Pages, MVC views and
   `.cshtml`; Blazor WebAssembly and the wasm SDK; running the Razor source generator, the Web SDK or
   MSBuild inside `mist build`; a `partial` modifier; any Razor syntax in a `.mist` file.
