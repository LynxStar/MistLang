using System.Collections.Frozen;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// How a user-chosen name is written into C#. Mist reserves fewer words than C# does, so a valid Mist
/// name such as <c>fixed</c> or <c>event</c> is a C# keyword and is written as a verbatim identifier,
/// whose <c>@</c> is not part of the metadata name: reflection, <c>nameof</c> and printed output see
/// the name as written. Contextual keywords such as <c>var</c>, <c>value</c> and <c>nameof</c> are
/// identifiers wherever a user name can stand, so they are written as they are.
/// </summary>
internal static class IdentifierSpelling
{
    private static readonly FrozenSet<string> Reserved = new[]
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum",
        "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto",
        "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace",
        "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
        "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string",
        "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked",
        "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
        "__arglist", "__makeref", "__reftype", "__refvalue",
    }.ToFrozenSet(StringComparer.Ordinal);

    public static string Of(string name)
    {
        return Reserved.Contains(name) ? "@" + name : name;
    }

    public static string Qualified(string dottedName)
    {
        return string.Join(".", dottedName.Split('.').Select(Of));
    }
}