# Hello, World!

Now that you have Mist installed, let's write your first program. By the end of this section you'll have a working project that prints a message to the console.

## Creating a Project

A Mist project is a directory containing two things: a `mist.toml` configuration file and at least one `.mist` source file. Create a new directory for your project:

```bash
mkdir hello
cd hello
```

Your project directory will look like this:

```
hello/
├── mist.toml
└── Program.mist
```

## The Project File

Every Mist project needs a `mist.toml` file that tells the compiler about your project. Create `mist.toml` with the following contents:

```toml
name = "Hello"
```

The `name` field identifies your project. The compiler uses it as the default namespace when your source files don't declare one explicitly.

## Writing the Program

Create a file called `Program.mist` with the following code:

{{#tabs }}
{{#tab name="Mist" }}
```mist
using System;

namespace Hello;

public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("Hello, World!");
    }
}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
using System;

namespace Hello;

public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("Hello, World!");
    }
}
```
{{#endtab }}
{{#endtabs }}

## Building and Running

Compile your program with the `mist build` command:

```bash
mist build
```

Then run it with `mist run`:

```bash
mist run
```

You should see the following output:

```
Hello, World!
```

The `mist build` command compiles your `.mist` source files into a .NET executable. The `mist run` command builds and then immediately executes the result in one step.

## Line-by-Line Explanation

Let's walk through each part of the program:

```mist
using System;
```

The `using` declaration imports the `System` namespace, which gives you access to `Console` and other fundamental .NET types. Without this line, you'd need to write `System.Console.WriteLine(...)` instead.

```mist
namespace Hello;
```

The `namespace` declaration places your code inside the `Hello` namespace. The semicolon-terminated form is a file-scoped namespace — it applies to everything in the file. This matches the project name from `mist.toml`.

```mist
public class Program
{
```

This declares a public class named `Program`. In Mist, all executable code lives inside classes. The `public` modifier makes the class accessible from outside its namespace.

```mist
    public static void Main(string[] args)
    {
```

The `Main` method is the entry point of your program. The runtime calls this method when you execute the compiled output. It's `static` because it's called without creating an instance of `Program`, and `void` because it doesn't return a value. The `string[] args` parameter receives any command-line arguments.

```mist
        Console.WriteLine("Hello, World!");
```

This calls the `WriteLine` method on the `Console` class from the `System` namespace, printing the string `"Hello, World!"` followed by a newline to standard output.

## What's Next

You now have a working Mist project. In the next section, we'll look at how projects are structured when they grow beyond a single file — including how to organize multiple source files and reference other projects.
