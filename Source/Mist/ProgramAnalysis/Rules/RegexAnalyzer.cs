using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// A regex the .NET engine refuses, wherever it is written: a literal or the string shorthand that binds as
/// one, a regex pattern in a body, and a regex inside any predicate <see cref="DeclaredPredicates"/> finds.
/// Each is judged where it is written and nowhere it is reached, so a refinement tested at many sites is one
/// complaint, and <see cref="Refinements.LiteralPredicates.IsRefused"/> keeps its uses from being another.
/// </summary>
internal sealed class RegexAnalyzer : IProgramAnalyzer
{
    public string Id => "regex-validity";

    public void Analyze(AnalysisContext context)
    {
        foreach(var node in context.Nodes)
        {
            switch(node)
            {
                case RegexLiteralExpression literal:
                    Judge(context, literal.Node, literal.Pattern, [literal]);
                    break;

                case RegexPattern regex:
                    Judge(context, regex.Node, regex.Pattern, []);
                    break;
            }
        }

        foreach(var declared in DeclaredPredicates.Of(context))
        {
            foreach(var regex in BodyWalker.DescendantsAndSelf(declared.Predicate).OfType<RegexPattern>())
            {
                Judge(context, regex.Node, regex.Pattern, []);
            }
        }
    }

    private static void Judge(AnalysisContext context, SyntaxNode at, string body, IReadOnlyList<Expression> about)
    {
        if(RegexSyntax.Error(body) is { } error)
        {
            context.Report(ProgramAnalysisDiagnostics.RegexInvalid, at, about, error);
        }
    }
}