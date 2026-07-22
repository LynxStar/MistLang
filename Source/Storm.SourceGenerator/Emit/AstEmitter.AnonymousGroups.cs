using System.Collections.Generic;
using System.Linq;
using Storm.SourceGenerator.IR;

namespace Storm.SourceGenerator.Emit;

/// <summary>
/// The repetition or optional modifier applied to an anonymous group in a grammar
/// rule. Controls the C# property type projected onto the parent node: a scalar
/// group struct (<see cref="None"/>), a nullable group struct (<see cref="Optional"/>),
/// or an <c>ImmutableArray</c> of group structs (<see cref="ZeroOrMore"/> / <see cref="OneOrMore"/>).
/// </summary>
internal enum GroupModifier
{
    None,
    Optional,
    ZeroOrMore,
    OneOrMore,
}

/// <summary>
/// Describes a single anonymous group discovered within a rule alternative: the
/// group's body sequence, the modifier applied to it, its zero-based left-to-right
/// index among the alternative's anonymous groups, and the field labels declared
/// inside it.
/// </summary>
internal readonly record struct AnonymousGroupInfo(
    SequenceIR Sequence,
    GroupModifier Modifier,
    int GroupIndex,
    IReadOnlyList<FieldLabelIR> Labels);

public static partial class AstEmitter
{
    /// <summary>
    /// Detects anonymous groups in a rule alternative by walking its expression tree
    /// left-to-right. An anonymous group is a <see cref="SequenceIR"/> nested directly
    /// inside a <see cref="ZeroOrMoreIR"/>, <see cref="OneOrMoreIR"/>, or
    /// <see cref="OptionalIR"/> that holds two or more value-bearing elements
    /// (see <see cref="CountValueBearingElements"/>). A value-bearing element is a
    /// labelled element or an unlabelled rule reference; bare fixed tokens are
    /// structural and are consumed by the parser rather than stored, so they do not
    /// count. A group with zero or one value-bearing element is not projected as a
    /// struct -- it keeps the existing scalar/list/optional promotion. Each detected
    /// group is assigned a distinct <see cref="AnonymousGroupInfo.GroupIndex"/> in
    /// source order, starting from zero.
    /// </summary>
    internal static List<AnonymousGroupInfo> DetectAnonymousGroups(RuleAlternativeIR alt)
    {
        var groups = new List<AnonymousGroupInfo>();
        var index = 0;
        WalkForGroups(alt.Expression, alt.FieldLabels, groups, ref index);
        return groups;
    }

    /// <summary>
    /// Determines whether a single labelled element qualifies for the
    /// Pure_Token_Alternation optimisation: it is passed as a one-element list whose
    /// sole label's expression is an <see cref="AlternationIR"/> where every
    /// alternative is a single <see cref="TokenRefIR"/>. When this holds, the label
    /// collapses to a single <c>Token</c> field on the group struct -- for example
    /// <c>operator:(PLUS | MINUS)</c> inside <c>(operator:(PLUS | MINUS)
    /// right:Multiplicative)*</c> becomes a <c>Token Operator</c> field. See
    /// <see cref="FieldTypeForLabeledElement"/> for the call site.
    /// </summary>
    /// <remarks>
    /// The design specifies a <c>SequenceIR seq</c> parameter alongside the labels,
    /// but the logic reads only the labels. An unused parameter is a build error
    /// under this project's analyzer configuration, so the redundant parameter is
    /// omitted while the logic remains identical to the design pseudocode.
    /// </remarks>
    internal static bool IsPureTokenAlternation(IReadOnlyList<FieldLabelIR> labelsInGroup)
    {
        if(labelsInGroup.Count != 1)
        {
            return false;
        }

        if(labelsInGroup[0].Expression is not AlternationIR alternation)
        {
            return false;
        }

        return alternation.Alternatives.All(alternative => alternative is TokenRefIR);
    }

