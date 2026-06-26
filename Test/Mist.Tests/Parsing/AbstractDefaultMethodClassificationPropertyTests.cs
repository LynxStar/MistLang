// Feature: abstract-type-unification, Property 2: Slot vs Default Classification by Body Presence
// **Validates: Requirements 13.1, 13.2, 13.5**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

/// <summary>
/// A generated abstract-type method member that is either a slot (no body,
/// semicolon-terminated) or a default implementation (braced body).
/// </summary>
public sealed class AbstractMethodClassificationScenario
{
    public string Source { get; }
    public bool HasBody { get; }
    public string Description { get; }

    public AbstractMethodClassificationScenario(string source, bool hasBody, string description)
    {
        Source = source;
        HasBody = hasBody;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// Property-based test for Property 2: Slot vs Default Classification by Body Presence.
///
/// **Validates: Requirements 13.1, 13.2, 13.5**
///
/// For any method declaration inside an abstract type body, the method is classified
/// as a slot if and only if it has no body (semicolon-terminated), and as a default
/// implementation if and only if it has a braced body. The classification is
/// determined solely by body presence.
/// </summary>
public class AbstractDefaultMethodClassificationPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private static readonly string[] AbstractNames = ["IGreeting", "IRepository", "ICalc", "IService"];
    private static readonly string[] MethodNames = ["Run", "Greet", "Compute", "Name", "Process", "Build"];
    private static readonly string[] ReturnTypes = ["void", "int", "string", "bool"];
    private static readonly string[] ParamTypes = ["int", "string", "bool"];

    /// <summary>Generates a parameter list body (without the enclosing parentheses).</summary>
    private static Gen<string> GenParams()
    {
        return Gen.Choose(0, 3).SelectMany(count =>
        {
            if(count == 0)
            {
                return Gen.Constant("");
            }

            return Gen.ArrayOf(
                    from t in Gen.Elements(ParamTypes)
                    from n in Gen.Elements(new[] { "a", "b", "c", "value", "input" })
                    select $"{t} {n}",
                    count)
                // Deduplicate parameter names so the (parse-level) signature is well-formed.
                .Select(parts =>
                {
                    var used = new List<string>();
                    for(var i = 0; i < parts.Length; i++)
                    {
                        var name = parts[i].Split(' ')[1] + i;
                        var type = parts[i].Split(' ')[0];
                        used.Add($"{type} p{i}_{name}");
                    }
                    return string.Join(", ", used);
                });
        });
    }

    private static Gen<AbstractMethodClassificationScenario> GenScenario()
    {
        return from abstractName in Gen.Elements(AbstractNames)
               from methodName in Gen.Elements(MethodNames)
               from returnType in Gen.Elements(ReturnTypes)
               from paramList in GenParams()
               from hasBody in Gen.Elements(new[] { true, false })
               let signature = $"{returnType} {methodName}({paramList})"
               // Slot: semicolon-terminated. Default: empty braced body (valid at parse level
               // for any return type — the parser does not enforce returns).
               let member = hasBody ? $"{signature} {{ }}" : $"{signature};"
               let source = $"abstract {abstractName} {{ {member} }}"
               select new AbstractMethodClassificationScenario(
                   source, hasBody,
                   $"{(hasBody ? "default" : "slot")}: {signature}");
    }

    public static Arbitrary<AbstractMethodClassificationScenario> ArbitraryScenario() =>
        GenScenario().ToArbitrary();

    private static (AbstractMemberDeclarationNode? Member, ImmutableArray<Diagnostic> Diagnostics) ParseMember(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);
        var diagnostics = bag.ToImmutableArray();

        if(result.Root is not CompilationUnitNode cu)
        {
            return (null, diagnostics);
        }

        var types = cu.GetTypes();
        if(types.IsEmpty)
        {
            return (null, diagnostics);
        }

        if(types[0] is not AbstractTypeTypeDeclarationNode abstractType)
        {
            return (null, diagnostics);
        }

        var members = abstractType.AbstractDeclaration.Members;
        return (members.Length == 1 ? members[0] : null, diagnostics);
    }

    [Property(MaxTest = 200, Arbitrary = new[] { typeof(AbstractDefaultMethodClassificationPropertyTests) })]
    public Property Method_IsClassified_ByBodyPresence(AbstractMethodClassificationScenario scenario)
    {
        var (member, diagnostics) = ParseMember(scenario.Source);

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if(errors.Count > 0)
        {
            return false.ToProperty().Label(
                $"Unexpected parse errors for [{scenario.Source}]:\n  {string.Join("\n  ", errors.Select(e => e.Message))}");
        }

        if(member is null)
        {
            return false.ToProperty().Label($"Could not extract a single member from: {scenario.Source}");
        }

        if(scenario.HasBody)
        {
            return (member is AbstractDefaultMethodMemberAbstractMemberDeclarationNode)
                .ToProperty()
                .Label($"Expected default (body present) but got {member.GetType().Name} for: {scenario.Source}");
        }

        return (member is AbstractMethodMemberAbstractMemberDeclarationNode)
            .ToProperty()
            .Label($"Expected slot (no body) but got {member.GetType().Name} for: {scenario.Source}");
    }
}