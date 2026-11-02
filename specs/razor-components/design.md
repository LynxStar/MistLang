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
          TranslatedUnit[] ─► written to out/<project name>/translated/<relative>.mist ─► lex + parse ─► CompilationUnit[] (joined)
                 │
   Stage 4/5/6 unchanged ─► emitted C# ─► Roslyn ─► bin/
                 │
   deployment: project wwwroot/ and package _framework/ ─► bin/wwwroot/
```

A diagnostic anywhere in Stages 2 to 6 whose node came from a translated unit resolves to
`GeneratedLocation(out/<project name>/translated/<relative>.mist, native lines, Mapped: SourceRange in the .razor)`, and the formatter
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
- `RazorDiagnostics`: the descriptors of Requirement 12.1, in the registry folder
  `Source/Mist/Common/Diagnostics/Descriptors/` beside `BackendDiagnostics`, where the registry, the text
  freeze table and the catalog page read them. Their text names no file extension.
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
   to `<OutputDirectory>/<project name>/translated/<relative>.mist`, for the root and every referenced
   project alike, beside that project's emitted C#. The relative path is measured from the owning
   project folder. Its `SourceText.Path` is that file and its `Origin`
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

**Decided at task 3: a hand-written scanner, not a Storm grammar.** Storm with lexer modes
(`storm-lexer-modes`) was attempted first, as `Razor/Grammar/RazorMarkup.storm` beside `Mist.storm` in
`Mist.csproj`'s `AdditionalFiles`, and the build failed with `STORM0010`: the generator accepts one
`grammar` declaration per compilation and, finding two, emits nothing for either, so the Mist grammar
itself disappears. A second grammar would need its own project, which the core would then have to reference
for a backend's markup, or a change to the generator in the Storm submodule. Neither is worth it, because the
mode switches above also exceed what token actions express: a Storm mode changes only when a token matches,
while an implicit expression ends at the first character that cannot continue it without consuming that
character, a directive is recognized only at the start of a line, code mode switches to markup only where a
statement could begin, and an end tag must name the element it closes. The scanner is
`Razor/RazorParser`, one partial class split by mode: `RazorParser.cs` (entry and cursor),
`RazorParser.Markup.cs`, `RazorParser.Transitions.cs`, `RazorParser.Code.cs` and
`RazorParser.Directives.cs`; `Razor/MistBalancer` holds the Mist lexical knowledge; `Razor/Syntax/` holds
the tree. Its tests are Tier 1 over text, `Test/Mist.Tests/Backend/Razor/`.

The dialect the scanner fixes, beyond the transitions above:

- A **directive line** is a line of the file's root markup, not inside an element or block, whose first
  non-blank character is `@` followed by a name. A known directive name is a directive whatever follows it;
  an MVC directive or `@functions` is refused and its brace block, on that line or the next, is skipped. Any
  other lowercase name followed by blanks and an argument on the same line is *Unknown directive*; a line that
  is only `@name`, `@name.Member` or `@Name text` is an implicit expression. `@(name) text` writes an
  expression followed by text on such a line.
- In code mode, markup begins at the first non-blank character of a line or the first after `{`, `}` or `;`,
  when that character is `<` followed by a letter, `</`, `<!--` (an HTML comment), or `@:`; `<text>` is a
  `TextTag` only there, and an ordinary element in markup. `@*` is a comment in either mode.
- `@` between two letters or digits, as in an e-mail address, is literal text, as in Razor.
- The void HTML elements (`br`, `input`, `img` and the rest of the HTML list) take no end tag; `script` and
  `style` bodies are text with transitions and no tags. Both lists are lowercase names matched exactly, so
  `<Link>`, `<Meta>` or `<Style>` is a component with an end tag and markup children.
- A quoted attribute value ends at the first matching quote outside a Code_Region: an `@( … )` or implicit
  expression inside it is balanced first, so `title="@Format("x")"` is one value. An attribute name cannot
  begin with a quote or `=`; one that does leaves the start tag *Unclosed element*.
- A directive attribute's value is a Code_Region, quoted or not, and a leading `@` or `@( … )` inside the
  quotes is dropped; `@name:parameter` splits at the first colon.
- Recovery: an end tag naming an open ancestor reports *Unclosed element* for each element it skips; any other
  wrong end tag is *Mismatched end tag* and closes the current element; an unclosed `<!--` is *Unclosed
  element*; an unterminated explicit expression
  or control header resumes at the end of its line. Every error is reported before the tree is returned, and
  `RazorSyntaxTree.HasErrors` tells the translator to produce no unit.

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

1. The `using` directives: `Microsoft.AspNetCore.Components` and `Microsoft.AspNetCore.Components.Rendering`,
   then the Imports_Files' `@using`s outermost first, then the file's own, each namespace once. The first is
   the import Razor gives every component, which `[Parameter]` and `RenderFragment` in the fixture's
   `Greeting.razor` rely on; the second names `RenderTreeBuilder` for the render method. Mist's `using`
   names a namespace only, so `@using static` and `@using Alias = Type` are *Using form not supported*.
2. No `namespace`: the unit is placed by the inferred-namespace rule, so `Components/Counter.razor`
   lands in `RazorComponents.Components` like a `.mist` file beside it would. `@namespace` writes one.
3. Attributes from directives: `[Route("/counter")]`, `[Layout(MainLayout)]`, each `@attribute`, and the
   render-mode attribute of Requirement 8.8.
4. `public class Counter : ComponentBase` with the `@implements` types appended to the base list.
5. The Code_Block members verbatim, each a verbatim segment of the origin map.
6. The render method.

The render method for the fixture's `Counter.razor`, as task 5 measured it against the C# the Razor compiler
generates for the same component (the whitespace calls 3, 9, 23 and 28 and the numbering after them are the
measured change from the sketch this section first showed; the rules are in section 6):

```mist
protected void BuildRenderTree(RenderTreeBuilder __builder)
{
    __builder.OpenComponent<PageTitle>(0);
    __builder.AddComponentParameter(1, "ChildContent", (RenderFragment)((RenderTreeBuilder __b1) =>
    {
        __b1.AddContent(2, "Counter");
    }));
    __builder.CloseComponent();
    __builder.AddMarkupContent(3, "\n\n");
    __builder.AddMarkupContent(4, "<h1>Counter</h1>\n\n");
    __builder.OpenElement(5, "p");
    __builder.AddAttribute(6, "role", "status");
    __builder.AddContent(7, "Current count: ");
    __builder.AddContent(8, count);
    __builder.CloseElement();
    __builder.AddMarkupContent(9, "\n");
    __builder.OpenElement(10, "p");
    __builder.AddContent(11, "The count is a ");
    __builder.AddContent(12, int.Name);
    __builder.AddContent(13, ".");
    __builder.CloseElement();
    if (lastReset is void)
    {
        __builder.AddMarkupContent(14, "<p>Never reset.</p>");
    }
    else
    {
        __builder.OpenElement(15, "p");
        __builder.AddContent(16, "Last reset at count ");
        __builder.AddContent(17, lastReset);
        __builder.AddContent(18, ".");
        __builder.CloseElement();
    }
    __builder.OpenElement(19, "button");
    __builder.AddAttribute(20, "class", "btn btn-primary");
    __builder.AddAttribute(21, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, Increment));
    __builder.AddContent(22, "Click me");
    __builder.CloseElement();
    __builder.AddMarkupContent(23, "\n");
    __builder.OpenElement(24, "button");
    __builder.AddAttribute(25, "class", "btn btn-secondary");
    __builder.AddAttribute(26, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, Reset));
    __builder.AddContent(27, "Reset");
    __builder.CloseElement();
    __builder.AddMarkupContent(28, "\n\n");
    __builder.OpenComponent<Greeting>(29);
    __builder.AddComponentParameter(30, "Title", "Counter says");
    __builder.AddComponentParameter(31, "Count", count);
    __builder.AddComponentParameter(32, "ChildContent", (RenderFragment)((RenderTreeBuilder __b2) =>
    {
        __b2.AddMarkupContent(33, "<em>Nested from the counter.</em>");
    }));
    __builder.CloseComponent();
}
```

Razor's own output makes the same 34 calls with the same numbers, passing child content through
`AddAttribute` where this translation uses `AddComponentParameter`, which writes the same frame. The fixture's
`Program.mist` renders the translated `Counter` to HTML byte-identical to the C# component's.

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
| `<!-- c -->`, `@* c *@` | nothing, and no sequence number |
| `@on<e>="h"` | `AddAttribute(seq, "on<e>", EventCallback.Factory.Create<TArgs>(this, h))` |
| `@on<e>:preventDefault` | `AddEventPreventDefaultAttribute(seq, "on<e>", true)` |
| `@on<e>:stopPropagation` | `AddEventStopPropagationAttribute(seq, "on<e>", true)` |
| `@key="k"` | `SetKey(k)` |
| `@ref="f"` on element | `AddElementReferenceCapture(seq, (ElementReference __r) => { f = __r; })` |
| `@ref="f"` on component `T` | `AddComponentReferenceCapture(seq, (System.Object __r) => { if (__r is T __c) { f = __c; } })` |
| `@attributes="d"` | `AddMultipleAttributes(seq, d)` |
| `@bind="f"` | `AddAttribute(seq, "value", BindConverter.FormatValue(f))`; `AddAttribute(seq, "onchange", EventCallback.Factory.CreateBinder(this, (T __v) => { f = __v; }, f))`; `SetUpdatesAttributeName("value")`, which takes no sequence number |
| `<Comp A="x">` | `OpenComponent<Comp>(seq)`; `AddComponentParameter(seq, "A", x or "x")`; `CloseComponent()` |
| child content | `AddComponentParameter(seq, "ChildContent", (RenderFragment)((RenderTreeBuilder __b) => { … }))` |
| `<Tmpl Context="c">` under `Comp` | `AddComponentParameter(seq, "Tmpl", (RenderFragment<TValue>)((TValue c) => (RenderTreeBuilder __b) => { … }))` |
| `@rendermode m` on a tag | `AddComponentRenderMode(m)` |
| `@bind-P="f"` | `AddComponentParameter(seq, "P", f)`; `AddComponentParameter(seq, "PChanged", EventCallback.Factory.Create<T>(this, (T __v) => { f = __v; }))` |

Sequence numbers count every call in source order, including the calls inside fragments, which is how the
Razor compiler numbers them and what the differ relies on.

**Decided at task 5, measured against the Razor compiler's output:**

- An element's attributes lower in source order, events and `@attributes` in place, except `@bind`: its value
  attribute, binder and `SetUpdatesAttributeName` follow every other attribute, event and `@attributes` of the
  element, wherever `@bind` was written. `SetKey` follows them, then the reference capture. Measured on
  2026-11-01 against the C# that SDK 10.0.301 generated for `class` before and after `@bind`, `@attributes`
  with an attribute after it, and `<select>`: `<input @bind="name" class="a" />` writes `class` at 1, `value`
  at 2 and the binder at 3. A component's parameters lower in source order, then its child content and
  templates in child order, then `SetKey`, the reference capture and `AddComponentRenderMode`.
- Mist has no downcast from `System.Object`, so a component reference narrows with `is` instead of the cast
  Razor writes; `object` in a Mist lambda is the unknown type, so the parameter is `System.Object`.
- An event's argument type is the catalog's full name, written without its namespace when the unit imports
  it. A literal attribute value for an `EventCallback` parameter is wrapped in
  `EventCallback.Factory.Create<T>(this, ...)`, as Razor does; a minimized one passes `true`.
- An element `@bind` ends with `SetUpdatesAttributeName("value")` right after its binder, before `SetKey` and the
  reference capture, telling the renderer which attribute the handler updates so that a re-render does not
  overwrite what the user typed. It is not a frame and takes no sequence number. Measured on 2026-11-01 against
  the C# that SDK 10.0.301 generated for the plain binding and for `:event`, `:format`, `:culture` and both of
  the last two; `@bind-P` on a component has no such call. `<input @bind="name" @bind:event="oninput" />`
  lowers to:

  ```mist
  __builder.OpenElement(0, "input");
  __builder.AddAttribute(1, "value", BindConverter.FormatValue(name));
  __builder.AddAttribute(2, "oninput", EventCallback.Factory.CreateBinder(this, (string __v) => { name = __v; }, name));
  __builder.SetUpdatesAttributeName("value");
  __builder.CloseElement();
  ```

- `@bind`'s `T` is the declared type of the field or property of that name in the Code_Blocks; a value that
  names no such member gets an untyped lambda. `:format` is a string literal and `:culture` an expression,
  both passed by position, since Mist has no named arguments.
- An attribute with no value or an empty one is `AddAttribute(seq, name)`; a mixed value parenthesizes each
  expression, `"a " + (expr) + " b"`.
- A `@{ ... }` block's code is written inline, without braces, so its locals reach the markup after it, as in
  Razor. A control block is its Mist statement with the keyword and header copied.
- An HTML comment is dropped (Requirement 5.7, decided by the owner on 2026-11-01), as Razor's component
  compiler drops it: no call, no sequence number, and its text is never scanned for transitions. Measured on
  2026-11-01 against the C# that SDK 10.0.301 generated for a throwaway component with comments at the root,
  inside elements, inside `<text>` and inside `@if` bodies: whitespace trimming sees through a comment, so the
  whitespace it leaves at an edge or beside code is dropped; the texts on either side stay separate siblings,
  which fold into one `AddMarkupContent` where a lone text would be `AddContent` (`<p>@n <!-- c --> after</p>`
  gives `AddMarkupContent(seq, "  after")`); an element holding only comments is static and empty; and the line
  break after a code block is markup when a comment comes between them.
- A Razor comment between two texts splits them the same way: the texts stay separate siblings, trimming
  drops a whitespace-only one at an edge, and when two or more survive they fold into one `AddMarkupContent`
  with the text as written, entities undecoded, where one text left alone is `AddContent` under whitespace
  rule 7. Next to an element, code or another comment, a Razor comment changes nothing, so an element after it
  folds by rule 6. Measured on 2026-11-01 against the C# that SDK 10.0.301 generated: `<p>a@* gone *@b @n</p>`
  gives `AddMarkupContent(seq, "ab ")`, `<p> @* c *@b @n</p>` gives `AddContent(seq, "b ")`,
  `<p>a@* c *@&amp;b @n</p>` gives `AddMarkupContent(seq, "a&amp;b ")`, `<p>a@* x *@<b>c</b> @n</p>` gives
  `AddContent(seq, "a")` then `AddMarkupContent(seq, "<b>c</b> ")`, and a static `<div>a@* x *@<b>c</b></div>`
  gives `AddMarkupContent(seq, "<div>a<b>c</b></div>")`.

**Whitespace (Requirement 5.8), measured at task 5.** Read from the C# that the Razor compiler of SDK
10.0.113 generated for the fixture, rewritten in C#, and for probe components beside it in a throwaway Blazor
project outside the repository; `MarkupWhitespaceTests` and `StaticMarkupTests` pin each rule:

1. Whitespace-only text is dropped at the start and end of the file's root content, of each element's
   children, of each component's child content and template, and of each block body.
2. Whitespace-only text on either side of code, a control block, a `@{ ... }` block or a statement inside a
   block, is dropped; a Razor comment between them changes nothing.
3. Other whitespace-only text is kept: between root siblings (Counter's `"\n\n"` after `</PageTitle>` and
   `"\n"` between its two `<p>` elements) and between the children of an element.
4. Text that holds anything besides whitespace is kept whole, its leading and trailing whitespace included,
   except that the line break ending a code block's line belongs to the block. A `@code` block owns its line
   as well: the indentation before it, and the spaces and one line break after its `}`. Trimming sees through
   the block, and only then do the texts on either side of it merge into one. So at the end of the file a
   text-bearing tail keeps every line break before the directive, while an element-final or expression-final
   whitespace-only tail, and the whitespace after the block, are dropped. SDK 10.0.113, measured on macOS
   arm64 on 2026-11-01: `<span>x</span> y\n@code {...}\n` keeps one newline (two with a blank line before
   `@code`), `<p>a</p>\n@code {...}\n` ends at `</p>`, and `@x\n@code {...}\n` ends at `AddContent(0, x)`.
   SDK 10.0.301, measured on Windows x64 on 2026-11-01 with LF and CRLF: `a\n@code {...}\nb\n` is one
   `AddMarkupContent(0, "a\nb\n")`, `<p>a</p>\n@code {...}\n<p>b</p>\n` gives `"<p>a</p>\n"` then
   `"<p>b</p>"`, `a\n\n@code {...}\n\nb\n` gives `"a\n\n\nb\n"`, `a\n  @code {...}  \nb\n` gives `"a\nb\n"`,
   and `<span>x</span> y\n@code {...}\n\n` still ends at `" y\n"`.
5. Inside a block, the line break and indentation between two markup lines, elements, HTML comments or `@:`
   lines, are markup; a `@:` line keeps its own line break; the indentation around a `<text>` tag is code,
   and the tag's content stays apart from the text beside it.
6. An element with no Code_Region, component or directive attribute anywhere inside is static, except an
   `<option>` with a `<select>` ancestor (case-insensitive); that option and its ancestors stay as frames.
   SDK 10.0.113, measured on macOS arm64 on 2026-11-01: empty, text-only and static-child forms of `<option>`,
   `<textarea>`, `<script>`, `<style>`, `<pre>`, `<template>` and `<div>` fold, as do empty and text-only
   `<select>` elements. A static select containing an option writes element/attribute/content frames
   (sequences 0-4 for `<select class="s"><option value="x">x</option></select>`); a bound select writes its
   options as frames after the binder. `<div><option value="x">x</option></div>` still folds. Static
   siblings fold into one `AddMarkupContent`. A static node joins its static left sibling only when it has no
   static content of its own, because Razor folds siblings in a postorder walk that meets an element's
   children first: `"\n\n"` before `<h1>` stays its own call while `"<h1>Counter</h1>\n\n"` folds.
7. Text that folds with no element is `AddContent` with its entities decoded, unless it holds a line break, a
   tab, a character outside ASCII or an `&` that begins no decodable entity; then it is `AddMarkupContent` with
   the text as written. Decoding uses `WebUtility.HtmlDecode`, which knows the HTML 4 entity names, so an
   HTML5-only name stays markup where Razor would decode it.
8. Static HTML is written as Razor writes it: a void element without an end tag, a self-closed non-void one
   with its end tag, an empty attribute value minimized, a single-quoted value in single quotes and every
   other value in double quotes.

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
| `@code { … }` | members verbatim | a `__` declaration is refused only in a unit that parses; see Error Handling |
| `@typeparam`, `@preservewhitespace` | *later work* diagnostic | |
| `@functions`, MVC directives | *not supported* diagnostic | |

Imports_Files are resolved by walking from the project folder to the file's folder; their directives are
prepended in that order, and `@inherits`, `@layout` and `@rendermode` from an inner file override an outer
one, as Razor applies them. An Imports_File contributes only the directives Requirement 4.5 lists; its `@page` and
`@namespace` are ignored. `@typeparam` and `@preservewhitespace` are *Directive not supported yet* wherever they
appear. The render-mode attribute class is `__RenderModeAttribute`, nested in the component, its `Mode` an
expression-bodied property, and the class carries `[<Name>.__RenderModeAttribute]`.

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
`(translated: out/RazorComponents/translated/Components/Counter.razor.mist(58,25))`.

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

Whether a project has Component_Files is decided by `StaticAssetDeployment.HasComponentFiles` from the
Claimed_Inputs the driver discovered for the root project, so discovery's `SourcePathSelection` is the one
place that enumerates them and applies the `out/` and `bin/` exclusions. The driver adds the pack to the declared packages after the referenced projects'
dependencies are joined and before Stage 1.5, so the restore, the resolver and its verbose lines treat it
as any declared package; the resolver records each restored package's folder, and the backend reads the
pack's from there. A file under the project's `wwwroot/` wins over a pack file at the same deployed path.

## Error Handling

| Situation | Where | Diagnostic |
| --- | --- | --- |
| `<div>` never closed | parser | *Unclosed element* at the open tag |
| `</span>` closing a `<div>` | parser | *Mismatched end tag* at the end tag |
| `@(` or `@{` without its close | parser | *Unterminated code region* at the transition |
| `@if`, `@code`, `@*` or `@name(` without its close | parser | *Unterminated code region* at the transition |
| `@` followed by whitespace, a digit or other non-transition | parser | *Invalid transition* at the `@`; minted at task 3 for Requirement 4.9's "transition the grammar does not admit", which Requirement 12.1 names no descriptor for |
| `@foo` where `foo` is not a directive, on a directive line | parser | *Unknown directive* |
| `@model`, `@section` | parser | *Directive not valid in a component* |
| `<Widget>` resolving to nothing | translator | *Unknown component* at the tag |
| `<Widget>` resolving to two types | translator | *Ambiguous component name* listing both |
| `class` inside `@code` | translator | *Nested type in code block* |
| `__count` declared inside `@code` or in markup code | translator | *Reserved identifier* at the declaration, when the translated unit lexes and parses. The Mist parser backtracks without recovering, so a syntax error anywhere in the class leaves no declaration to walk: the check reports nothing, the frontend reports the syntax error alone, and the reserved name is refused once that is fixed |
| `@bind-Value` on a component declaring `ValueExpression` | translator | *Expression parameter not supported* |
| `<Grid TItem="…">` | translator | *Generic component not supported* |
| `@functions` | parser | *Functions directive not supported* |
| a constructor in `@code` in a file that has an `@inject` | translator | *Constructor beside inject* at the constructor |
| `@typeparam`, `@preservewhitespace` | translator | *Directive not supported yet* at the keyword; minted at task 5 as MIST0476 for Requirement 8.10, which Requirement 12.1 names no descriptor for |
| `@using static X`, `@using A = B` | translator | *Using form not supported* at the directive; minted at task 5 as MIST0477 for Requirement 8.5 |
| `@foo` on an element, `@rendermode` on an element, `@onclick` on a component | translator | *Unknown directive attribute* at the attribute's name; minted at task 5 as MIST0478 |
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

- **Tier 1.** `RazorParserTests`, split by mode into `RazorParserMarkupTests`, `RazorParserCodeTests`,
  `RazorParserDirectiveTests` and `RazorParserMalformedTests`: text in, tree out, every shape of
  Requirement 13.1, every malformed shape with its position, and the fixture's components. `OriginMapTests`: hand-built segments, Requirement 13.2. `ComponentCatalogTests`:
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

- **`mist run` content root**: decided in task 6, `--contentRoot <bin>` for a program referencing
  `Microsoft.AspNetCore.App`; section 9 records the reason.
- **Whitespace fidelity**: decided in task 5 from the Razor compiler's output; section 6 records the rules.