    /// <summary>
    /// Recursively walks an expression tree, recording anonymous groups. When a
    /// repetition or optional wraps a sequence with two or more value-bearing
    /// elements, that sequence is recorded as a group and the walk does not descend
    /// into it -- nested groups are resolved separately during struct emission.
    /// Repetitions/optionals with fewer than two value-bearing elements are left for
    /// the existing scalar/list/optional promotion. Bare sequences are traversed
    /// element by element so groups nested deeper in the alternative are still found.
    /// </summary>
    private static void WalkForGroups(
        ExpressionIR expr,
        IReadOnlyList<FieldLabelIR> labels,
        List<AnonymousGroupInfo> groups,
        ref int index)
    {
        switch(expr)
        {
            case ZeroOrMoreIR { Inner: SequenceIR zeroOrMoreSeq } when CountValueBearingElements(zeroOrMoreSeq, labels) >= 2:
                groups.Add(new AnonymousGroupInfo(zeroOrMoreSeq, GroupModifier.ZeroOrMore, index++, GetLabelsInside(zeroOrMoreSeq, labels)));
                break;

            case OneOrMoreIR { Inner: SequenceIR oneOrMoreSeq } when CountValueBearingElements(oneOrMoreSeq, labels) >= 2:
                groups.Add(new AnonymousGroupInfo(oneOrMoreSeq, GroupModifier.OneOrMore, index++, GetLabelsInside(oneOrMoreSeq, labels)));
                break;

            case OptionalIR { Inner: SequenceIR optionalSeq } when CountValueBearingElements(optionalSeq, labels) >= 2:
                groups.Add(new AnonymousGroupInfo(optionalSeq, GroupModifier.Optional, index++, GetLabelsInside(optionalSeq, labels)));
                break;

            case SequenceIR seq:
                foreach(var element in seq.Elements)
                {
                    WalkForGroups(element, labels, groups, ref index);
                }

                break;
        }
    }

    /// <summary>
    /// Returns true when at least one of the alternative's field labels references an
    /// expression reachable within the given sequence.
    /// </summary>
    private static bool HasLabelsInside(SequenceIR seq, IReadOnlyList<FieldLabelIR> labels)
    {
        var reachable = new HashSet<ExpressionIR>();
        CollectReachableExpressions(seq, reachable);
        return labels.Any(label => reachable.Contains(label.Expression));
    }

    /// <summary>
    /// Returns the subset of the alternative's field labels whose expressions are
    /// reachable within the given sequence, preserving the labels' source order.
    /// </summary>
    private static IReadOnlyList<FieldLabelIR> GetLabelsInside(SequenceIR seq, IReadOnlyList<FieldLabelIR> labels)
    {
        var reachable = new HashSet<ExpressionIR>();
        CollectReachableExpressions(seq, reachable);
        return labels.Where(label => reachable.Contains(label.Expression)).ToList();
    }

