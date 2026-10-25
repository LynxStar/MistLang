# Technical Design: Top-Level Statements

## Overview

This design adds top-level statements to Mist by extending the Storm grammar, parser, semantic model, C# emitter, pretty printer, and project file parser. The feature allows executable statements at file scope, with strict entry point disambiguation enforced via `mist.toml`.

## Architecture Changes

The feature touches six layers of the compiler:

1. **Grammar** (`Mist.storm`) — Add `Statement` as a `TopLevelItem` variant
2. **Project File Parser** (`ProjectFileParser.cs`) — Parse the new `entry_point` field
3. **Semantic Model Builder** (`SemanticModelBuilder.cs`) — Detect TLS files, validate constraints, resolve statement types
4. **C# Emitter** (`CSharpSourceWriter`) — Emit top-level statements as C# top-level statements
5. **Pretty Printer** (`MistPrettyPrinter.cs`) — Round-trip TLS files
6. **Entry Point Analyzer** (new) — Validate entry point rules

## Detailed Design

### 1. Grammar Extension

Add a `Statement` variant to the `TopLevelItem` rule in `Mist.storm`:

```
rule TopLevelItem
    = namespaceDeclaration:NamespaceDeclaration  as NamespaceItem
    | typeDeclaration:TypeDeclaration            as TypeItem
    | newtypeDeclaration:NewtypeDeclaration      as NewtypeItem
    | typeAliasDeclaration:TypeAliasDeclaration  as TypeAliasItem
    | withDeclaration:WithDeclaration            as WithItem
    | extensionDeclaration:ExtensionBlockDeclaration as ExtensionItem
    | statement:Statement                        as StatementItem
    ;
```

Storm will auto-generate `StatementItemTopLevelItemNode` with a `Statement` property of type `StatementNode`. No new keywords are introduced — the parser distinguishes statements from declarations by the existing token lookahead (statements start with `var`, `if`, `while`, `for`, `foreach`, `return`, `break`, expression-start tokens, or typed variable declarations without access modifiers).

**Parser disambiguation:** The parser must distinguish between a type declaration (e.g., `public class Foo`) and a statement (e.g., `var x = 5;`). The key signal is:
- If the token sequence starts with a modifier (`public`, `private`, `static`, `readonly`, `abstract`, `intrinsic`, `owned`) → it's a declaration.
- If it starts with a control keyword (`if`, `while`, `for`, `foreach`, `return`, `break`) → it's a statement.
- If it starts with `var` → it's a statement (variable declaration).
- If it starts with a type name followed by an identifier and `=` or `;` (without modifiers) → it's a statement (typed variable declaration).
- Otherwise, attempt expression statement parsing.

This mirrors how C# distinguishes top-level statements from member declarations.

### 2. Project Configuration: `entry_point` Field

Extend `ProjectConfiguration` with an optional `EntryPoint` property:

```csharp
public sealed record ProjectConfiguration(
    string Name,
    string NamespaceRoot,
    ImmutableArray<ProjectReference> References,
    string? EntryPoint = null);  // NEW
```

In `ProjectFileParser.Parse()`, after reading `namespace_root` and before reading `[[references]]`:

```csharp
// 8.5. entry_point field (optional)
string? entryPoint = null;
if (model.TryGetValue("entry_point", out var epObj) && epObj is string epStr)
{
    if (string.IsNullOrWhiteSpace(epStr))
    {
        diagnostics.Add(/* MIST_PROJECT_EMPTY_ENTRY_POINT */);
        return null;
    }
    entryPoint = epStr;
}
```

Valid values:
- `"top-level"` — use the TLS file as entry point
- A fully qualified class name (e.g., `"MyNamespace.Program"`) — use that class's `static void Main`

### 3. CompilationUnit Extension

Add a property to track whether a unit contains top-level statements:

```csharp
public sealed record CompilationUnit(Common.Text.SourceText Source)
{
    // ... existing properties ...
    
    /// <summary>
    /// True if this compilation unit's AST contains at least one
    /// StatementItemTopLevelItemNode (a top-level statement).
    /// Set after parsing, before semantic model construction.
    /// </summary>
    public bool HasTopLevelStatements { get; init; }
}
```

