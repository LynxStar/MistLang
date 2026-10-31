# Design Document

## Overview

Three changes, in the order the pipeline meets them. The core gains one generic seam: a backend may claim
file extensions beyond `.mist`, and a translation stage after parsing turns each claimed file into Mist
source carrying a map back to the original text. The diagnostics layer learns to render a mapped generated
location, which it already models. Everything that knows Razor lives under
`Source/Mist/Backends/CSharpTranspiler/Razor/`: a markup parser, a component catalog and a translator that
writes a Mist class per component.

The translator writes the render method Razor's own compiler would write, in Mist, against the same
`RenderTreeBuilder` protocol, and copies every `@code` member and every `@` expression as written. The
Mist parser, program model and analyzers then see an ordinary class. Probes on 2026-10-30 against a
compiler built from `main` compiled every shape the translator emits, `[Route]`, `[Layout(MainLayout)]`,
`EventCallback.Factory.Create<MouseEventArgs>(this, Increment)`, a `RenderFragment` declared from a lambda
and one cast from a lambda, an `async void` hook lowered to `async Task`, and an `HtmlRenderer` console
harness, with zero warnings and Roslyn-identical C#. The one thing those probes could not do is render: the
synthesized `BuildRenderTree` is emitted without `override`, which is the `clr-slot-filling` gap. That spec
is a prerequisite, not a part of this one.

## Architecture

```
mist.toml ─► ProjectLayout.DiscoverSources(folder, backend.ClaimedExtensions)
                 │  *.mist ─────────────────────────────┐
                 │  *.razor (claimed) ──────────┐       │
                 ▼                              ▼       ▼
CompilerDriver.Compile                  ClaimedInput[]  CompilationUnit[]
   Stage 1  load                                │       │
   Stage 1.5 packages ─► IAmbientTypeSource ────┤       │
   Stage 2/3 lex + parse .mist units ───────────┼───────┘
   Stage 3.5 TRANSLATION  ◄── TranslationContext(inputs, parsed units, ambient, parser, project, bag)
                 │   for each ISourceTranslator of BackendRegistry.Resolve(options.Backend)
                 │       RazorComponentTranslator: RazorParser ─► ComponentCatalog ─► MistWriter + OriginMap
                 ▼
          TranslatedUnit[] ─► written to out/translated/… ─► lex + parse ─► CompilationUnit[] (joined)
                 │
   Stage 4/5/6 unchanged ─► emitted C# ─► Roslyn ─► bin/
                 │
   deployment: project wwwroot/ and package _framework/ ─► bin/wwwroot/
```

A diagnostic anywhere in Stages 2 to 6 whose node came from a translated unit resolves to
`GeneratedLocation(out/translated/…, native lines, Mapped: SourceRange in the .razor)`, and the formatter
prints the mapped position.

## Components and Interfaces

### New

Under `Source/Mist/Backends/Contracts/`:

- `IBackend`: `BackendId Id`, `ImmutableArray<ISourceTranslator> SourceTranslators`, and
  `ImmutableArray<string> ClaimedExtensions` derived from them.
- `ISourceTranslator`: `ImmutableArray<string> Extensions`, `ImmutableArray<TranslatedUnit> Translate(TranslationContext context)`.
- `TranslationContext`: the claimed inputs for this translator's extensions, the parsed `.mist` units,
  the `IAmbientTypeSource`, the `ILexer` and `IParser`, the project facts, the `DiagnosticBag`.
- `ClaimedInput(SourceText Text, MistProject Project)` and `TranslatedUnit(SourceText Text, MistProject Project)`.

Under `Source/Mist/Common/Source/`:

- `OriginMap`: ordered `OriginSegment`s covering a generated text; `SourceRange Map(int generatedOffset, int length)`.
- `OriginSegment(int GeneratedStart, int Length, SourceText Original, int OriginalStart, int OriginalLength, bool Verbatim)`.

Under `Source/Mist/Driver/`:

- `BackendRegistry`: `IBackend? Resolve(BackendId id)`; holds the `csharp` backend.
- `TranslationStage`: runs the translators, writes the generated files, lexes and parses the results.

