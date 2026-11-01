# Razor Components

A `.razor` file in a Mist project is an ordinary Blazor component whose code is Mist. Every position that
holds C# in a C# Blazor application holds Mist instead: `@code` blocks, `@` expressions, control-flow
headers, event handlers, directive arguments, and the host program and services behind them. `mist build`
compiles the whole application. No Razor source generator, Web SDK or MSBuild takes part.

Razor belongs to the C# backend, not to the language. The backend claims the `.razor` extension and turns
each component into a Mist class deriving `ComponentBase`. The `@code` members are copied into that class as
written. The markup becomes a `BuildRenderTree` method made of the same `RenderTreeBuilder` calls the Razor
compiler emits. From there the ordinary parser, program model, analyzers and emitter run unchanged, so Mist's
own rules judge the component. A diagnostic is reported at the `.razor` line you wrote, not in the generated
Mist.

## A Project with Components

Components need the ASP.NET Core shared framework. Put `.razor` files anywhere under the project folder;
nothing else opts them in:

```toml
name = "RazorComponents"

[framework_references]
"Microsoft.AspNetCore.App" = true
```

A component's namespace follows the [inferred namespace](../basics/namespaces.md) rule a `.mist` file
follows: the project's namespace root, then the file's folders. So `Components/Counter.razor` declares
`RazorComponents.Components.Counter`, and `@namespace` overrides that. An `_Imports.razor` file gives its
`@using`, `@inherits`, `@layout`, `@implements`, `@inject`, `@attribute` and `@rendermode` directives to
every component in its folder and below.

The translated Mist is written to `out/<project>/translated/<path>.razor.mist`, beside the emitted C#.
You never need to read it, but `--verbose` names the translated line next to each diagnostic, so a
translator defect can be traced.

## Parameters

A `[Parameter]` property in `@code` is a component parameter. Mist does not let a property hold a value
its type cannot have, and the runtime never assigns a parameter the parent leaves out. So a parameter whose
type has no default is declared optional and narrowed where it is read. `RenderFragment? ChildContent` below
is optional because a parent may pass no child content. `[EditorRequired]` would not change that, since it
is an analyzer hint and not a runtime guarantee. `string` and `int` have defaults, so `Title` and `Count`
need nothing more.

```razor
{{#include ../../snippets/advanced/razor-components/Greeting.razor}}
```

