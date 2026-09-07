namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// How Mist's <c>regex</c> reaches C#.
/// <para>
/// A GENERATED WRAPPER RATHER THAN THE CLR TYPE DIRECTLY, which is the whole of why this file is
/// short. Mist declares Regex <c>intrinsic</c>, so the backend owns the mapping, and three of its
/// members do not line up with System.Text.RegularExpressions.Regex: <c>Pattern</c> is spelled
/// ToString there, <c>IsFullMatch</c> does not exist at all, and the two conversions to and from
/// string are Mist's own. Spelling each of those at every use site means the emitter carrying a
/// member table AND knowing the expected type at every argument, assignment and return -- which is
/// conversion insertion, a capability of its own. A wrapper declares them once and lets C#'s own
/// rules apply them, exactly as Standard.Void is a real emitted struct for the same kind of reason.
/// </para>
/// </summary>
internal static class RegexLowering
{
    public const string ClrType = "Standard.Regex";

    private const string Underlying = "System.Text.RegularExpressions.Regex";

    /// <summary>
    /// A pattern as a C# string literal. The model already holds it as a literal's BODY, so this
    /// only puts the quotes back.
    /// </summary>
    public static string Quote(string patternBody)
    {
        return $"\"{patternBody}\"";
    }

    public static string Construct(string csharpPatternExpression)
    {
        return $"new {ClrType}({csharpPatternExpression})";
    }

    /// <summary>
    /// The pattern a FULL match needs, which Mist derives rather than calls: the CLR has no
    /// full-match API, so the pattern is wrapped and matched partially. Grouped with <c>(?:</c> so a
    /// top-level alternation is anchored as a whole rather than only its first branch.
    /// </summary>
    public static string FullMatchPattern(string patternExpression)
    {
        return $"\"^(?:\" + {patternExpression} + \")$\"";
    }

    public static string IsFullMatch(string inputExpression, string patternExpression)
    {
        return $"{Underlying}.IsMatch({inputExpression}, {FullMatchPattern(patternExpression)})";
    }

    /// <summary>
    /// The wrapper's own source. Compiled because a Mist regex is built from a pattern that is
    /// constant at nearly every site, so the cost is paid once at construction rather than per match.
    /// </summary>
    public static void Emit(Emitter.CSharpWriter writer)
    {
        writer.WriteLine("namespace Standard;");
        writer.WriteBlankLine();
        writer.WriteLine("[System.Runtime.CompilerServices.CompilerGenerated]");
        writer.WriteLine("public sealed class Regex");

        using(writer.Block())
        {
            writer.WriteLine($"private readonly {Underlying} inner;");
            writer.WriteBlankLine();
            writer.WriteLine("public Regex(string pattern)");
            using(writer.Block())
            {
                writer.WriteLine($"inner = new {Underlying}(pattern, {Underlying}Options.Compiled);");
            }

            writer.WriteBlankLine();
            writer.WriteLine("public string Pattern => inner.ToString();");
            writer.WriteBlankLine();
            writer.WriteLine("public bool IsMatch(string input) => inner.IsMatch(input);");
            writer.WriteBlankLine();
            writer.WriteLine(
                $"public bool IsFullMatch(string input) => {IsFullMatch("input", "inner.ToString()")};");
            writer.WriteBlankLine();
            writer.WriteLine("public override string ToString() => inner.ToString();");
            writer.WriteBlankLine();
            writer.WriteLine("public static implicit operator string(Regex r) => r.inner.ToString();");
            writer.WriteBlankLine();
            writer.WriteLine("public static explicit operator Regex(string s) => new Regex(s);");
        }
    }
}