Under `Source/Mist/Backends/CSharpTranspiler/`:

- `CSharpBackend : IBackend` with one translator.
- `Razor/RazorParser`: text to `RazorSyntaxTree`; `Razor/Syntax/*`: the markup node kinds.
- `Razor/ComponentCatalog` and `Razor/CatalogBuilder`.
- `Razor/RazorComponentTranslator : ISourceTranslator`, `Razor/MistWriter` (text plus origin segments),
  `Razor/RenderProtocolWriter`, `Razor/DirectiveLowering`, `Razor/ImportsResolution`.
- `Razor/RazorDiagnostics`: the descriptors of Requirement 12.1.
- `StaticAssetDeployment`: the `wwwroot/` and `_framework/` rules of Requirement 11.

### Modified

- `SourcePathSelection.EnumerateCandidates(folder, claimedExtensions)`: enumerates `*.mist` and each
  `*.<ext>`; `Select` is unchanged.
- `ProjectLayout.DiscoverSources` and `ProjectReferenceLoader`: pass the backend's extensions; the CLI
  resolves the backend before discovery; the "no source files" check counts both kinds.
- `CompilerDriver`: the unknown-backend check uses the registry; Step 1b separates `.mist` paths from
  claimed inputs; the ambient source is created after Stage 1.5 and placed on `PipelineInput`; the
  translation stage runs after Stage 3; translated units join the compilation before Stage 4.
- `StandardPipeline`: Stage 4 uses the `IAmbientTypeSource` on `PipelineInput` when present.
- `SourceText`: gains `OriginMap? Origin { get; init; }`.
- The origin resolver that turns a `ParsedOrigin` node into a `Location`: when the node's `SourceText`
  has an `Origin`, it returns `GeneratedLocation(text.Path, nativeLines, Mapped: text.Origin.Map(...))`.
- `DiagnosticFormatter`: a `GeneratedLocation` with `Mapped` prints the mapped path, line and column.
- `DiagnosticJsonWriter.Locations`: both positions.
- `RuntimeAssetDeployment` or a sibling: the static-asset rules.
- `ICompilationProgressReporter` callers: the new stage.

## Data Models

```csharp
public sealed record TranslationContext(
    ImmutableArray<ClaimedInput> Inputs,
    ImmutableArray<CompilationUnit> ParsedUnits,
    IAmbientTypeSource AmbientTypes,
    ILexer Lexer,
    IParser Parser,
    ProjectFacts Project,
    DiagnosticBag Diagnostics);

public sealed record ProjectFacts(string NamespaceRoot, string ProjectFolder, ImmutableArray<string> GlobalUsings);

public sealed class OriginMap
{
    public ImmutableArray<OriginSegment> Segments { get; }
    public SourceRange Map(int generatedOffset, int length);
}
```

The component catalog, built once per compile:

```csharp
internal sealed record ComponentFacts(
    string FullName,                      // App.Components.Greeting
    ImmutableArray<ParameterFacts> Parameters,
    int TypeParameterCount,
    bool IsMist);                         // from a .mist unit or a .razor file, not the ambient source

internal sealed record ParameterFacts(
    string Name,
    string TypeText,                      // declared text for Mist, CLR full name for ambient
    ParameterShape Shape);                // Plain, String, RenderFragment, TemplatedFragment, EventCallback

internal sealed record EventFacts(string EventName, string ArgumentTypeFullName);
```

## Detailed Design

### 1. Discovery and the backend seam

`SourcePathSelection.EnumerateCandidates` takes the claimed extensions and enumerates one `*.<ext>` pass
per extension beside `*.mist`, then `Select` applies the same generated-folder and output-folder
exclusions. The CLI resolves `BackendRegistry.Resolve(cliArgs.BackendId)` before `DiscoverSources`; an
unknown id is the existing `UnknownBackend` diagnostic, now decided by a null from the registry. A
referenced Mist project's sources are discovered through `ProjectReferenceLoader` with the same
extensions, since Requirement 1.2 includes them.

