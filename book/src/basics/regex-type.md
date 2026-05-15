# Regex Type

Mist has first-class support for regular expressions through the `regex` type. Regex literals use backtick delimiters, making patterns easy to write without excessive escaping. Under the hood, the compiler emits `System.Text.RegularExpressions.Regex` instances with the `Compiled` option for optimal runtime performance.

## Regex Literal Syntax

A regex literal is written by enclosing a pattern in backticks. The compiler infers the type as `regex`:

{{#tabs }}
{{#tab name="Mist" }}
```mist
var digits = `\d+`;
Console.WriteLine($"pattern={digits.Pattern}");
Console.WriteLine($"matches_123={digits.IsMatch("123")}");
Console.WriteLine($"matches_abc={digits.IsMatch("abc")}");
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
var digits = new System.Text.RegularExpressions.Regex("\\d+", System.Text.RegularExpressions.RegexOptions.Compiled);
Console.WriteLine($"pattern={digits.ToString()}");
Console.WriteLine($"matches_123={digits.IsMatch("123")}");
Console.WriteLine($"matches_abc={digits.IsMatch("abc")}");
```
{{#endtab }}
{{#endtabs }}

The backtick syntax avoids the double-backslash escaping that string-based regex patterns require. Inside backticks, `\d` means the regex digit metacharacter directly.

## Explicit Type Annotation

You can declare a variable with an explicit `regex` type annotation:

{{#tabs }}
{{#tab name="Mist" }}
```mist
regex letters = `[a-zA-Z]+`;
Console.WriteLine($"pattern={letters.Pattern}");
Console.WriteLine($"matches_Hello={letters.IsMatch("Hello")}");
Console.WriteLine($"matches_123={letters.IsMatch("123")}");
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
System.Text.RegularExpressions.Regex letters = new System.Text.RegularExpressions.Regex("[a-zA-Z]+", System.Text.RegularExpressions.RegexOptions.Compiled);
Console.WriteLine($"pattern={letters.ToString()}");
Console.WriteLine($"matches_Hello={letters.IsMatch("Hello")}");
Console.WriteLine($"matches_123={letters.IsMatch("123")}");
```
{{#endtab }}
{{#endtabs }}

The `regex` type in Mist maps to `System.Text.RegularExpressions.Regex` in the emitted C#. Every regex literal is constructed with `RegexOptions.Compiled`, which pre-compiles the pattern to IL for faster matching at runtime.

## IsMatch (Partial Match)

The `IsMatch` method checks whether the pattern matches anywhere within the input string. It returns `true` if any substring of the input matches the pattern:

{{#tabs }}
{{#tab name="Mist" }}
```mist
var wordPattern = `\bcat\b`;
Console.WriteLine($"cat_in_the_cat_sat={wordPattern.IsMatch("the cat sat")}");
Console.WriteLine($"cat_in_concatenate={wordPattern.IsMatch("concatenate")}");
Console.WriteLine($"cat_in_cat={wordPattern.IsMatch("cat")}");
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
var wordPattern = new System.Text.RegularExpressions.Regex("\\bcat\\b", System.Text.RegularExpressions.RegexOptions.Compiled);
Console.WriteLine($"cat_in_the_cat_sat={wordPattern.IsMatch("the cat sat")}");
Console.WriteLine($"cat_in_concatenate={wordPattern.IsMatch("concatenate")}");
Console.WriteLine($"cat_in_cat={wordPattern.IsMatch("cat")}");
```
{{#endtab }}
{{#endtabs }}

In this example, `\bcat\b` matches the word "cat" as a whole word. It finds a match in `"the cat sat"` and `"cat"`, but not in `"concatenate"` because the word boundaries don't align.

## IsFullMatch (Full Match)

The `IsFullMatch` method checks whether the entire input string matches the pattern from start to end. Unlike `IsMatch`, a partial match is not sufficient:

{{#tabs }}
{{#tab name="Mist" }}
```mist
var fullDigits = `\d{3}`;
Console.WriteLine($"fullmatch_123={fullDigits.IsFullMatch("123")}");
Console.WriteLine($"fullmatch_1234={fullDigits.IsFullMatch("1234")}");
Console.WriteLine($"fullmatch_12={fullDigits.IsFullMatch("12")}");
Console.WriteLine($"fullmatch_abc={fullDigits.IsFullMatch("abc")}");
```
{{#endtab }}
{{#tab name="Emitted C#" }}
```csharp
var fullDigits = new System.Text.RegularExpressions.Regex("\\d{3}", System.Text.RegularExpressions.RegexOptions.Compiled);
Console.WriteLine($"fullmatch_123={System.Text.RegularExpressions.Regex.IsMatch("123", "^(?:\\d{3})$")}");
Console.WriteLine($"fullmatch_1234={System.Text.RegularExpressions.Regex.IsMatch("1234", "^(?:\\d{3})$")}");
Console.WriteLine($"fullmatch_12={System.Text.RegularExpressions.Regex.IsMatch("12", "^(?:\\d{3})$")}");
Console.WriteLine($"fullmatch_abc={System.Text.RegularExpressions.Regex.IsMatch("abc", "^(?:\\d{3})$")}");
```
{{#endtab }}
{{#endtabs }}

The pattern `\d{3}` matches exactly three digits. `IsFullMatch("123")` returns `true` because the entire string is three digits. `IsFullMatch("1234")` returns `false` because the string has four characters, even though it contains a three-digit substring.

Under the hood, `IsFullMatch` wraps the pattern with `^(?:...)$` anchors to enforce full-string matching. This is a Mist convenience — .NET's `Regex` class only provides partial matching natively.
