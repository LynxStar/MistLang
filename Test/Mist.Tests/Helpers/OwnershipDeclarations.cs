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

        if(name.Contains("-argument-", StringComparison.Ordinal))
        {
            return Closings(fragment, name);
        }

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

    private static IReadOnlyList<Diagnostic> Closings(DeclarationFragments fragment, string name)
    {
        var box = fragment.Class("Box");
        var t = DeclarationFragments.Parameter(box, box.Scope, "T");
        box.FieldsList.Add(new Field("Item", t, default, default) { DeclaringType = box, DeclarationNode = HandBuiltModel.Node() });
        var resource = fragment.Class("Resource");
        resource.Modifiers = DeclarationFragments.Public with { Discipline = SubstructuralDiscipline.Linear };
        var registry = fragment.Built.Model.ConstructorConstraintClosures;
        var interner = fragment.Built.Interner;

        switch(name)
        {
            case "linear-argument-closing-an-unconstrained-field":
                registry.RecordType(box, [resource], HandBuiltModel.Node(37));
                break;
            case "plain-argument-closing-an-unconstrained-field":
                registry.RecordType(box, [fragment.Class("Plain")], HandBuiltModel.Node(37));
                break;
            case "linear-argument-closing-an-affine-constrained-field":
                box.Modifiers = DeclarationFragments.Public with { Discipline = SubstructuralDiscipline.Affine };
                t.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Affine));
                registry.RecordType(box, [resource], HandBuiltModel.Node(37));
                break;
            case "linear-argument-reaching-an-unconstrained-field-through-a-wrapper":
                var wrapper = fragment.Class("Wrapper");
                var u = DeclarationFragments.Parameter(wrapper, wrapper.Scope, "U");
                wrapper.FieldsList.Add(new Field("Inner", interner.GetOrCreateConstructedType(box, [u]), default, default) { DeclaringType = wrapper, DeclarationNode = HandBuiltModel.Node() });
                registry.RecordType(box, [u], HandBuiltModel.Node());
                registry.RecordType(wrapper, [resource], HandBuiltModel.Node(37));
                break;
            case "linear-argument-closing-a-generic-alias":
                var alias = fragment.Alias("Wrap", box);
                var aliased = DeclarationFragments.Parameter(alias, alias.Scope, "T");
                alias.AliasedType = interner.GetOrCreateConstructedType(box, [aliased]);
                registry.RecordType(box, [aliased], HandBuiltModel.Node());
                registry.RecordType(alias, [resource], HandBuiltModel.Node(37));
                break;
            default:
                throw new ArgumentException($"no closing fragment named '{name}'", nameof(name));
        }

        return fragment.Run(new DisciplineAnalyzer());
    }
}