`CompilerOptions.SourcePaths` carries every discovered path. Step 1b of loading reads each: a `.mist` path
becomes a `CompilationUnit` as today; any other path becomes a `ClaimedInput` keyed by extension, grouped
with its `MistProject`. Nothing in the driver inspects the extension beyond matching it to a translator.

### 2. The translation stage and the ambient source

The driver today lexes and parses in Stages 2 and 3 and hands `PipelineInput` to `StandardPipeline`, which
creates the ambient source inside Stage 4. The stage order becomes:

1. Stage 1.5 resolves packages and framework references to assembly paths.
2. The driver creates `IAmbientTypeSource` from those paths, once, and puts it on `PipelineInput`.
3. Stages 2 and 3 lex and parse the `.mist` units.
4. Stage 3.5 builds a `TranslationContext` per translator and collects `TranslatedUnit`s. Each is written
   to `<OutputDirectory>/translated/<relative>.mist`, its `SourceText.Path` is that file and its `Origin`
   is the map. The stage then lexes and parses them with the same lexer and parser and appends the units
   to the compilation under the owning project.
5. Stage 4 builds the model over all units, using the ambient source it was handed.

A translator error is reported into the shared bag and stops at the parse gate. Because the ambient source
is a `MetadataLoadContext` over the reference pack, creating it before Stage 2 moves cost earlier but adds
none; seam tests keep handing the driver a stub as they do today.

### 3. Parsing a component file

The parser recognizes Razor's markup and transition grammar and nothing of Mist: every Code_Region is a
leaf holding raw text and its span. It needs modes, markup and code, because what ends a region depends on
which mode it is in:

- **Transitions.** `@@` is a literal `@`. `@*` opens a comment to `*@`. `@(` opens an explicit expression
  to the balancing `)`. `@{` opens a statement block to the balancing `}`. `@:` makes the rest of the line
  text. `@` followed by a Mist block keyword, `if`, `while`, `for`, `foreach`, `readonly` or `with`, opens a
  control block: the header runs to the balancing `)` where the keyword takes one, then the block runs to the
  balancing `}`; after an `if` block, `else if` and `else` continue it. `@` followed by an identifier opens
  an implicit expression: the identifier, then any run of `.identifier`, `(...)` and `[...]` with balanced
  brackets; the region ends at the first character that is none of these, so `@count.` before a space is
  `count` and a literal `.`. `@` followed by a directive keyword at the start of a line is a directive.
- **Balancing** skips Mist string literals, character literals, interpolation holes and comments so that a
  brace inside a string does not close a block. That is the only Mist lexical knowledge the parser holds.
- **Code mode** is the inside of a statement or control block. A line whose first non-blank character
  opens an element switches to markup until that element closes; `@:` takes the line; `<text>` is markup
  with the tags dropped; everything else is Mist statement text.
- **Markup mode** is elements, attributes, text and components. An attribute value may hold transitions;
  `@on`, `@bind`, `@key`, `@ref`, `@attributes` and `@rendermode` attributes are directive attributes
  whose values are Code_Regions.

The output is a `RazorSyntaxTree`: directives, then a sequence of markup nodes, elements with attributes
and children, component tags, text, comments, Code_Region leaves of the kinds above, and the Code_Blocks.
Malformed input is reported with the descriptors of Requirement 12.1 at the original offset, and the file
produces no unit.

Storm with lexer modes (`storm-lexer-modes`) is the preferred way to write this grammar, as
`Razor/Grammar/RazorMarkup.storm`, so the parser is generated and its tests are grammar tests. If the mode
switches above exceed what Storm's modes express, a hand-written scanner in the same folder is the
fallback; either is an implementation choice inside the backend and changes nothing above.

### 4. The component catalog

`CatalogBuilder` runs once, before any file is translated:

1. **Mist components.** Walk the parsed `.mist` units for class declarations; walk every Component_File's
   Code_Blocks by parsing each as a class body with the real parser. Record each class's full name (by the
   inferred-namespace rule or its declaration), base type names, `[Parameter]` and `[CascadingParameter]`
   properties with their declared type text, and whether it is a Component_Class. A class is a component
   when it is a Component_Class or its base list names a component, resolved to a fixpoint.
