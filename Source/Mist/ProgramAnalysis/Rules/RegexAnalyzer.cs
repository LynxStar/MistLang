using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// Judge each regex where written; <see cref="Refinements.LiteralPredicates.IsRefused"/> prevents
/// repeated complaints when a refinement containing it is tested elsewhere.
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
            context.Report(ProgramAnalysisDiagnostics.RegexInvalid, at, about, new TextValue(error));
        }
    }
}