The driver sets this flag after parsing by inspecting the AST:

```csharp
var hasTopLevelStatements = ast is CompilationUnitNode cu 
    && cu.Items.Any(item => item is StatementItemTopLevelItemNode);
```

### 4. Entry Point Validation (New Analyzer)

Create `EntryPointAnalyzer` in `Source/Mist/Analysis/Builtin/` that runs during the Analysis stage. It receives the semantic model and project configuration, then enforces:

**Rule 1: Single TLS file**
- Scan all user-code compilation units for `HasTopLevelStatements == true`.
- If more than one exists → error `MIST_EP_0001` listing all TLS file paths.

**Rule 2: TLS + Main conflict**
- If exactly one TLS file exists AND one or more `static void Main` methods exist in user code:
  - If `entry_point` is not set → error `MIST_EP_0002` with suggestion.
  - If `entry_point` is set to `"top-level"` → valid, proceed.
  - If `entry_point` is set to a class name → error `MIST_EP_0003` ("cannot have a TLS that isn't the entry point").

**Rule 3: Multiple Mains without TLS**
- If no TLS file exists AND multiple classes have `static void Main`:
  - If `entry_point` is not set → error `MIST_EP_0004` listing all Main-containing classes.
  - If `entry_point` is set to a class name → validate that class exists and has Main.
  - If `entry_point` is set to `"top-level"` → error `MIST_EP_0005` ("no TLS file found").

**Rule 4: TLS + namespace validation**
- If a TLS file contains a block namespace declaration → error `MIST_EP_0006`.
- File-scoped namespaces are allowed (they apply to type declarations only).

**Diagnostic IDs:**
| ID | Condition |
|----|-----------|
| `MIST_EP_0001` | Multiple TLS files in project |
| `MIST_EP_0002` | TLS + Main without `entry_point` config |
| `MIST_EP_0003` | TLS exists but `entry_point` specifies a class (TLS must be entry point) |
| `MIST_EP_0004` | Multiple Mains without `entry_point` config |
| `MIST_EP_0005` | `entry_point = "top-level"` but no TLS file exists |
| `MIST_EP_0006` | TLS file contains block namespace declaration |
| `MIST_EP_0007` | `entry_point` specifies class that doesn't exist or lacks Main |

### 5. Semantic Model Builder Changes

#### Pass 1: Type Registration

When iterating `TopLevelItem` nodes in a compilation unit, skip `StatementItemTopLevelItemNode` during type registration — statements don't declare types. Type declarations that appear after statements in the same file are registered normally (they belong to the file's inferred or explicit namespace).

#### Implicit `args` Variable

For the TLS file, the semantic model builder synthesizes an implicit `args` parameter of type `string[]` in the TLS file's local scope. This is done at the start of Pass 2 for the TLS unit:

```csharp
if (unit.HasTopLevelStatements)
{
    var argsSymbol = new LocalVariableSymbol("args", stringArrayType, ...);
    tlsScope.Register(argsSymbol);
}
```

#### Pass 2: Type Resolution for TLS

Top-level statements are resolved in Pass 2 like method body statements. The builder creates a synthetic scope for the TLS file's statements and resolves each statement's expressions and types within that scope. The scope has access to:
- All types in the global scope (same as any method body)
- The implicit `args` variable
- Local variables declared by preceding top-level statements

#### Return Type Inference

If any top-level `return` statement has an integer expression, the TLS file's effective return type is `int` (exit code). If all returns are bare `return;`, the effective return type is `void`. Mixed return types (some with int, some bare) are allowed — bare returns are treated as `return 0`.

In the current compiler the synthesized entry method keeps a `void` result, and a valued top-level `return` is bound against `int` and marked as the exit code (`ReturnStatement.IsExitCode`); a `return` inside a lambda, local function or async block answers to that body instead. A value that does not convert implicitly to `int`, a `void` value included, is the type error of Requirement 7.3, reported at the value. The emitter writes an exit-code return as an assignment to `Environment.ExitCode` followed by `return;`, so a bare `return;` and falling off the end both exit with 0 however they are mixed with valued returns.