2. **CLR components.** Resolution is lazy: for a tag name, build the candidate full names from the file's
   scope (its `@using`s, its Imports_Files', the global usings, its own namespace), ask
   `IAmbientTypeSource.FindClrType` for each, and keep the ones that implement `IComponent`. Read
   `[Parameter]` properties and their types and the generic arity from the returned `System.Type`.
3. **Events.** Read `[EventHandler]` attributes from `Microsoft.AspNetCore.Components.Web.EventHandlers`
   once; `onclick` maps to `MouseEventArgs`, an unknown name to `EventArgs`.

A `ParameterShape` is decided from the type: `string` or `string?` is `String`; `RenderFragment` is
`RenderFragment`; `RenderFragment<T>` is `TemplatedFragment`; `EventCallback` or `EventCallback<T>` is
`EventCallback`; anything else is `Plain`. The attribute-typing rule of Requirement 6.3 reads only the
shape, so a Mist alias of `string` is `Plain` and its attribute value is an expression; that is the one
divergence from Razor and is documented.

### 5. Translating a component file

For `fixture/Components/Counter.razor`, the translator writes, in order:

1. The `using` directives: the Imports_Files' `@using`s outermost first, then the file's own.
2. No `namespace`: the unit is placed by the inferred-namespace rule, so `Components/Counter.razor`
   lands in `RazorComponents.Components` like a `.mist` file beside it would. `@namespace` writes one.
3. Attributes from directives: `[Route("/counter")]`, `[Layout(MainLayout)]`, each `@attribute`, and the
   render-mode attribute of Requirement 8.8.
4. `public class Counter : ComponentBase` with the `@implements` types appended to the base list.
5. The Code_Block members verbatim, each a verbatim segment of the origin map.
6. The render method.

The render method for the fixture's `Counter.razor`:

```mist
protected void BuildRenderTree(RenderTreeBuilder __builder)
{
    __builder.OpenComponent<PageTitle>(0);
    __builder.AddComponentParameter(1, "ChildContent", (RenderFragment)((RenderTreeBuilder __b1) =>
    {
        __b1.AddContent(2, "Counter");
    }));
    __builder.CloseComponent();
    __builder.AddMarkupContent(3, "<h1>Counter</h1>\n\n");
    __builder.OpenElement(4, "p");
    __builder.AddAttribute(5, "role", "status");
    __builder.AddContent(6, "Current count: ");
    __builder.AddContent(7, count);
    __builder.CloseElement();
    __builder.OpenElement(8, "p");
    __builder.AddContent(9, "The count is a ");
    __builder.AddContent(10, int.Name);
    __builder.AddContent(11, ".");
    __builder.CloseElement();
    if (lastReset is void)
    {
        __builder.AddMarkupContent(12, "<p>Never reset.</p>");
    }
    else
    {
        __builder.OpenElement(13, "p");
        __builder.AddContent(14, "Last reset at count ");
        __builder.AddContent(15, lastReset);
        __builder.AddContent(16, ".");
        __builder.CloseElement();
    }
    __builder.OpenElement(17, "button");
    __builder.AddAttribute(18, "class", "btn btn-primary");
    __builder.AddAttribute(19, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, Increment));
    __builder.AddContent(20, "Click me");
    __builder.CloseElement();
    __builder.OpenElement(21, "button");
    __builder.AddAttribute(22, "class", "btn btn-secondary");
    __builder.AddAttribute(23, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, Reset));
    __builder.AddContent(24, "Reset");
    __builder.CloseElement();
    __builder.OpenComponent<Greeting>(25);
    __builder.AddComponentParameter(26, "Title", "Counter says");
    __builder.AddComponentParameter(27, "Count", count);
    __builder.AddComponentParameter(28, "ChildContent", (RenderFragment)((RenderTreeBuilder __b2) =>
    {
        __b2.AddMarkupContent(29, "<em>Nested from the counter.</em>");
    }));
    __builder.CloseComponent();
}
```

Every expression, `count`, `int.Name`, `lastReset is void`, `Increment`, is a verbatim segment pointing at
its `.razor` span; every builder call is a synthesized segment pointing at the element, attribute or tag it
lowers. `Title="Counter says"` is a literal because the catalog records `Title` as `String`;
`Count="count"` is an expression because `Count` is `Plain`. The fragment is written as a cast lambda, the
form Razor emits and one the probe compiled; the alternative, a typed local, also compiled and is the
fallback if a cast ever fails to target-type.

`Greeting.razor` translates the same way, its `@if (ChildContent is RenderFragment body)` becoming a Mist
`if` with the narrowed `body` passed to `AddContent(seq, body)`.

### 6. The render protocol

| Markup | Lowering |
| --- | --- |
| literal run with no transitions or components | `AddMarkupContent(seq, text)` |
| element containing a transition or component | `OpenElement(seq, name)` … `CloseElement()` |
| attribute, literal value | `AddAttribute(seq, name, "value")` |
| attribute, one expression | `AddAttribute(seq, name, expr)` |
| attribute, mixed | `AddAttribute(seq, name, "a " + expr + " b")` |
| text | `AddContent(seq, "text")` |
| `@expr`, `@(expr)` | `AddContent(seq, expr)` |
| `@on<e>="h"` | `AddAttribute(seq, "on<e>", EventCallback.Factory.Create<TArgs>(this, h))` |
| `@on<e>:preventDefault` | `AddEventPreventDefaultAttribute(seq, "on<e>", true)` |
| `@on<e>:stopPropagation` | `AddEventStopPropagationAttribute(seq, "on<e>", true)` |
| `@key="k"` | `SetKey(k)` |
| `@ref="f"` on element | `AddElementReferenceCapture(seq, (ElementReference __r) => { f = __r; })` |
| `@ref="f"` on component `T` | `AddComponentReferenceCapture(seq, (object __r) => { f = (T)__r; })` |
| `@attributes="d"` | `AddMultipleAttributes(seq, d)` |
| `@bind="f"` | `AddAttribute(seq, "value", BindConverter.FormatValue(f))`; `AddAttribute(seq, "onchange", EventCallback.Factory.CreateBinder(this, (T __v) => { f = __v; }, f))` |
| `<Comp A="x">` | `OpenComponent<Comp>(seq)`; `AddComponentParameter(seq, "A", x or "x")`; `CloseComponent()` |
| child content | `AddComponentParameter(seq, "ChildContent", (RenderFragment)((RenderTreeBuilder __b) => { … }))` |
| `<Tmpl Context="c">` under `Comp` | `AddComponentParameter(seq, "Tmpl", (RenderFragment<TValue>)((TValue c) => (RenderTreeBuilder __b) => { … }))` |
| `@rendermode m` on a tag | `AddComponentRenderMode(m)` |
| `@bind-P="f"` | `AddComponentParameter(seq, "P", f)`; `AddComponentParameter(seq, "PChanged", EventCallback.Factory.Create<T>(this, (T __v) => { f = __v; }))` |

Sequence numbers count every call in source order, including the calls inside fragments, which is how the
Razor compiler numbers them and what the differ relies on.

### 7. Directives

| Directive | Lowering | Note |
| --- | --- | --- |
| `@page "/r"` | `[Route("/r")]` | repeatable |
| `@layout T` | `[Layout(T)]` | Mist emits `typeof` |
| `@inherits T` | base type `T` | |
| `@implements T` | `T` appended to the base list | Mist conformance |
| `@inject T Name` | `private readonly T Name;` and a parameter `T Name` of the synthesized constructor | Blazor resolves constructor parameters from the container; a Code_Block constructor beside `@inject` is *Constructor beside inject* |
| `@using N` | `using N;` | |
| `@namespace N` | `namespace N;` | |
| `@attribute [A]` | `[A]` on the class | |
| `@rendermode M` | nested `RenderModeAttribute` subclass filling `Mode` with `M`, applied to the class | needs `clr-slot-filling` for the property slot |
| `@code { … }` | members verbatim | |
| `@typeparam`, `@preservewhitespace` | *later work* diagnostic | |
| `@functions`, MVC directives | *not supported* diagnostic | |

Imports_Files are resolved by walking from the project folder to the file's folder; their directives are
prepended in that order, and `@inherits`, `@layout` and `@rendermode` from an inner file override an outer
one, as Razor applies them.

A file with at least one `@inject` gets one synthesized public constructor: a parameter per injected member
in directive order, a body that assigns each field, and no base call, since `ComponentBase` constructs
without arguments. A file with none gets no constructor and takes the implicit one under defaultability
Requirement 9.8. The emitted C# for `@inject NavigationManager Nav` is therefore

```csharp
private readonly NavigationManager Nav;
public Counter(NavigationManager Nav) { this.Nav = Nav; }
```

which is the shape measured on 2026-10-30: Blazor's activator resolves the parameter from the container,
and Mist admits the class with no diagnostic because the constructor assigns the field.

### 8. Origin mapping and diagnostics

`MistWriter` appends text in two ways: `Verbatim(SourceText original, SourceSpan span)` copies the span and
records a verbatim segment; `Synthesized(string text, SourceText original, SourceSpan forConstruct)`
records a synthesized segment. The finished `OriginMap` covers the whole text. `Map(offset, length)` finds
the segment by binary search: a verbatim segment shifts the offset; a synthesized one returns the
construct's span. The resolver produces `GeneratedLocation(text.Path, native, Mapped)`; `DiagnosticHelper`
already excerpts from `Mapped`; the formatter gains one arm so that a mapped `GeneratedLocation` prints as
`Components/Counter.razor(31,13): error MISTnnnn: …`, and verbose output appends
`(translated: out/translated/Components/Counter.razor.mist(58,25))`.

### 9. Hosting and static assets

The host is the Mist program the spike and today's probe compiled: `WebApplication.CreateBuilder(args)`,
`AddRazorComponents().AddInteractiveServerComponents()`, `UseAntiforgery()`,
`MapRazorComponents<App>().AddInteractiveServerRenderMode()`, `Run()`. `App.razor` is a Component_File
whose markup is the HTML document with `<Routes />`, `<HeadOutlet />` and
`<script src="_framework/blazor.web.js"></script>`; `Routes.razor` is a Component_File using `Router`
with its `Found` and `NotFound` templates, which Requirement 6.5 lowers.

In the SDK the `Microsoft.AspNetCore.App` framework reference brings the `Microsoft.AspNetCore.App.Internal.Assets`
pack with it: `Microsoft.NETCoreSdk.BundledVersions.props` lists the pack as the `KnownAspNetCorePack` for
the target framework, `ProcessFrameworkReferences` adds it for a web executable, and the pack's own targets
register `blazor.web.js` and `blazor.server.js` as static web assets when `OutputType` is `Exe`. A Mist
project has no static-web-assets manifest, so the backend does both halves itself. `StaticAssetDeployment`
adds the pack to the restore set when the project is an executable with Component_Files and that framework
reference, at the resolved targeting pack's version, with a `[packages]` entry pinning it instead
(Requirement 11.2); the pack is on nuget.org and 2.4 MB on disk. It then copies the pack's `_framework/`
folder, which the package lays out at its root, to `<bin>/wwwroot/_framework/`, and the project's
`wwwroot/` to `<bin>/wwwroot/`. The host calls `UseStaticFiles()`, which serves the web root.
`mist run` started `dotnet <artifact>` through `ProcessStartInfo` with no working directory and no
program arguments, so the content root was whatever directory the user ran `mist run` from. The choice was
between setting the working directory to `<bin>`, which changes where every program's relative paths
resolve, and passing `--contentRoot <bin>` to programs that reference `Microsoft.AspNetCore.App`, which
`WebApplication.CreateBuilder(args)` honors and a console program never sees.

**Decision: `--contentRoot <bin>`** (`ArtifactLaunch.StartInfo` in `Mist.CLI`). When the root project's
`[framework_references]` enables `Microsoft.AspNetCore.App`, `mist run` starts `dotnet <artifact> --contentRoot
<bin>`; otherwise it starts `dotnet <artifact>` as before, and the working directory stays the user's in both
cases. The reasons: the content root is an ASP.NET Core setting, so it is set through ASP.NET Core's own
switch and reaches only programs that build a host; a console program's relative paths, which resolve
against the user's directory today, do not move; and `<bin>` is where the backend deploys `wwwroot/`, so
the default web root is the deployed folder. The cost is that a web program's `args` carries the two
words, which a host passes to `CreateBuilder` and a program that parses its own arguments must skip. The
switch is the root project's own reference, not one inherited through a referenced project. Section 9's
probe on 2026-10-30, `mist run` from the repository root on a host with `UseStaticFiles()`, logged
`Content root path: <project>/bin` and served `/_framework/blazor.web.js` and `/css/app.css` from
`<bin>/wwwroot/`.

Whether a project has Component_Files is decided by `StaticAssetDeployment.HasComponentFiles`, which
enumerates `*.razor` under the project folder with the `out/` and `bin/` exclusions of
`SourcePathSelection.Select`. It stands in for the backend's `ProjectFacts` until that lands and is the
one call to replace then. The driver adds the pack to the declared packages after the referenced projects'
dependencies are joined and before Stage 1.5, so the restore, the resolver and its verbose lines treat it
as any declared package; the resolver records each restored package's folder, and the backend reads the
pack's from there. A file under the project's `wwwroot/` wins over a pack file at the same deployed path.

## Error Handling

| Situation | Where | Diagnostic |
| --- | --- | --- |
| `<div>` never closed | parser | *Unclosed element* at the open tag |
| `</span>` closing a `<div>` | parser | *Mismatched end tag* at the end tag |
| `@(` or `@{` without its close | parser | *Unterminated code region* at the transition |
| `@foo` where `foo` is not a directive, on a directive line | parser | *Unknown directive* |
| `@model`, `@section` | parser | *Directive not valid in a component* |
| `<Widget>` resolving to nothing | translator | *Unknown component* at the tag |
| `<Widget>` resolving to two types | translator | *Ambiguous component name* listing both |
| `class` inside `@code` | translator | *Nested type in code block* |
| `__count` declared inside `@code` | translator | *Reserved identifier* at the declaration |
| `@bind-Value` on a component declaring `ValueExpression` | translator | *Expression parameter not supported* |
| `<Grid TItem="…">` | translator | *Generic component not supported* |
| `@functions` | parser | *Functions directive not supported* |
| a constructor in `@code` in a file that has an `@inject` | translator | *Constructor beside inject* at the constructor |
| `pure` misuse, unresolved name, type error inside a Code_Region | Mist stages 4 and 5 | the ordinary Mist diagnostic, mapped to the `.razor` position |
| `.razor` under a backend claiming nothing | nobody | ignored, as any non-`.mist` file is today |

## Correctness Properties

1. **The core names no extension.** No type outside `Backends/` mentions `.razor`; a grep is the test.
2. **Translation is a pure function of its inputs.** The same `RazorSyntaxTree`, catalog and project facts
   produce the same text and map.
3. **The origin map is total.** Every offset of a translated unit maps to a range in exactly one original
   file.
4. **Verbatim regions are byte-identical.** The text a Code_Region's segment covers in the translated unit
   equals the text of its span in the `.razor` file.
5. **Sequence numbers are strictly increasing in source order** across the render method, fragments
   included.
6. **A valid component reaches Roslyn clean.** Any Roslyn error from a translated unit is a translator or
   emitter defect.

## Testing Strategy

The binding rules are the root `AGENTS.md` "Tests" section and `.claude/skills/testing-philosophy/SKILL.md`.

- **Tier 1.** `RazorParserTests`: text in, tree out, every shape of Requirement 13.1, every malformed shape
  with its position. `OriginMapTests`: hand-built segments, Requirement 13.2. `ComponentCatalogTests`:
  resolution and shapes over hand-written facts. `SourcePathSelectionTests`: extensions enumerated and
  excluded like `.mist`. `RenderProtocolWriterTests`: sequence numbering over hand-built nodes. `StaticAssetDeploymentTests`: the
  pack-inference rule and the deployment selection over hand-built listings. `ArtifactLaunchTests`: the
  `mist run` start information.
- **Tier 2.** `RazorComponentTranslatorTests`: a parsed Component_File and a hand-built catalog produce the
  expected Mist and map, with the fixture files among the cases. `CatalogBuilderTests`: Code_Blocks parsed
  by the real parser yield parameter facts.
- **Tier 3.** `TranslatedUnitDiagnosticsTests`: a translated unit with a deliberate Mist error, run through
  the real parser and model, reports a `GeneratedLocation` whose `Mapped` is the `.razor` span, with the
  ambient source stubbed.
- **Tier 4.** `CompilerDriverClaimedInputTests`: a stub backend claiming `.probe` whose translator emits
  a fixed class; the driver discovers the file, runs the stage, and the class binds from a `.mist` unit.
  No Razor, no reference pack.
- **Tier 5.** The sample of Requirement 13.6 through `AllFeaturesV2` once banked, rendering the fixture's
  components to HTML with `HtmlRenderer`; its output is the baseline's record. Nothing else at Tier 5.
- **By hand, once.** Interactive Server acceptance with the spike's Playwright verifier against the
  fixture's host, recorded as a Carbonek finding.

## Alternatives Considered

- **Translating before Stage 1, from text alone.** Simplest ordering, but the component catalog needs the
  parsed `.mist` declarations and the ambient types to decide literal-versus-expression and
  template-versus-child, which is Razor's own semantic rule. Running after Stage 3 with the ambient source
  costs one reorder and keeps the rule faithful.
- **A syntactic rule instead of the catalog**: uppercase tags are components, unprefixed attribute values
  are strings, non-strings need `@`. A strict subset of Razor with identical meaning, and no type facts
  needed, but it rejects `Count="count"`, `OnClick="Handler"` and `<NotFound>` templates as written in
  every Blazor sample. Rejected; the catalog is cheap.
- **Lowering to standard Razor plus C# and running the Razor source generator.** The spike's first
  proposal. It puts Roslyn in charge of the component, needs MSBuild or a generator driver inside
  `mist build`, and leaves the ordering problem of Razor needing Mist declarations while Mist needs
  Razor's generated class. Emitting Mist removes all three.
- **Using the Razor compiler's own parser.** `Microsoft.CodeAnalysis.Razor.Compiler` exists on NuGet as one
  preview (`10.0.0-preview.25277.114`) while the SDK ships a newer build, `Microsoft.AspNetCore.Razor.Language`
  stopped at 6.0, and the compiler's parser ends implicit expressions by C# tokenization. The subset this
  feature needs is small enough to own, and owning it is what keeps the backend's Razor dialect explicit.
- **Teaching the core about Razor** (a `.razor` arm in discovery, markup nodes in the program model).
  Rejected by the owner on 2026-10-30: a backend mechanism, not a language one.
- **Code-behind by `partial`.** Mist has no `partial`; the translator already owns the class, so a sibling
  `<Name>.razor.mist` can be folded in later without one.
- **A host-assigned storage facility** for injected services and non-optional parameters (Requirement 10).
  A member marked as written by the host before any read would be exempt from the constructor-assignment
  rule, unreadable in initializers, default blocks and constructors, and emitted with a sentinel. Set aside
  on 2026-10-30. The assignment and the reads happen in different host calls, so definite assignment of such
  a field is not one body's flow but the object's state across lifecycle members: a field readable only in
  members the host calls after configuration, with the state of each CLR lifecycle slot declared once in
  the projection of the base type. Mist could check its own side of that completely; the host's call order
  would remain a declared trust at the CLR boundary, which the ambient provider accepts today only for
  nullable annotations on inbound results. Constructor injection removes the need for services, and a
  parameter the parent may omit is optional in fact. If a later framework offers no constructor path, the
  facility attaches to the slot kinds `clr-slot-filling` defines, and its notes live on that work's Carbonek
  record, MIST-1126.

## Open Items

- **Storm or scanner** for the markup grammar, decided when task 3 starts.
- **`mist run` content root**: decided in task 6, `--contentRoot <bin>` for a program referencing
  `Microsoft.AspNetCore.App`; section 9 records the reason.
- **Whitespace fidelity.** Requirement 5.8 states Razor's default; the exact rules Razor applies are read
  from its generated output for the fixture during task 5 and recorded as Tier 1 cases.
