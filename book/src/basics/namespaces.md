# Namespaces

Namespaces organize your code into logical groups and prevent naming conflicts between types. Mist supports the same namespace mechanics as C# — file-scoped declarations, block declarations, and `using` imports — plus automatic namespace inference from your project's directory structure.

## File-Scoped Namespace Declarations

A file-scoped namespace applies to the entire file. Place it at the top (after any `using` declarations) and terminate it with a semicolon. All types declared in the file belong to that namespace.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace Namespaces.Utilities;

public class StringHelper
{
    public static string Greet(string name)
    {
        return $"Hello, {name}!";
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace Namespaces.Utilities;

public class StringHelper
{
    public static string Greet(string name)
    {
        return $"Hello, {name}!";
    }
}
```
{{#endtab }}
{{#endtabs }}

File-scoped namespaces reduce indentation since you don't need braces around the entire file contents.

## Block Namespace Declarations

A block namespace uses braces to scope the enclosed types. This form is useful when you need multiple namespaces in a single file or want explicit visual grouping.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace Namespaces.Helpers
{
    public class MathHelper
    {
        public static int Add(int a, int b)
        {
            return a + b;
        }
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace Namespaces.Helpers
{
    public class MathHelper
    {
        public static int Add(int a, int b)
        {
            return a + b;
        }
    }
}
```
{{#endtab }}
{{#endtabs }}

You can declare multiple block namespaces in the same file:

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace Namespaces.MixedA
{
    public class AlphaType
    {
        public string Name()
        {
            return "AlphaType";
        }
    }
}

namespace Namespaces.MixedB
{
    public class BetaType
    {
        public string Name()
        {
            return "BetaType";
        }
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace Namespaces.MixedA
{
    public class AlphaType
    {
        public string Name()
        {
            return "AlphaType";
        }
    }
}

namespace Namespaces.MixedB
{
    public class BetaType
    {
        public string Name()
        {
            return "BetaType";
        }
    }
}
```
{{#endtab }}
{{#endtabs }}

## Namespace Inference from Project Name

Files without an explicit `namespace` declaration are automatically placed under the project's **NamespaceRoot**. The NamespaceRoot is defined by the `namespace_root` field in `mist.toml`, or defaults to the project `name` if `namespace_root` is not specified.

For example, given this `mist.toml`:

```toml
name = "Namespaces"
```

A file at the project root with no namespace declaration receives the inferred namespace `Namespaces`:

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

// No explicit namespace — inferred as "Namespaces" from project name

public class AppConfig
{
    private string appName;
    private string version;

    public AppConfig(string appName, string version)
    {
        this.appName = appName;
        this.version = version;
    }

    public string Display()
    {
        return $"App: {appName} v{version}";
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace Namespaces;

public class AppConfig
{
    private string appName;
    private string version;

    public AppConfig(string appName, string version)
    {
        this.appName = appName;
        this.version = version;
    }

    public string Display()
    {
        return $"App: {appName} v{version}";
    }
}
```
{{#endtab }}
{{#endtabs }}

The compiler inserts the inferred namespace in the emitted C# even though you didn't write one in the Mist source.

## Nested Namespaces from Directory Structure

Subdirectories under the project root map to nested namespaces. A file in `Models/` receives the namespace `{NamespaceRoot}.Models`, and a file in `Models/Game/` receives `{NamespaceRoot}.Models.Game`.

Given this project layout:

```
Namespaces/
├── mist.toml          (name = "Namespaces")
├── Program.mist       → namespace Namespaces
├── Models/
│   ├── Player.mist    → namespace Namespaces.Models
│   └── Game/
│       └── GameState.mist → namespace Namespaces.Models.Game
```

A file in `Models/` with no explicit namespace:

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

// No explicit namespace — inferred as "Namespaces.Models" from folder path

public class Player
{
    private string name;
    private int health;

    public Player(string name, int health)
    {
        this.name = name;
        this.health = health;
    }

    public string Describe()
    {
        return $"Player: {name}, HP: {health}";
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace Namespaces.Models;

public class Player
{
    private string name;
    private int health;

    public Player(string name, int health)
    {
        this.name = name;
        this.health = health;
    }

    public string Describe()
    {
        return $"Player: {name}, HP: {health}";
    }
}
```
{{#endtab }}
{{#endtabs }}

And a file nested deeper in `Models/Game/`:

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

// No explicit namespace — inferred as "Namespaces.Models.Game"

public class GameState
{
    private int level;
    private bool active;

    public GameState(int level, bool active)
    {
        this.level = level;
        this.active = active;
    }

    public string Summary()
    {
        return $"Level: {level}, Active: {active}";
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace Namespaces.Models.Game;

public class GameState
{
    private int level;
    private bool active;

    public GameState(int level, bool active)
    {
        this.level = level;
        this.active = active;
    }

    public string Summary()
    {
        return $"Level: {level}, Active: {active}";
    }
}
```
{{#endtab }}
{{#endtabs }}

## Using Declarations

To reference types from another namespace, add a `using` declaration at the top of the file. This imports all public types from the specified namespace.

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;
using Namespaces.Models;
using Namespaces.Models.Game;

public class Program
{
    public static void Main(string[] args)
    {
        var player = new Player("Alice", 100);
        Console.WriteLine(player.Describe());

        var state = new GameState(1, true);
        Console.WriteLine(state.Summary());
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;
using Namespaces.Models;
using Namespaces.Models.Game;

namespace Namespaces;

public class Program
{
    public static void Main(string[] args)
    {
        var player = new Player("Alice", 100);
        Console.WriteLine(player.Describe());

        var state = new GameState(1, true);
        Console.WriteLine(state.Summary());
    }
}
```
{{#endtab }}
{{#endtabs }}

Without the `using Namespaces.Models;` declaration, the compiler would not resolve `Player` since it lives in a different namespace.

## Same-Namespace Type References

Types within the same namespace can reference each other directly without a `using` declaration. If two files share the same inferred or explicit namespace, their types are mutually visible.

In the example above, `Program.mist` and `AppConfig.mist` both live at the project root with no explicit namespace, so they both belong to the `Namespaces` namespace. `Program` can use `AppConfig` directly:

```mist
// Both files are in namespace "Namespaces" — no using needed
var config = new AppConfig("Namespace Demo", "1.0");
Console.WriteLine(config.Display());
```

Similarly, `Player.mist` and `Item.mist` both live in the `Models/` directory, so they share the inferred namespace `Namespaces.Models` and can reference each other without imports.