### 6. C# Emitter Changes

In `CSharpSourceWriter.EmitCompilationUnit()`, detect whether the compilation unit has top-level statements:

```csharp
public string EmitCompilationUnit(CompilationUnitNode cu)
{
    var topLevelStatements = cu.Items
        .OfType<StatementItemTopLevelItemNode>()
        .ToList();
    
    if (topLevelStatements.Count > 0)
    {
        return EmitTopLevelStatementUnit(cu, topLevelStatements);
    }
    
    // ... existing emission logic ...
}
```

`EmitTopLevelStatementUnit` emits:
1. `using` declarations at the top
2. Top-level statements directly (C# top-level statement syntax — no class/method wrapper)
3. Type declarations after the statements, wrapped in their namespace

```csharp
private string EmitTopLevelStatementUnit(
    CompilationUnitNode cu, 
    List<StatementItemTopLevelItemNode> statements)
{
    // 1. Emit usings
    EmitUsings(cu.Usings);
    
    // 2. Emit top-level statements (in order of appearance)
    foreach (var item in cu.Items)
    {
        if (item is StatementItemTopLevelItemNode stmtItem)
        {
            EmitStatement(stmtItem.Statement);
        }
    }
    
    // 3. Emit type declarations in their namespace
    var typeDecls = cu.Items
        .Where(i => i is not StatementItemTopLevelItemNode)
        .ToList();
    
    if (typeDecls.Any())
    {
        // Emit namespace block wrapping the type declarations
        EmitNamespaceWrappedDeclarations(typeDecls);
    }
}
```

**Key emission rule:** The emitted C# file uses C# top-level statements directly. The .NET SDK handles the synthetic `Program` class and `Main` method generation. This means:
- No namespace wraps the statements themselves
- Type declarations after statements get their own namespace block
- The generated `.csproj` already has `<OutputType>Exe</OutputType>`

#### Non-Entry-Point Main Methods

When `entry_point = "top-level"` and a class has `static void Main`, that method is emitted as a normal static method. No special handling needed — C# will use the top-level statements as the entry point and the Main method becomes just another static method.

### 7. Pretty Printer Changes

In `MistPrettyPrinter.EmitCompilationUnit()`, add handling for `StatementItemTopLevelItemNode`:

```csharp
foreach (var item in cu.Items)
{
    switch (item)
    {
        // ... existing cases ...
        case StatementItemTopLevelItemNode stmtItem:
            EmitStatement(stmtItem.Statement);
            break;
    }
}
```

The pretty printer already has `EmitStatement()` for method bodies — the same logic applies at file scope. Statements are printed at indent level 0 (no wrapping class/method indentation).

### 8. Driver Pipeline Integration

The `CompilerDriver` needs to pass the `ProjectConfiguration` (specifically the `EntryPoint` field) to the entry point analyzer. Options:

1. Add `EntryPoint` to `CompilerOptions`:
```csharp
public sealed record CompilerOptions(
    // ... existing fields ...
    string? EntryPoint = null);  // NEW
```

2. The CLI reads `mist.toml` and populates this field before calling `Compile()`.

3. The `EntryPointAnalyzer` receives this via the semantic model or a separate context object. Since analyzers currently only receive `ISemanticModel`, the cleanest approach is to store the entry point config on the compilation:

```csharp
public sealed record MistCompilation(
    ImmutableArray<CompilationUnit> Units,
    string? EntryPoint = null);  // NEW
```

### 9. Interaction with Namespace Inference

For a TLS file:
- If no namespace declaration exists: type declarations after the statements receive the inferred namespace (from project name/folder structure), same as any other file.
- If a file-scoped namespace exists (e.g., `namespace MyApp;`): type declarations receive that namespace. The top-level statements themselves are not namespaced.
- Block namespaces are forbidden (compile error).

### 10. Interaction with Ownership System

Top-level statements participate in the ownership system the same way method body statements do. Owned variables declared at top level follow the same move/borrow/freeze rules. The synthetic scope for TLS acts like a method body scope for ownership tracking purposes.

## Data Flow

```
mist.toml (entry_point field)
    ↓
ProjectFileParser → ProjectConfiguration.EntryPoint
    ↓
CompilerDriver → CompilerOptions.EntryPoint → MistCompilation.EntryPoint
    ↓
Mist.storm grammar (StatementItem variant)
    ↓
Parser → CompilationUnitNode with StatementItemTopLevelItemNode items
    ↓
Driver sets CompilationUnit.HasTopLevelStatements
    ↓
SemanticModelBuilder:
  - Pass 1: Skips statements, registers types normally
  - Pass 2: Resolves statement expressions, synthesizes `args` binding
    ↓
EntryPointAnalyzer: Validates single-TLS, conflict rules, entry_point config
    ↓
CSharpSourceWriter: Emits C# top-level statements + namespaced type declarations
    ↓
CSharpCompilationDriver: `dotnet build` compiles the emitted C# (SDK handles entry point)
```

## Error Messages

| Diagnostic | Message Template |
|---|---|
| `MIST_EP_0001` | `Multiple files contain top-level statements. Only one file per project may have top-level statements. Files: {paths}` |
| `MIST_EP_0002` | `Project has both top-level statements (in '{tlsPath}') and a static void Main (in '{className}'). Add entry_point = "top-level" to mist.toml to resolve this conflict.` |
| `MIST_EP_0003` | `Cannot specify entry_point = "{className}" when a top-level statement file exists. A top-level statement file must always be the entry point. Use entry_point = "top-level" instead.` |
| `MIST_EP_0004` | `Multiple classes contain static void Main: {classNames}. Add entry_point = "{suggestedClass}" to mist.toml to specify which one to use.` |
| `MIST_EP_0005` | `entry_point = "top-level" specified in mist.toml but no file contains top-level statements.` |
| `MIST_EP_0006` | `Top-level statements cannot coexist with block namespace declarations in the same file. Use a file-scoped namespace (namespace X;) instead.` |
| `MIST_EP_0007` | `entry_point specifies '{className}' but no class with that name contains a static void Main method.` |

## Files to Create/Modify

| File | Action |
|------|--------|
| `Source/Mist/Grammar/Mist.storm` | Modify — add `StatementItem` to `TopLevelItem` |
| `Source/Mist/Driver/Contracts/ProjectConfiguration.cs` | Modify — add `EntryPoint` property |
| `Source/Mist/Driver/Contracts/CompilerOptions.cs` | Modify — add `EntryPoint` property |
| `Source/Mist/Driver/Contracts/CompilationUnit.cs` | Modify — add `HasTopLevelStatements` property |
| `Source/Mist/Driver/Contracts/MistCompilation.cs` | Modify — add `EntryPoint` property |
| `Source/Mist/Driver/ProjectFileParser.cs` | Modify — parse `entry_point` field |
| `Source/Mist/Driver/CompilerDriver.cs` | Modify — set `HasTopLevelStatements` flag, pass entry point config |
| `Source/Mist/Semantics/Model/SemanticModelBuilder.cs` | Modify — handle TLS in Pass 1/2, synthesize `args` |
| `Source/Mist/Analysis/Builtin/EntryPointAnalyzer.cs` | Create — entry point validation rules |
| `Source/Mist/Backends/CSharpTranspiler/CSharpEmitter.cs` | Modify — `EmitTopLevelStatementUnit` |
| `Source/Mist/Parsing/MistPrettyPrinter.cs` | Modify — handle `StatementItemTopLevelItemNode` |
| `Source/Mist/Generated/CompilationUnitNodeExtensions.cs` | Modify — add `GetTopLevelStatements()` helper |
| `book/src/getting-started/top-level-statements.md` | Create — book chapter |
| `samples/TopLevelStatements/Program.mist` | Create — sample app |
| `samples/TopLevelStatements/mist.toml` | Create — with `entry_point = "top-level"` |