{{#tabs }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/razor-components/Greeting.razor.cs}}
```
{{#endtab }}
{{#endtabs }}

## Markup, Code and Events

`Counter.razor` is a page. `@page` becomes `[Route("/counter")]`. Each `@` expression and control-flow
block is Mist: `int.Name` is the type's name, `lastReset is void` tests an optional, and `pure` applies to
a `@code` member as it does to any method. `@onclick="Increment"` becomes an `EventCallback` created from the
method group, typed with `MouseEventArgs` because that is the event-argument type ASP.NET Core declares for
`onclick`.

The first line of the file lists the component it uses, so the book's tools compile `Greeting.razor` with
it. A Razor comment, `@* ... *@`, produces nothing.

```razor
{{#include ../../snippets/advanced/razor-components/Counter.razor}}
```

{{#tabs }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/razor-components/Counter.razor.cs}}
```
{{#endtab }}
{{#endtabs }}

A component tag's attributes are typed by the component's parameters, as Razor types them.
`Title="Counter says"` is a string literal because `Title` is a `string`. `Count="count"` is the field
`count` because `Count` is not a string. The tag's content becomes the `ChildContent` fragment.

### What the Markup Lowers To

The render method follows what the Razor compiler does for the same markup, and it was checked against
that compiler's output:

- Markup with no code and no component in it becomes one `AddMarkupContent` call holding the HTML.
- An element that contains code or a component opens and closes as a frame, with one `AddAttribute` per
  attribute.
- Sequence numbers rise in source order, including inside fragments, as Razor assigns them.
- An HTML comment, `<!-- ... -->`, is dropped as Razor drops it. It makes no call and takes no sequence
  number, and nothing inside it is evaluated.
- Whitespace is kept or dropped by the rules measured from Razor's own output. Whitespace-only text is
  dropped at the edges of an element, a block or a fragment, and next to code. It is kept between the
  file's root elements and between an element's children. Static siblings fold into one call where Razor
  folds them. The full rules, and the measurements behind them, are in section 6 of the razor-components
  design (`specs/razor-components/design.md`), under Whitespace.

Rendered through `HtmlRenderer`, the translated `Counter` produces the same HTML, byte for byte, as a C#
component with the same markup. The repository's `samples/RazorComponents` keeps that output in the
`AllFeaturesV2` baseline. Rendering in a console program takes a service provider and the renderer's
dispatcher:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/razor-components/render-to-html.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/razor-components/render-to-html.cs}}
```
{{#endtab }}
{{#endtabs }}

## Injected Services

`@inject NavigationManager Nav` becomes a private `readonly` field and a parameter of one generated public
constructor that assigns it. Blazor's activator fills constructor parameters from the service container,
and the constructor is what lets Mist's definite assignment rules accept a field with no default. The
translation never generates an `[Inject]` property. A property like that, with no default and no
initializer, is refused like any other. A component that has an `@inject` cannot also declare a constructor
in `@code`. That is the *Constructor beside inject* error, because the services belong among that
constructor's parameters.

```razor
{{#include ../../snippets/advanced/razor-components/NavBar.razor}}
```

{{#tabs }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/razor-components/NavBar.razor.cs}}
```
{{#endtab }}
{{#endtabs }}

## Hosting with Interactive Server

The host is an ordinary Mist `Main` using the ASP.NET Core builder:

{{#tabs }}
{{#tab name="Mist" }}
```mist
{{#include ../../snippets/advanced/razor-components/host.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/advanced/razor-components/host.cs}}
```
{{#endtab }}
{{#endtabs }}

`App.razor` is the HTML document. Mist has no `@using static`, so the render mode is written in full,
`RenderMode.InteractiveServer`:

```razor
{{#include ../../snippets/advanced/razor-components/App.razor}}
```

`Routes.razor` gives the router the application's assembly. In Mist, a type's name followed by a member is
a static member access on that type, so `App.Assembly` would look for a static `Assembly` on `App`. Naming
the type alone is its `Type` value. A field holds that value, and the attribute reads `Assembly` from the
field:

```razor
{{#include ../../snippets/advanced/razor-components/Routes.razor}}
```

When an executable has components and the `Microsoft.AspNetCore.App` reference, `mist build` restores the
`Microsoft.AspNetCore.App.Internal.Assets` pack. It uses the version of the ASP.NET Core targeting pack the
reference resolved to, and a `[packages]` entry pins another. The build copies the pack's `_framework/`
folder, which holds `blazor.web.js`, to `bin/wwwroot/_framework/`, and copies the project's own `wwwroot/`
to `bin/wwwroot/`. `mist run` starts a program that references `Microsoft.AspNetCore.App` with
`--contentRoot <bin>`, so `UseStaticFiles` serves the deployed folder. The working directory stays yours,
and the two extra arguments reach `args`.

The fixture's components, served by this host and driven in a headless browser, open the `_blazor`
circuit. Clicks change the count through the Mist handlers without a reload, and a button that calls the
injected `NavigationManager` navigates on the client.

## Directives

| Directive | Lowers to |
| --- | --- |
| `@page "/r"` | `[Route("/r")]`, repeatable |
| `@layout T` | `[Layout(T)]` |
| `@inherits T` | `T` as the base class |
| `@implements T` | `T` added to the base list |
| `@inject T Name` | a `readonly` field and a constructor parameter |
| `@using N` | `using N;` |
| `@namespace N` | the class's namespace |
| `@attribute [A]` | `[A]` on the class |
| `@rendermode M` | a nested `RenderModeAttribute` subclass whose `Mode` is `M`, applied to the class |
| `@code { ... }` | the members, copied as written |

`@functions` is refused in favor of `@code`. `@model`, `@section`, `@addTagHelper` and the other MVC
directives are refused as not valid in a component. `@using static` and `@using Alias = Type` are refused
because Mist's `using` names a namespace. An identifier that begins with two underscores is reserved for
the translation. Type declarations belong in a `.mist` file, not in `@code`.

## Not Yet Supported

> **Later work:** generic components (`@typeparam` and type-argument attributes); `@bind-Value` on a
> component that also declares a `ValueExpression` parameter, which needs an answer for expression trees;
> code-behind as a sibling `Counter.razor.mist`, folded into the class without `partial`;
> `@preservewhitespace`; cascading values; and sections. Using a generic component from markup, or
> `@typeparam` and `@preservewhitespace` directives, is reported as not supported yet.

These are outside the feature altogether: IDE classification, completion and hot reload for `.razor` files;
CSS isolation (`.razor.css`) and collocated scripts (`.razor.js`); fingerprinted asset paths, import maps and
`MapStaticAssets`; Razor Pages, MVC views and `.cshtml`; Blazor WebAssembly; and any Razor syntax in a
`.mist` file.