    /// <summary>
    /// Counts the value-bearing elements among the direct elements of a group's inner
    /// sequence (see <see cref="IsValueBearingElement"/>). A group projects a struct
    /// only when this count is two or more: a struct is needed exactly when there are
    /// two or more correlated values to store per occurrence. With one value-bearing
    /// element the existing single promotion (scalar/list/optional) suffices; with
    /// zero (only bare structural tokens) the existing lone-token promotion applies.
    /// </summary>
    private static int CountValueBearingElements(SequenceIR seq, IReadOnlyList<FieldLabelIR> labels)
    {
        var count = 0;
        foreach(var element in seq.Elements)
        {
            if(IsValueBearingElement(element, labels))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Returns true when a direct element of a group's inner sequence carries a value
    /// that must be stored: a labelled element (a <see cref="FieldLabelIR"/> references
    /// it), an unlabelled <see cref="RuleRefIR"/>, or a nested repetition/optional that
    /// wraps a labelled sequence (itself a nested group). Bare fixed tokens (unlabelled
    /// <see cref="TokenRefIR"/> such as <c>COMMA</c>, <c>DOT</c>, <c>EQ</c>) and
    /// unlabelled alternations are structural -- matched by the parser but not stored --
    /// so they are not value-bearing.
    /// </summary>
    private static bool IsValueBearingElement(ExpressionIR element, IReadOnlyList<FieldLabelIR> labels)
    {
        if(FindLabel(element, labels) != null)
        {
            return true;
        }

        if(element is RuleRefIR)
        {
            return true;
        }

        return element switch
        {
            ZeroOrMoreIR { Inner: SequenceIR nestedSeq } => HasLabelsInside(nestedSeq, labels),
            OneOrMoreIR { Inner: SequenceIR nestedSeq } => HasLabelsInside(nestedSeq, labels),
            OptionalIR { Inner: SequenceIR nestedSeq } => HasLabelsInside(nestedSeq, labels),
            _ => false,
        };
    }

    /// <summary>
    /// Adds the given expression and every descendant reachable through sequences,
    /// alternations, optionals, and repetitions to <paramref name="result"/>.
    /// Membership uses reference identity, matching how field labels reference the
    /// expression-node instances that live in the tree.
    /// </summary>
    private static void CollectReachableExpressions(ExpressionIR expr, HashSet<ExpressionIR> result)
    {
        if(!result.Add(expr))
        {
            return;
        }

        switch(expr)
        {
            case SequenceIR seq:
                foreach(var element in seq.Elements)
                {
                    CollectReachableExpressions(element, result);
                }

                break;

            case AlternationIR alternation:
                foreach(var alternative in alternation.Alternatives)
                {
                    CollectReachableExpressions(alternative, result);
                }

                break;

            case OptionalIR optional:
                CollectReachableExpressions(optional.Inner, result);
                break;

            case ZeroOrMoreIR zeroOrMore:
                CollectReachableExpressions(zeroOrMore.Inner, result);
                break;

            case OneOrMoreIR oneOrMore:
                CollectReachableExpressions(oneOrMore.Inner, result);
                break;
        }
    }

    // ---------------------------------------------------------------
    //  Group struct projection (Tasks 3.1-3.5, 4.1-4.2)
    // ---------------------------------------------------------------

    /// <summary>
    /// A single public field emitted on a group struct: <c>public {TypeName} {Name};</c>.
    /// <see cref="IsNodeType"/> is true only for a scalar <c>{Rule}Node</c> field (a
    /// direct rule reference); it drives whether <c>Children()</c> yields the field.
    /// Token fields, list fields, and nested-group fields are not directly yielded.
    /// </summary>
    private readonly struct GroupStructField
    {
        public string Name { get; }
        public string TypeName { get; }
        public bool IsNodeType { get; }

        public GroupStructField(string name, string typeName, bool isNodeType)
        {
            Name = name;
            TypeName = typeName;
            IsNodeType = isNodeType;
        }
    }

    /// <summary>
    /// A generated group struct emitted as a sibling type of the parent node class.
    /// Carries its public fields and any nested group structs (emitted as nested
    /// <c>public struct</c> types inside this one).
    /// </summary>
    private sealed class GroupStructModel
    {
        public string StructName { get; }
        public List<GroupStructField> Fields { get; } = new List<GroupStructField>();
        public List<GroupStructModel> NestedStructs { get; } = new List<GroupStructModel>();

        public GroupStructModel(string structName)
        {
            StructName = structName;
        }
    }

    /// <summary>
    /// A single <c>[Obsolete]</c> shim property emitted on the parent node during
    /// migration. It reproduces one of the flat-field properties the pre-wave-2
    /// emitter used to generate for an anonymous group and forwards reads to the new
    /// <c>Group{N}</c> data. <see cref="GroupPropertyName"/> is the target group
    /// property named in the obsolete message (e.g. <c>Group0</c>);
    /// <see cref="Body"/> is the expression-bodied forwarding expression, minus the
    /// trailing semicolon.
    /// </summary>
    private readonly struct ObsoleteShim
    {
        public string GroupPropertyName { get; }
        public string PropertyName { get; }
        public string TypeName { get; }
        public string Body { get; }

        public ObsoleteShim(string groupPropertyName, string propertyName, string typeName, string body)
        {
            GroupPropertyName = groupPropertyName;
            PropertyName = propertyName;
            TypeName = typeName;
            Body = body;
        }
    }

    /// <summary>
    /// Projects a single detected anonymous group onto the parent node. A detected
    /// group always holds two or more value-bearing elements, so it always builds a
    /// group struct and the parent receives a <c>Group{N}</c> property typed by the
    /// group's modifier (Tasks 3.1, 3.3). The Pure_Token_Alternation optimisation is
    /// applied per field inside the struct (a labelled token-only alternation such as
    /// <c>operator:(PLUS | MINUS)</c> collapses to a <c>Token</c> field, Tasks
    /// 4.1-4.2); a whole group that is nothing but a single token alternation carries
    /// one value-bearing element, so it is never detected here and keeps its existing
    /// single promotion. When <paramref name="emitObsoleteShims"/> is set, the old
    /// flat-field properties that existed before the group was projected are
    /// reconstructed as <c>[Obsolete]</c> shims that forward to the new group data
    /// (Tasks 5.1-5.4).
    /// </summary>
    private static void AddGroupProjection(
        AnonymousGroupInfo group,
        string nodeTypeName,
        List<PropertyInfo> properties,
        List<GroupStructModel> groupStructs,
        List<ObsoleteShim> shims,
        bool emitObsoleteShims,
        HashSet<string> usedNames)
    {
        var structName = nodeTypeName + "Group" + group.GroupIndex;
        var model = BuildGroupStruct(structName, group.Sequence, group.Labels);
        groupStructs.Add(model);

        var nodeFieldNames = model.Fields
            .Where(field => field.IsNodeType)
            .Select(field => field.Name)
            .ToList();

        var groupPropName = EnsureUnique("Group" + group.GroupIndex, usedNames);
        properties.Add(GroupPropertyForModifier(groupPropName, structName, group.Modifier, nodeFieldNames));

        if(emitObsoleteShims)
        {
            BuildGroupShims(group, groupPropName, shims);
        }
    }

    /// <summary>
    /// Builds the parent-node property for a struct-backed anonymous group: a scalar
    /// group struct, a nullable group struct, or an <c>ImmutableArray</c> of group
    /// structs depending on the modifier (Task 3.3). The property is never a node
    /// type itself, so the ordinary node <c>Children()</c> emission skips it; the
    /// node-typed struct field names are carried on the property so <c>Children()</c>
    /// can iterate the group and yield them.
    /// </summary>
    private static PropertyInfo GroupPropertyForModifier(string propName, string structName, GroupModifier modifier, IReadOnlyList<string> nodeFieldNames)
    {
        return modifier switch
        {
            GroupModifier.ZeroOrMore or GroupModifier.OneOrMore
                => new PropertyInfo(propName, $"ImmutableArray<{structName}>", false, false, true, $" = ImmutableArray<{structName}>.Empty;", nodeFieldNames),
            GroupModifier.Optional
                => new PropertyInfo(propName, $"{structName}?", true, false, false, "", nodeFieldNames),
            _ => new PropertyInfo(propName, structName, false, false, false, " = default;", nodeFieldNames),
        };
    }

    /// <summary>
    /// Builds a group struct model by walking the group's inner sequence in source
    /// order. Labelled elements become public fields whose types follow the same
    /// derivation rules as ordinary labelled node fields (Task 3.2). A nested
    /// repetition/optional wrapping a labelled sequence becomes a nested group struct
    /// plus a field of that nested type (Task 3.5). Unlabelled tokens/rules are
    /// consumed by the parser but not stored.
    /// </summary>
    private static GroupStructModel BuildGroupStruct(
        string structName,
        SequenceIR sequence,
        IReadOnlyList<FieldLabelIR> groupLabels)
    {
        var model = new GroupStructModel(structName);
        var usedFieldNames = new HashSet<string>();
        var nestedIndex = 0;
        foreach(var element in sequence.Elements)
        {
            AddGroupStructField(model, element, groupLabels, usedFieldNames, ref nestedIndex);
        }

        return model;
    }

    /// <summary>
    /// Adds one field (or nested group struct) to <paramref name="model"/> for a
    /// single value-bearing element of the group's inner sequence. Labelled elements
    /// become fields named from the label; unlabelled rule references become fields
    /// named from the referenced rule; a nested repetition/optional wrapping a
    /// labelled sequence becomes a nested group struct plus a field of that nested
    /// type. Bare fixed tokens and unlabelled alternations are structural -- consumed
    /// by the parser but not stored -- so they add no field.
    /// </summary>
    private static void AddGroupStructField(
        GroupStructModel model,
        ExpressionIR element,
        IReadOnlyList<FieldLabelIR> groupLabels,
        HashSet<string> usedFieldNames,
        ref int nestedIndex)
    {
        switch(element)
        {
            case ZeroOrMoreIR { Inner: SequenceIR zeroOrMoreSeq } when HasLabelsInside(zeroOrMoreSeq, groupLabels):
                AddNestedGroup(model, zeroOrMoreSeq, GroupModifier.ZeroOrMore, groupLabels, usedFieldNames, ref nestedIndex);
                return;

            case OneOrMoreIR { Inner: SequenceIR oneOrMoreSeq } when HasLabelsInside(oneOrMoreSeq, groupLabels):
                AddNestedGroup(model, oneOrMoreSeq, GroupModifier.OneOrMore, groupLabels, usedFieldNames, ref nestedIndex);
                return;

            case OptionalIR { Inner: SequenceIR optionalSeq } when HasLabelsInside(optionalSeq, groupLabels):
                AddNestedGroup(model, optionalSeq, GroupModifier.Optional, groupLabels, usedFieldNames, ref nestedIndex);
                return;
        }

        var label = FindLabel(element, groupLabels);
        if(label != null)
        {
            var fieldName = EnsureUnique(PascalCase(label.Label), usedFieldNames);
            var fieldType = FieldTypeForLabeledElement(element, label, fieldName);
            model.Fields.Add(new GroupStructField(fieldName, fieldType, IsScalarNodeType(fieldType)));
            return;
        }

        // Unlabelled rule references are value-bearing (capture-count rule): store the
        // referenced node as a field named after the rule.
        if(element is RuleRefIR ruleRef)
        {
            var fieldName = EnsureUnique(PascalCase(ruleRef.Rule.Name), usedFieldNames);
            model.Fields.Add(new GroupStructField(fieldName, ruleRef.Rule.Name + "Node", true));
        }
    }

    /// <summary>
    /// Derives the struct field type for a labelled group element. A labelled
    /// token-only alternation collapses to <c>Token</c> (the nested
    /// Pure_Token_Alternation optimisation, Tasks 4.1-4.2); every other labelled
    /// element uses the same derivation as an ordinary labelled node field.
    /// </summary>
    private static string FieldTypeForLabeledElement(ExpressionIR element, FieldLabelIR label, string fieldName)
    {
        if(element is AlternationIR && IsPureTokenAlternation(new List<FieldLabelIR> { label }))
        {
            return "Token";
        }

        return GetPropertyInfo(element, fieldName).TypeName;
    }

    /// <summary>
    /// Returns true when a field type is a scalar node reference (<c>{Rule}Node</c>) --
    /// not a token, a nullable, a list, or a nested-group struct. Only such fields are
    /// yielded directly by <c>Children()</c>.
    /// </summary>
    private static bool IsScalarNodeType(string typeName)
    {
        return typeName.EndsWith("Node", System.StringComparison.Ordinal)
            && !typeName.Contains("<")
            && typeName != "SyntaxNode";
    }

    /// <summary>
    /// Emits a nested group struct (named <c>{outerStruct}Group{M}</c>) inside the
    /// current struct and adds a field of that nested type, typed by the nested
    /// group's modifier (Task 3.5).
    /// </summary>
    private static void AddNestedGroup(
        GroupStructModel model,
        SequenceIR nestedSeq,
        GroupModifier modifier,
        IReadOnlyList<FieldLabelIR> groupLabels,
        HashSet<string> usedFieldNames,
        ref int nestedIndex)
    {
        var currentIndex = nestedIndex;
        nestedIndex++;
        var nestedStructName = model.StructName + "Group" + currentIndex;
        model.NestedStructs.Add(BuildGroupStruct(nestedStructName, nestedSeq, groupLabels));

        var fieldName = EnsureUnique("Group" + currentIndex, usedFieldNames);
        var fieldType = modifier switch
        {
            GroupModifier.ZeroOrMore => $"ImmutableArray<{nestedStructName}>",
            GroupModifier.OneOrMore => $"ImmutableArray<{nestedStructName}>",
            GroupModifier.Optional => $"{nestedStructName}?",
            _ => nestedStructName,
        };

        // A nested-group field is a struct value, not a scalar node; Children() does
        // not yield it directly (deep per-group iteration is handled separately).
        model.Fields.Add(new GroupStructField(fieldName, fieldType, false));
    }

    /// <summary>
    /// Returns the field label whose expression is the same instance as
    /// <paramref name="element"/>, or null when the element is unlabelled.
    /// </summary>
    private static FieldLabelIR? FindLabel(ExpressionIR element, IReadOnlyList<FieldLabelIR> labels)
    {
        foreach(var label in labels)
        {
            if(ReferenceEquals(label.Expression, element))
            {
                return label;
            }
        }

        return null;
    }

    // ---------------------------------------------------------------
    //  Obsolete shim reconstruction (Tasks 5.1-5.4)
    // ---------------------------------------------------------------

    /// <summary>
    /// Reconstructs the flat-field properties the pre-wave-2 emitter produced for a
    /// struct-projected group and records them as <c>[Obsolete]</c> shims that
    /// forward to the new <c>Group{N}</c> data (Task 5.1). The reconstruction mirrors
    /// the group's field walk in <see cref="AddGroupStructField"/> so shim bodies
    /// reference the exact struct field names:
    /// <list type="bullet">
    ///   <item>Each labelled element (token, pure-token alternation, or rule
    ///         reference) reproduces its old scalar property -- named
    ///         <c>PascalCase(label)</c>, typed as the struct field's type, and
    ///         forwarding per the group modifier (Tasks 5.2, 5.4).</item>
    ///   <item>Each rule reference under a <c>*</c>/<c>+</c> group additionally
    ///         reproduces the old auto-generated list property (e.g.
    ///         <c>Multiplicatives</c>) as <c>ImmutableArray&lt;{Rule}Node&gt;</c>
    ///         projected from the group with LINQ (Task 5.3). Lists are a repetition
    ///         phenomenon, so <c>?</c> and no-modifier groups produce no list shim.</item>
    /// </list>
    /// Nested groups take a <c>Group{M}</c> struct field rather than a flat field, so
    /// they contribute no shim.
    /// </summary>
    private static void BuildGroupShims(AnonymousGroupInfo group, string groupPropName, List<ObsoleteShim> shims)
    {
        // Mirrors BuildGroupStruct's field-name allocation so the forwarding bodies
        // reference the same struct field names.
        var usedFieldNames = new HashSet<string>();

        // Keeps the reconstructed shim property names distinct from one another.
        var usedShimNames = new HashSet<string>();

        var isList = group.Modifier is GroupModifier.ZeroOrMore or GroupModifier.OneOrMore;

        foreach(var element in group.Sequence.Elements)
        {
            if(IsNestedGroupElement(element, group.Labels))
            {
                continue;
            }

            var label = FindLabel(element, group.Labels);
            if(label != null)
            {
                var fieldName = EnsureUnique(PascalCase(label.Label), usedFieldNames);
                var fieldType = FieldTypeForLabeledElement(element, label, fieldName);
                var scalarName = EnsureUnique(fieldName, usedShimNames);
                shims.Add(new ObsoleteShim(
                    groupPropName,
                    scalarName,
                    fieldType,
                    ScalarShimBody(groupPropName, fieldName, fieldType, group.Modifier)));

                if(isList && element is RuleRefIR labeledRuleRef)
                {
                    AddListShim(shims, usedShimNames, groupPropName, fieldName, labeledRuleRef.Rule.Name);
                }

                continue;
            }

            if(element is RuleRefIR ruleRef)
            {
                var fieldName = EnsureUnique(PascalCase(ruleRef.Rule.Name), usedFieldNames);
                if(isList)
                {
                    AddListShim(shims, usedShimNames, groupPropName, fieldName, ruleRef.Rule.Name);
                }
            }
        }
    }

    /// <summary>
    /// Records the old auto-generated list shim for a rule reference:
    /// <c>ImmutableArray&lt;{Rule}Node&gt; {Rule}s</c> projecting
    /// <paramref name="fieldName"/> from each group element.
    /// </summary>
    private static void AddListShim(
        List<ObsoleteShim> shims,
        HashSet<string> usedShimNames,
        string groupPropName,
        string fieldName,
        string ruleName)
    {
        var listName = EnsureUnique(PascalCase(ruleName) + "s", usedShimNames);
        var listType = $"ImmutableArray<{ruleName}Node>";
        shims.Add(new ObsoleteShim(
            groupPropName,
            listName,
            listType,
            $"{groupPropName}.Select(g => g.{fieldName}).ToImmutableArray()"));
    }

    /// <summary>
    /// Builds the forwarding expression body (without the trailing semicolon) for a
    /// scalar shim, according to the group's modifier (Tasks 5.2, 5.4). A
    /// <c>*</c>/<c>+</c> group returns the last element's field (preserving the old
    /// last-wins semantics); a <c>?</c> group returns the field of the optional group
    /// or a default. A non-nullable node type defaults to <c>default!</c> to satisfy
    /// nullable analysis; other types default to <c>default</c>.
    /// </summary>
    private static string ScalarShimBody(string groupPropName, string fieldName, string fieldType, GroupModifier modifier)
    {
        var defaultLiteral = IsScalarNodeType(fieldType) ? "default!" : "default";
        return modifier switch
        {
            GroupModifier.ZeroOrMore or GroupModifier.OneOrMore
                => $"{groupPropName}.Length > 0 ? {groupPropName}[^1].{fieldName} : {defaultLiteral}",
            GroupModifier.Optional
                => $"{groupPropName}?.{fieldName} ?? {defaultLiteral}",
            _ => $"{groupPropName}.{fieldName}",
        };
    }

    /// <summary>
    /// Returns true when a group element is a nested anonymous group (a
    /// repetition/optional wrapping a labelled sequence). Mirrors the nested-group
    /// detection in <see cref="AddGroupStructField"/>, which projects such elements
    /// as a nested <c>Group{M}</c> struct field rather than a flat field.
    /// </summary>
    private static bool IsNestedGroupElement(ExpressionIR element, IReadOnlyList<FieldLabelIR> groupLabels)
    {
        return element switch
        {
            ZeroOrMoreIR { Inner: SequenceIR nestedSeq } => HasLabelsInside(nestedSeq, groupLabels),
            OneOrMoreIR { Inner: SequenceIR nestedSeq } => HasLabelsInside(nestedSeq, groupLabels),
            OptionalIR { Inner: SequenceIR nestedSeq } => HasLabelsInside(nestedSeq, groupLabels),
            _ => false,
        };
    }

    /// <summary>
    /// Emits a group struct (and any nested group structs) as a <c>public struct</c>
    /// with public fields.
    /// </summary>
    private static void EmitGroupStruct(CodeWriter writer, GroupStructModel model)
    {
        writer.WriteBlock($"public struct {model.StructName}", () =>
        {
            foreach(var field in model.Fields)
            {
                writer.WriteLine($"public {field.TypeName} {field.Name};");
            }

            foreach(var nested in model.NestedStructs)
            {
                writer.WriteEmptyLine();
                EmitGroupStruct(writer, nested);
            }
        });
    }
}