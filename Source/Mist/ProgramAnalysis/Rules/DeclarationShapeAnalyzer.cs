using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// Refusals decidable from a declaration alone: a base or a newtype source that is sealed, `sealed` on
/// an alias, an enum base that is not readonly, two variants sharing a value, and a default block
/// naming something that is not a field. Order 6 of the analysis register; each shape is classic's
/// verdict, measured, and the enum and sealed rules are the full-accessibility-model and
/// enum-readonly-restriction specs' own.
/// </summary>
internal sealed class DeclarationShapeAnalyzer : IProgramAnalyzer
{
    public string Id => "declaration-shape-checks";

    public void Analyze(AnalysisContext context)
    {
        foreach(var type in SourceTypes.Of(context.Model))
        {
            switch(type)
            {
                case ConcreteType { BaseType: { } baseType } concrete:
                    if(Declarations.IsSealed(baseType))
                    {
                        Report(context, concrete.DeclarationNode, ProgramAnalysisDiagnostics.SealedBaseType, TypeDisplay.Mist(baseType));
                    }

                    CheckDefaults(context, concrete);
                    break;

                case ConcreteType concrete:
                    CheckDefaults(context, concrete);
                    break;

                case Newtype { SourceType: { } source } newtype when Declarations.IsSealed(source):
                    Report(context, newtype.DeclarationNode, ProgramAnalysisDiagnostics.NewtypeOverSealedType, TypeDisplay.Mist(source));
                    break;

                case TypeAlias { Modifiers.IsSealed: true } alias:
                    Report(context, alias.DeclarationNode, ProgramAnalysisDiagnostics.SealedTypeAlias);
                    break;

                case EnumType enumType:
                    CheckEnum(context, enumType);
                    break;
            }
        }
    }

    private static void CheckEnum(AnalysisContext context, EnumType enumType)
    {
        if(enumType.UnderlyingType is { } underlying)
        {
            var constituents = underlying is UnionType union ? union.Constituents : [underlying];

            foreach(var constituent in constituents.Where(constituent => !Declarations.IsReadonly(constituent) && constituent is not ErrorType))
            {
                Report(context, enumType.DeclarationNode, ProgramAnalysisDiagnostics.EnumBaseNotReadonly, enumType.Name, TypeDisplay.Mist(constituent));
            }
        }

        var seen = new Dictionary<object, string>();

        foreach(var variant in enumType.MembersList)
        {
            if(Constant(variant.Value) is not { } value)
            {
                continue;
            }

            if(seen.TryGetValue(value, out var first))
            {
                Report(
                    context, (SyntaxNode?)variant.DeclarationNode ?? enumType.DeclarationNode,
                    ProgramAnalysisDiagnostics.EnumDuplicateValue, variant.Name, first, enumType.Name);
            }
            else
            {
                seen[value] = variant.Name;
            }
        }
    }

    private static object? Constant(Expression? value)
    {
        return value switch
        {
            LiteralExpression { Value: long constant } => constant,
            LiteralExpression { Value: string text } => text,
            UnaryExpression { OperatorKind: UnaryOperatorKind.Negate, Operand: LiteralExpression { Value: long constant } } => -constant,
            _ => null,
        };
    }

    private static void CheckDefaults(AnalysisContext context, ConcreteType type)
    {
        if(type.DefaultBlock is { } block)
        {
            CheckEntries(context, type, block.Entries);
        }
    }

    private static void CheckEntries(AnalysisContext context, ConcreteType type, IReadOnlyList<DefaultEntry> entries)
    {
        foreach(var entry in entries)
        {
            if(!type.FieldsList.Exists(field => field.Name == entry.FieldName))
            {
                var at = entry switch
                {
                    DefaultValueEntry valued => valued.Value.Node,
                    DefaultBindingEntry binding => binding.BindingNode,
                    _ => null,
                };

                Report(context, at, ProgramAnalysisDiagnostics.DefaultEntryNotAField, entry.FieldName, type.Name);
                continue;
            }

            if(entry is DefaultBindingEntry { BoundType: ConcreteType bound } nested)
            {
                CheckEntries(context, bound, nested.NestedEntries);
            }
        }
    }

    private static void Report(AnalysisContext context, SyntaxNode? at, DiagnosticDescriptor descriptor, params object?[] args)
    {
        if(at is not null)
        {
            context.Report(descriptor, at, [], args);
        }
    }
}