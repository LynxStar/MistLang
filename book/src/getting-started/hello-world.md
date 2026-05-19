# Hello, World!

In the installation chapter you ran your first Mist program using a top-level statement — a single line of code with no ceremony. That's the quickest way to get something running, but most real programs use a more traditional structure with namespaces, classes, and an explicit entry point. This chapter walks through that structure so you understand what each piece does.

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
{{#include ../../snippets/getting-started/hello-world.mist}}
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
{{#include ../../snippets/getting-started/hello-world.cs}}
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
{{#include ../../snippets/getting-started/hello-world-using.mist}}
```

The `using` declaration imports the `System` namespace, which gives you access to `Console` and other fundamental .NET types. Without this line, you'd need to write `System.Console.WriteLine(...)` instead.

```mist
{{#include ../../snippets/getting-started/hello-world-namespace.mist}}
```

The `namespace` declaration places your code inside the `Hello` namespace. The semicolon-terminated form is a file-scoped namespace — it applies to everything in the file. This matches the project name from `mist.toml`.

```mist
{{#include ../../snippets/getting-started/hello-world-class.mist}}
```

This declares a public class named `Program`. In Mist, all executable code lives inside classes. The `public` modifier makes the class accessible from outside its namespace.

```mist
{{#include ../../snippets/getting-started/hello-world-main.mist}}
```

The `Main` method is the entry point of your program — the method the runtime calls when you execute the compiled output. This is the traditional way to specify where execution begins: a `public static void Main` method inside a class. It's `static` because it's called without creating an instance of `Program`, and the `string[] args` parameter receives any command-line arguments.

The return type here is `void`, which works the same as in C — the method runs and exits. Mist's `void` type has some additional nuances covered in the [Void Type](../basics/void-type.md) chapter, but for now you can treat it exactly as you would in C or C#.

```mist
{{#include ../../snippets/getting-started/hello-world-writeline.mist}}
```

This calls the `WriteLine` method on the `Console` class from the `System` namespace, printing the string `"Hello, World!"` followed by a newline to standard output.

## What's Next

You now have a working Mist project. In the next section, we'll look at how projects are structured when they grow beyond a single file — including how to organize multiple source files and reference other projects.
