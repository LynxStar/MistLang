using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Helpers;

internal static class OwnershipDeclarations
{
    public static IReadOnlyList<Diagnostic> Analyze(string name)
    {
        var fragment = new DeclarationFragments();
        var container = fragment.Class("Container");
        var held = fragment.Class("Held");
        switch(name)
        {
            case "affine-declared-owned":
            case "relevant-declared-owned":
                container.Modifiers = DeclarationFragments.Public with
                {
                    IsOwned = true,
                    Discipline = name.StartsWith("affine", StringComparison.Ordinal) ? SubstructuralDiscipline.Affine : SubstructuralDiscipline.Relevant,
                };
                break;
            case "owned-abstract":
                var abstraction = fragment.Abstract("IThing");
                abstraction.Modifiers = DeclarationFragments.Public with { IsOwned = true };
                break;
            case "ref-on-class":
                container.Modifiers = DeclarationFragments.Public with { IsRef = true };
                break;
            case "owned-binding-of-linear-type":
                held.Modifiers = DeclarationFragments.Public with { Discipline = SubstructuralDiscipline.Linear };
                fragment.Root = new LocalDeclarationStatement(new LocalVariable("h", held) { Ownership = OwnershipKind.Owned }, HandBuiltModel.Literal(held, offset: 37));
                break;
            case "ref-struct-as-type-argument":
            case "ref-struct-field-in-class":
                var cursor = new StructType("Cursor", "Refusal", new Scope(null), null,
                    DeclarationFragments.At<Generated.BodyTypeTypeDeclarationNode>())
                { Modifiers = DeclarationFragments.Public with { IsRef = true } };
                if(name == "ref-struct-as-type-argument")
                {
                    _ = DeclarationFragments.Parameter(container, container.Scope, "T");
                    fragment.Built.Model.ConstructorConstraintClosures.RecordType(container, [cursor], HandBuiltModel.Node(37));
                }
                else
                {
                    container.FieldsList.Add(new Field("Field", cursor, default, default)
                    {
                        DeclaringType = container,
                        DeclarationNode = HandBuiltModel.Node(37),
                    });
                }

                break;
            default:
                var discipline = name.StartsWith("affine-field", StringComparison.Ordinal) ? SubstructuralDiscipline.Affine : SubstructuralDiscipline.Linear;
                held.Modifiers = DeclarationFragments.Public with { Discipline = discipline };
                container.Modifiers = DeclarationFragments.Public with
                {
                    Discipline = name.Contains("affine-container", StringComparison.Ordinal) ? SubstructuralDiscipline.Affine
                        : name.Contains("relevant-container", StringComparison.Ordinal) ? SubstructuralDiscipline.Relevant
                        : name.Contains("linear-container", StringComparison.Ordinal) ? SubstructuralDiscipline.Linear
                        : SubstructuralDiscipline.None,
                };
                IMistType fieldType = held;
                if(name == "unconstrained-type-parameter-field-in-linear-container")
                {
                    fieldType = DeclarationFragments.Parameter(container, container.Scope, "T");
                }
                else if(name == "task-field-in-unrestricted-container")
                {
                    var task = fragment.Built.Type("Task<>");
                    task.Modifiers = DeclarationFragments.Public with { Discipline = SubstructuralDiscipline.Linear };
                    fieldType = fragment.Built.Interner.GetOrCreateConstructedType(task, [fragment.Built.Type("Int32")]);
                }

                container.FieldsList.Add(new Field("Value", fieldType, default, default)
                {
                    DeclaringType = container,
                    DeclarationNode = HandBuiltModel.Node(37),
                });
                break;
        }

        return fragment.Run(name.StartsWith("ref-struct-", StringComparison.Ordinal) ? new RefStructPositionAnalyzer() : new DisciplineAnalyzer());
    }
}