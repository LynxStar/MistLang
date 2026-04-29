// Feature: class-inheritance, Property 5: Additive-only constraint (no redeclaration)
// **Validates: Requirements 4.3, 4.4**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying the additive-only constraint on class inheritance.
///
/// Property 5: For any derived class that declares a field or method with the same
/// name as a field or method in any of its ancestor classes, the semantic model
/// builder shall emit a MIST_MEMBER_REDECLARATION diagnostic error.
/// </summary>
public class AdditiveOnlyConstraintPropertyTests
{
    private readonly SemanticModelBuilder _builder = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token MakeToken(string lexeme, int start = 0)
    {
        return new Storm.Runtime.Token(
            kind: 0,
            span: new Storm.Runtime.SourceSpan(start, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

    private static MistCompilation MakeCompilation(string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    private (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModelWithDiagnostics(
        Storm.Runtime.SyntaxNode ast,
        string source = "")
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        var model = _builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    private static ClassDeclarationNode MakeClassDeclWithMembers(
        string name,
        string? baseClassName,
        ImmutableArray<MemberDeclarationNode> members)
    {
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = members,
        };

        if (baseClassName is not null)
        {
            classDecl.BaseClass = MakeToken(":");
            classDecl.BaseClassName = MakeToken(baseClassName);
        }

        return classDecl;
    }

    private static MemberDeclarationNode MakeFieldMember(string name)
    {
        return new FieldMemberMemberDeclarationNode
        {
            Field = new FieldDeclarationNode
            {
                Name = MakeToken(name),
                Type = new PrimitiveTypeRefTypeReferenceNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                },
            },
        };
    }

    private static MemberDeclarationNode MakeMethodMember(string name)
    {
        return new MethodMemberMemberDeclarationNode
        {
            Method = new MethodDeclarationNode
            {
                Name = MakeToken(name),
                ReturnType = new VoidTypeRefTypeReferenceNode(),
                Body = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
            },
        };
    }

    private static CompilationUnitNode MakeCompilationUnit(params ClassDeclarationNode[] classDecls)
    {
        var types = classDecls.Select(c =>
            (TypeDeclarationNode)new ClassTypeTypeDeclarationNode { ClassDeclaration = c })
            .ToImmutableArray();

        return new CompilationUnitNode { Types = types };
    }

    // ─── Test data models ────────────────────────────────────────────────

    /// <summary>
    /// Describes the kind of member being redeclared.
    /// </summary>
#pragma warning disable CA1034
    public enum MemberKind { Field, Method }

    /// <summary>
    /// Represents a redeclaration scenario: a class hierarchy where a derived class
    /// redeclares a member from an ancestor.
    /// </summary>
    public record RedeclarationScenario(
        ImmutableArray<ClassInfo> Classes,
        string DerivedClassName,
        string RedeclaredMemberName,
        string AncestorClassName,
        MemberKind AncestorMemberKind,
        MemberKind DerivedMemberKind)
    {
        public override string ToString()
        {
            var classesStr = string.Join("; ", Classes);
            return $"[{classesStr}] derived={DerivedClassName} redeclares {DerivedMemberKind} '{RedeclaredMemberName}' from {AncestorClassName} ({AncestorMemberKind})";
        }
    }

    /// <summary>
    /// Represents a class in a hierarchy with its fields and methods.
    /// </summary>
    public record ClassInfo(string Name, string? BaseName, ImmutableArray<string> Fields, ImmutableArray<string> Methods)
    {
        public override string ToString()
        {
            var baseStr = BaseName is not null ? $" : {BaseName}" : "";
            var fieldsStr = Fields.Length > 0 ? $" fields=[{string.Join(",", Fields)}]" : "";
            var methodsStr = Methods.Length > 0 ? $" methods=[{string.Join(",", Methods)}]" : "";
            return $"{Name}{baseStr}{fieldsStr}{methodsStr}";
        }
    }

    /// <summary>
    /// Represents a scenario where a derived class adds only new members (no redeclaration).
    /// </summary>
    public record AdditiveScenario(ImmutableArray<ClassInfo> Classes)
    {
        public override string ToString()
        {
            return $"[{string.Join("; ", Classes)}]";
        }
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ClassNamePool =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon"];

    private static readonly string[] FieldNamePool =
        ["width", "height", "depth", "weight", "speed", "count", "value", "size"];

    private static readonly string[] MethodNamePool =
        ["Run", "Stop", "Reset", "Update", "Process", "Compute", "Init", "Check"];

    /// <summary>
    /// Generates a redeclaration scenario: a hierarchy of 2-4 classes where the
    /// leaf (or an intermediate derived) class redeclares a field or method from
    /// an ancestor. The redeclared member can be field→field, method→method,
    /// field→method, or method→field.
    /// </summary>
    public static Gen<RedeclarationScenario> GenRedeclarationScenario()
    {
        return from depth in Gen.Choose(2, 4)
               from classNames in Gen.Shuffle(ClassNamePool).Select(n => n.Take(depth).ToArray())
               from scenario in BuildRedeclarationScenario(classNames)
               select scenario;
    }

    private static Gen<RedeclarationScenario> BuildRedeclarationScenario(string[] classNames)
    {
        var depth = classNames.Length;

        return from ancestorLevel in Gen.Choose(0, depth - 2)
               from redeclLevel in Gen.Choose(ancestorLevel + 1, depth - 1)
               from ancestorKind in Gen.Elements(MemberKind.Field, MemberKind.Method)
               from derivedKind in Gen.Elements(MemberKind.Field, MemberKind.Method)
               from redeclaredName in Gen.Elements(ancestorKind == MemberKind.Field ? FieldNamePool : MethodNamePool)
               from shuffledFields in Gen.Shuffle(FieldNamePool).Select(f => f.ToArray())
               from shuffledMethods in Gen.Shuffle(MethodNamePool).Select(m => m.ToArray())
               let classes = BuildHierarchyWithRedeclaration(
                   classNames, ancestorLevel, redeclLevel, ancestorKind, derivedKind,
                   redeclaredName, shuffledFields, shuffledMethods)
               where classes.Length > 0
               select new RedeclarationScenario(
                   classes,
                   classNames[redeclLevel],
                   redeclaredName,
                   classNames[ancestorLevel],
                   ancestorKind,
                   derivedKind);
    }

    private static ImmutableArray<ClassInfo> BuildHierarchyWithRedeclaration(
        string[] classNames,
        int ancestorLevel,
        int redeclLevel,
        MemberKind ancestorKind,
        MemberKind derivedKind,
        string redeclaredName,
        string[] shuffledFields,
        string[] shuffledMethods)
    {
        var classes = new List<ClassInfo>();
        var usedNames = new HashSet<string>();
        var fieldIdx = 0;
        var methodIdx = 0;

        // Reserve the redeclared name so we don't accidentally use it elsewhere
        usedNames.Add(redeclaredName);

        for (var i = 0; i < classNames.Length; i++)
        {
            var fields = new List<string>();
            var methods = new List<string>();

            // Add the redeclared member at the ancestor level
            if (i == ancestorLevel)
            {
                if (ancestorKind == MemberKind.Field)
                    fields.Add(redeclaredName);
                else
                    methods.Add(redeclaredName);
            }

            // Add the redeclared member at the derived level (this is the redeclaration)
            if (i == redeclLevel)
            {
                if (derivedKind == MemberKind.Field)
                    fields.Add(redeclaredName);
                else
                    methods.Add(redeclaredName);
            }

            // Add 0-1 unique fields at this level
            if (i != ancestorLevel || ancestorKind != MemberKind.Field)
            {
                // Try to add one unique field
                while (fieldIdx < shuffledFields.Length)
                {
                    var candidate = shuffledFields[fieldIdx++];
                    if (usedNames.Add(candidate))
                    {
                        fields.Add(candidate);
                        break;
                    }
                }
            }

            // Add 0-1 unique methods at this level
            if (i != ancestorLevel || ancestorKind != MemberKind.Method)
            {
                while (methodIdx < shuffledMethods.Length)
                {
                    var candidate = shuffledMethods[methodIdx++];
                    if (usedNames.Add(candidate))
                    {
                        methods.Add(candidate);
                        break;
                    }
                }
            }

            var baseName = i > 0 ? classNames[i - 1] : null;
            classes.Add(new ClassInfo(
                classNames[i],
                baseName,
                fields.ToImmutableArray(),
                methods.ToImmutableArray()));
        }

        return classes.ToImmutableArray();
    }

    /// <summary>
    /// Generates an additive-only scenario: a hierarchy of 2-4 classes where all
    /// member names are unique across the entire chain (no redeclarations).
    /// </summary>
    public static Gen<AdditiveScenario> GenAdditiveScenario()
    {
        return from depth in Gen.Choose(2, 4)
               from classNames in Gen.Shuffle(ClassNamePool).Select(n => n.Take(depth).ToArray())
               from scenario in BuildAdditiveScenario(classNames)
               select scenario;
    }

    private static Gen<AdditiveScenario> BuildAdditiveScenario(string[] classNames)
    {
        var depth = classNames.Length;

        return from shuffledFields in Gen.Shuffle(FieldNamePool).Select(f => f.ToArray())
               from shuffledMethods in Gen.Shuffle(MethodNamePool).Select(m => m.ToArray())
               let classes = BuildAdditiveHierarchy(classNames, shuffledFields, shuffledMethods)
               where classes.Length > 0
               select new AdditiveScenario(classes);
    }

    private static ImmutableArray<ClassInfo> BuildAdditiveHierarchy(
        string[] classNames,
        string[] shuffledFields,
        string[] shuffledMethods)
    {
        var classes = new List<ClassInfo>();
        var usedNames = new HashSet<string>();
        var fieldIdx = 0;
        var methodIdx = 0;

        for (var i = 0; i < classNames.Length; i++)
        {
            var fields = new List<string>();
            var methods = new List<string>();

            // Add 1-2 unique fields at this level
            var fieldCount = Math.Min(2, shuffledFields.Length - fieldIdx);
            if (fieldCount > 0) fieldCount = 1; // Keep it simple: 1 field per level
            for (var f = 0; f < fieldCount; f++)
            {
                while (fieldIdx < shuffledFields.Length)
                {
                    var candidate = shuffledFields[fieldIdx++];
                    if (usedNames.Add(candidate))
                    {
                        fields.Add(candidate);
                        break;
                    }
                }
            }

            // Add 1 unique method at this level
            while (methodIdx < shuffledMethods.Length)
            {
                var candidate = shuffledMethods[methodIdx++];
                if (usedNames.Add(candidate))
                {
                    methods.Add(candidate);
                    break;
                }
            }

            var baseName = i > 0 ? classNames[i - 1] : null;
            classes.Add(new ClassInfo(
                classNames[i],
                baseName,
                fields.ToImmutableArray(),
                methods.ToImmutableArray()));
        }

        return classes.ToImmutableArray();
    }

    /// <summary>FsCheck Arbitrary registration for RedeclarationScenario.</summary>
    public static Arbitrary<RedeclarationScenario> ArbitraryRedeclarationScenario() =>
        GenRedeclarationScenario().ToArbitrary();

    /// <summary>FsCheck Arbitrary registration for AdditiveScenario.</summary>
    public static Arbitrary<AdditiveScenario> ArbitraryAdditiveScenario() =>
        GenAdditiveScenario().ToArbitrary();

    // ─── AST construction ────────────────────────────────────────────────

    /// <summary>
    /// Builds a CompilationUnitNode from a list of ClassInfo records.
    /// </summary>
    private static CompilationUnitNode BuildAst(ImmutableArray<ClassInfo> classes)
    {
        var classDecls = new List<ClassDeclarationNode>();

        foreach (var cls in classes)
        {
            var members = new List<MemberDeclarationNode>();

            foreach (var field in cls.Fields)
            {
                members.Add(MakeFieldMember(field));
            }

            foreach (var method in cls.Methods)
            {
                members.Add(MakeMethodMember(method));
            }

            var classDecl = MakeClassDeclWithMembers(cls.Name, cls.BaseName, members.ToImmutableArray());
            classDecls.Add(classDecl);
        }

        return MakeCompilationUnit(classDecls.ToArray());
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 5a: For any derived class that redeclares a field or method with the
    /// same name as a field or method in any of its ancestor classes, the semantic
    /// model builder emits a MIST_MEMBER_REDECLARATION diagnostic mentioning the
    /// derived class, the member name, and the ancestor class.
    ///
    /// **Validates: Requirements 4.3, 4.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(AdditiveOnlyConstraintPropertyTests) }, MaxTest = 100)]
    public bool Redeclaration_EmitsMemberRedeclarationDiagnostic(RedeclarationScenario scenario)
    {
        var ast = BuildAst(scenario.Classes);
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Assert: at least one MIST_MEMBER_REDECLARATION diagnostic is emitted
        // that mentions the derived class and the redeclared member name
        var redeclDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_MEMBER_REDECLARATION"
                        && d.Message.Contains(scenario.RedeclaredMemberName)
                        && d.Message.Contains(scenario.DerivedClassName))
            .ToList();

        return redeclDiags.Count > 0;
    }

    /// <summary>
    /// Property 5b: For any class hierarchy where all member names are unique across
    /// the entire inheritance chain (no redeclarations), the semantic model builder
    /// does NOT emit any MIST_MEMBER_REDECLARATION diagnostic.
    ///
    /// **Validates: Requirements 4.3, 4.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(AdditiveOnlyConstraintPropertyTests) }, MaxTest = 100)]
    public bool AdditiveOnly_NoRedeclarationDiagnostic(AdditiveScenario scenario)
    {
        var ast = BuildAst(scenario.Classes);
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        // Assert: no MIST_MEMBER_REDECLARATION diagnostics are emitted
        var redeclDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_MEMBER_REDECLARATION")
            .ToList();

        return redeclDiags.Count == 0;
    }
}
