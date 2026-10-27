using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// What a declaration alone decides about ownership and disciplines: `owned` on an abstract or beside
/// a discipline that implies or excludes it, `ref` on a class, an owned binding of a value whose
/// discipline already governs it, and the containment matrix -- a linear container holds anything, an
/// affine one affine or unrestricted, a relevant one relevant or unrestricted, and every other container
/// only unrestricted, a type parameter counting as what its constraint says and as nothing at all when
/// it says nothing inside a disciplined container.
/// </summary>
internal sealed class DisciplineAnalyzer : IProgramAnalyzer
{
    public string Id => "ownership-borrow-checking";

    public void Analyze(AnalysisContext context)
    {
        foreach(var type in context.Types)
        {
            switch(type)
            {
                case AbstractType { Modifiers.IsOwned: true } abstractType when Declarations.NodeOf(abstractType) is { } node:
                    context.Report(ProgramAnalysisDiagnostics.OwnedAbstract, node, [], DiagnosticSnapshot.Type(abstractType, abstractType.Name));
                    break;

                case ClassType { Modifiers.IsRef: true } refClass when Declarations.NodeOf(refClass) is { } node:
                    context.Report(ProgramAnalysisDiagnostics.RefOnClass, node, [], DiagnosticSnapshot.Type(refClass, refClass.Name));
                    break;
            }

            if(type is ConcreteType concrete)
            {
                JudgeContainer(context, concrete);
            }
        }

        ClosedContainment.Judge(context);

        foreach(var node in context.Nodes)
        {
            if(node is LocalDeclarationStatement { Local: { Ownership: OwnershipKind.Owned } local, Initializer: { } initializer }
                && OwnershipAnalyzer.Substructural(local.Type) is not SubstructuralDiscipline.None and var discipline)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.OwnedSubstructuralBinding, initializer.Node, [initializer],
                    new NameValue(local.Name), new EnumValue(Display(discipline)), DiagnosticSnapshot.Type(local.Type));
            }
        }
    }

    private static void JudgeContainer(AnalysisContext context, ConcreteType container)
    {
        var node = Declarations.NodeOf(container);
        var discipline = container.Discipline;

        if(node is not null && discipline != SubstructuralDiscipline.None && container.Modifiers.IsOwned)
        {
            context.Report(
                ProgramAnalysisDiagnostics.ConflictingDiscipline, node, [], new NameValue(container.Name), new EnumValue($"'{Display(discipline)}'"),
                new NameValue(discipline == SubstructuralDiscipline.Relevant ? "a relevant value is aliasable" : $"'{Display(discipline)}' already implies 'owned'"));
        }

        foreach(var field in container.FieldsList.Where(field => !field.Modifiers.IsStatic))
        {
            var at = field.DeclarationNode ?? node;

            if(at is null)
            {
                continue;
            }

            if(field.Type is TypeParameter parameter)
            {
                if(Constrained(parameter) is { } constrained)
                {
                    JudgeField(context, container, field, constrained, at);
                }
                else if(discipline != SubstructuralDiscipline.None)
                {
                    context.Report(
                        ProgramAnalysisDiagnostics.FieldTypeParameterUnconstrained, at, [],
                        new EnumValue(Capitalised(discipline)), DiagnosticSnapshot.Type(container, container.Name), new NameValue(field.Name), new NameValue(parameter.Name), new EnumValue(Display(discipline)));
                }

                continue;
            }

            JudgeField(context, container, field, OwnershipAnalyzer.Substructural(field.Type), at);
        }
    }

    internal static string? Reason(SubstructuralDiscipline container, SubstructuralDiscipline held)
    {
        return (container, held) switch
        {
            (_, SubstructuralDiscipline.None) => null,
            (SubstructuralDiscipline.Linear, _) => null,
            (SubstructuralDiscipline.Affine, SubstructuralDiscipline.Affine) => null,
            (SubstructuralDiscipline.Affine, _) => "the container may never be dropped, which the field's discipline does not allow",
            (SubstructuralDiscipline.Relevant, SubstructuralDiscipline.Relevant) => null,
            (SubstructuralDiscipline.Relevant, _) => "the container may be dropped more than once, which the field's discipline does not allow",
            _ => "the container has no substructural discipline and cannot guarantee the field's",
        };
    }

    private static void JudgeField(AnalysisContext context, ConcreteType container, Field field, SubstructuralDiscipline held, Storm.Runtime.SyntaxNode at)
    {
        if(Reason(container.Discipline, held) is { } reason)
        {
            context.Report(
                ProgramAnalysisDiagnostics.FieldDisciplineContainment, at, [],
                new EnumValue(Capitalised(container.Discipline)), DiagnosticSnapshot.Type(container, container.Name), new EnumValue(Display(held)), new NameValue(field.Name), new NameValue(reason));
        }
    }

    internal static SubstructuralDiscipline? Constrained(TypeParameter parameter)
    {
        foreach(var constraint in parameter.ConstraintsList)
        {
            switch((constraint as KeywordConstraint)?.Kind)
            {
                case TypeParameterConstraintKind.Linear:
                    return SubstructuralDiscipline.Linear;
                case TypeParameterConstraintKind.Affine:
                    return SubstructuralDiscipline.Affine;
                case TypeParameterConstraintKind.Relevant:
                    return SubstructuralDiscipline.Relevant;
            }
        }

        return null;
    }

    internal static string Display(SubstructuralDiscipline discipline)
    {
        return discipline switch
        {
            SubstructuralDiscipline.Linear => "linear",
            SubstructuralDiscipline.Affine => "affine",
            SubstructuralDiscipline.Relevant => "relevant",
            _ => "unrestricted",
        };
    }

    internal static string Capitalised(SubstructuralDiscipline discipline)
    {
        var name = Display(discipline);
        return char.ToUpperInvariant(name[0]) + name[1..];
    }
}