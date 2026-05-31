// Feature: traits, Property 9: Trait and interface coexistence
// **Validates: Requirements 11.1, 11.2, 11.3**

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
using Mist.Tests.Generators;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying trait and interface coexistence.
///
/// Property 9: For any type that simultaneously implements one or more interfaces
/// (nominal typing) and satisfies one or more traits (structural typing), the
/// compiler SHALL process both independently without conflict. Satisfying a trait
/// SHALL NOT imply implementing an interface with the same member signatures, and
/// implementing an interface SHALL NOT imply satisfying a trait with the same
/// member signatures.
/// </summary>
public class TraitInterfaceCoexistencePropertyTests
{
    private readonly SemanticModelBuilder _builder = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme) =>
        MistTypeGenerator.MakeToken(lexeme);

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
        var model = _builder.Build(
            compilation,
            ImmutableArray.Create(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    private static TypeReferenceNode MakePrimitiveTypeRef(string typeName)
    {
        return typeName switch
        {
            "int" => new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            } },
            "string" => new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new StringTypePrimitiveTypeNode(),
            } },
            "bool" => new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new BoolTypePrimitiveTypeNode(),
            } },
            _ => new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            } },
        };
    }

    private static BaseListNode MakeBaseList(string name)
    {
        var baseTypeRef = new BaseTypeRefNode { Name = Tok(name) };
        return new BaseListNode
        {
            First = baseTypeRef,
            Rest = baseTypeRef,
            BaseTypeRefs = ImmutableArray<BaseTypeRefNode>.Empty,
        };
    }

    private static CompilationUnitNode MakeCompilationUnit(params TopLevelItemNode[] items) =>
        new() { Items = items.ToImmutableArray() };

    private static TopLevelItemNode MakeClassTypeItem(ClassDeclarationNode classDecl) =>
        new TypeItemTopLevelItemNode
        {
            TypeDeclaration = new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl },
        };

    private static TopLevelItemNode MakeTraitTypeItem(TraitDeclarationNode traitDecl) =>
        new TypeItemTopLevelItemNode
        {
            TypeDeclaration = new TraitTypeTypeDeclarationNode { TraitDeclaration = traitDecl },
        };

    private static TopLevelItemNode MakeInterfaceTypeItem(InterfaceDeclarationNode ifaceDecl) =>
        new TypeItemTopLevelItemNode
        {
            TypeDeclaration = new InterfaceTypeTypeDeclarationNode { InterfaceDeclaration = ifaceDecl },
        };

    // ─── AST construction helpers ────────────────────────────────────────

    /// <summary>
    /// Creates a class with public field members that implements an interface.
    /// </summary>
    private static ClassDeclarationNode MakeClassWithFieldsAndInterface(
        string className,
        string interfaceName,
        params (string typeName, string fieldName)[] fields)
    {
        var members = new List<MemberDeclarationNode>();

        foreach (var f in fields)
        {
            members.Add(new FieldMemberMemberDeclarationNode
            {
                Field = new FieldDeclarationNode
                {
                    Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                    Type = MakePrimitiveTypeRef(f.typeName),
                    Name = Tok(f.fieldName),
                },
            });
        }

        var decl = new ClassDeclarationNode
        {
            Name = Tok(className),
            Members = members.ToImmutableArray(),
        };

        var baseList = MakeBaseList(interfaceName);
        decl.BaseClause = baseList;
        decl.BaseList = baseList;

        return decl;
    }

    /// <summary>
    /// Creates a class with public field members and public methods, implementing an interface.
    /// </summary>
    private static ClassDeclarationNode MakeClassWithFieldsMethodsAndInterface(
        string className,
        string interfaceName,
        (string typeName, string fieldName)[] fields,
        (string returnType, string methodName)[] methods)
    {
        var members = new List<MemberDeclarationNode>();

        foreach (var f in fields)
        {
            members.Add(new FieldMemberMemberDeclarationNode
            {
                Field = new FieldDeclarationNode
                {
                    Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                    Type = MakePrimitiveTypeRef(f.typeName),
                    Name = Tok(f.fieldName),
                },
            });
        }

        foreach (var m in methods)
        {
            members.Add(new MethodMemberMemberDeclarationNode
            {
                Method = new MethodDeclarationNode
                {
                    Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                    ReturnType = m.returnType == "void"
                        ? new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() }
                        : MakePrimitiveTypeRef(m.returnType),
                    Name = Tok(m.methodName),
                    Parameters = null,
                    Body = new MethodBlockBodyMethodBodyNode
                    {
                        Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
                    },
                },
            });
        }

        var decl = new ClassDeclarationNode
        {
            Name = Tok(className),
            Members = members.ToImmutableArray(),
        };

        var baseList = MakeBaseList(interfaceName);
        decl.BaseClause = baseList;
        decl.BaseList = baseList;

        return decl;
    }

    /// <summary>
    /// Creates a class with public fields only (no interface).
    /// </summary>
    private static ClassDeclarationNode MakeClassWithPublicFields(
        string className, params (string typeName, string fieldName)[] fields)
    {
        var members = fields.Select(f =>
            (MemberDeclarationNode)new FieldMemberMemberDeclarationNode
            {
                Field = new FieldDeclarationNode
                {
                    Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                    Type = MakePrimitiveTypeRef(f.typeName),
                    Name = Tok(f.fieldName),
                },
            }).ToImmutableArray();

        return new ClassDeclarationNode { Name = Tok(className), Members = members };
    }

    /// <summary>
    /// Creates a trait with field members.
    /// </summary>
    private static TraitDeclarationNode MakeTraitWithFields(
        string traitName, params (string typeName, string fieldName)[] fields)
    {
        var members = fields.Select(f =>
            (TraitMemberDeclarationNode)new TraitFieldMemberTraitMemberDeclarationNode
            {
                Field = new TraitFieldDeclarationNode
                {
                    Type = MakePrimitiveTypeRef(f.typeName),
                    Name = Tok(f.fieldName),
                },
            }).ToImmutableArray();

        return new TraitDeclarationNode { Name = Tok(traitName), Members = members };
    }

    /// <summary>
    /// Creates an interface with method members.
    /// </summary>
    private static InterfaceDeclarationNode MakeInterfaceWithMethods(
        string interfaceName,
        params (string returnType, string methodName)[] methods)
    {
        var members = methods.Select(m =>
            (InterfaceMemberDeclarationNode)new InterfaceMethodMemberInterfaceMemberDeclarationNode
            {
                Method = new InterfaceMethodDeclarationNode
                {
                    Name = Tok(m.methodName),
                    ReturnType = m.returnType == "void"
                        ? new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() }
                        : MakePrimitiveTypeRef(m.returnType),
                    Parameters = null,
                },
            }).ToImmutableArray();

        return new InterfaceDeclarationNode
        {
            Name = Tok(interfaceName),
            Members = members,
        };
    }

    /// <summary>
    /// Finds a type in the semantic model by name and kind.
    /// </summary>
    private static IType? FindType(ISemanticModel model, string name, SymbolKind kind)
    {
        return model.AllSymbols
            .FirstOrDefault(s => s.Name == name && s.Kind == kind)
            ?.DeclaredType;
    }

    /// <summary>
    /// Finds a TraitType in the semantic model by name.
    /// </summary>
    private static TraitType? FindTrait(ISemanticModel model, string name)
    {
        return model.AllSymbols
            .FirstOrDefault(s => s.Name == name && s.Kind == SymbolKind.Trait)
            ?.DeclaredType as TraitType;
    }

    /// <summary>
    /// Finds an InterfaceType in the semantic model by name.
    /// </summary>
    private static InterfaceType? FindInterface(ISemanticModel model, string name)
    {
        return model.AllSymbols
            .FirstOrDefault(s => s.Name == name && s.Kind == SymbolKind.Interface)
            ?.DeclaredType as InterfaceType;
    }

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] ClassNames =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon"];

    private static readonly string[] TraitNames =
        ["Printable", "Drawable", "Serializable", "Hashable", "Comparable"];

    private static readonly string[] InterfaceNames =
        ["IRunnable", "IDisposable", "ICloneable", "IFormattable", "IEquatable"];

    private static readonly string[] FieldNames =
        ["value", "count", "size", "width", "height"];

    private static readonly string[] MethodNames =
        ["DoWork", "Process", "Execute", "Transform", "Validate"];

    private static readonly string[] PrimitiveTypes = ["int", "string", "bool"];

    // ═══════════════════════════════════════════════════════════════════════
    // Scenario types
    // ═══════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034

    /// <summary>
    /// A scenario where a class implements an interface AND structurally satisfies
    /// a trait simultaneously. The interface methods and trait fields are independent.
    /// </summary>
    public record CoexistenceScenario(
        string ClassName,
        string InterfaceName,
        string TraitName,
        ImmutableArray<(string ReturnType, string MethodName)> InterfaceMethods,
        ImmutableArray<(string FieldType, string FieldName)> TraitFields)
    {
        public override string ToString()
        {
            var methods = string.Join(", ", InterfaceMethods.Select(m => $"{m.ReturnType} {m.MethodName}()"));
            var fields = string.Join(", ", TraitFields.Select(f => $"{f.FieldType} {f.FieldName}"));
            return $"class {ClassName} : {InterfaceName} {{ {methods} }} satisfies trait {TraitName} {{ {fields} }}";
        }
    }

    /// <summary>
    /// A scenario where a trait and an interface have the same member signatures
    /// (same method names). A class implements the interface but does NOT structurally
    /// satisfy the trait (because trait satisfaction is independent).
    /// </summary>
    public record SameSignatureIndependenceScenario(
        string ClassName,
        string InterfaceName,
        string TraitName,
        ImmutableArray<(string FieldType, string FieldName)> SharedFields)
    {
        public override string ToString()
        {
            var fields = string.Join(", ", SharedFields.Select(f => $"{f.FieldType} {f.FieldName}"));
            return $"interface {InterfaceName} and trait {TraitName} share {{ {fields} }}; " +
                   $"class {ClassName} implements interface only";
        }
    }

    /// <summary>
    /// A scenario where a class satisfies a trait but does NOT implement an interface
    /// with the same member signatures. Trait satisfaction does not imply interface
    /// implementation.
    /// </summary>
    public record TraitDoesNotImplyInterfaceScenario(
        string ClassName,
        string InterfaceName,
        string TraitName,
        ImmutableArray<(string ReturnType, string MethodName)> SharedMethods)
    {
        public override string ToString()
        {
            var methods = string.Join(", ", SharedMethods.Select(m => $"{m.ReturnType} {m.MethodName}()"));
            return $"class {ClassName} satisfies trait {TraitName} {{ {methods} }} but does NOT implement {InterfaceName}";
        }
    }

#pragma warning restore CA1034

    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a scenario where a class implements an interface (with methods)
    /// AND structurally satisfies a trait (with fields). The interface methods and
    /// trait fields are independent (different member kinds).
    /// </summary>
    private static Gen<CoexistenceScenario> GenCoexistenceScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from interfaceName in Gen.Elements(InterfaceNames)
               from traitName in Gen.Elements(TraitNames)
               from methodCount in Gen.Choose(1, 3)
               from fieldCount in Gen.Choose(1, 3)
               from shuffledMethods in Gen.Shuffle(MethodNames)
               from methodReturnTypes in Gen.Elements("void", "int", "string").ArrayOf(methodCount)
               from shuffledFields in Gen.Shuffle(FieldNames)
               from fieldTypes in Gen.Elements(PrimitiveTypes).ArrayOf(fieldCount)
               let methods = shuffledMethods.Take(methodCount)
                   .Zip(methodReturnTypes, (name, ret) => (ret, name))
                   .ToImmutableArray()
               let fields = shuffledFields.Take(fieldCount)
                   .Zip(fieldTypes, (name, type) => (type, name))
                   .ToImmutableArray()
               select new CoexistenceScenario(className, interfaceName, traitName, methods, fields);
    }

    /// <summary>
    /// Generates a scenario where a trait and an interface have the same field-like
    /// member signatures. A class implements the interface (nominal) but does NOT
    /// have the matching public fields, so it should NOT satisfy the trait.
    /// This tests that implementing an interface does NOT imply satisfying a trait.
    /// </summary>
    private static Gen<SameSignatureIndependenceScenario> GenSameSignatureIndependenceScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from interfaceName in Gen.Elements(InterfaceNames)
               from traitName in Gen.Elements(TraitNames)
               from fieldCount in Gen.Choose(1, 3)
               from shuffledFields in Gen.Shuffle(FieldNames)
               from fieldTypes in Gen.Elements(PrimitiveTypes).ArrayOf(fieldCount)
               let fields = shuffledFields.Take(fieldCount)
                   .Zip(fieldTypes, (name, type) => (type, name))
                   .ToImmutableArray()
               select new SameSignatureIndependenceScenario(className, interfaceName, traitName, fields);
    }

    /// <summary>
    /// Generates a scenario where a class has public methods that satisfy a trait
    /// (with method members) but does NOT nominally implement an interface with
    /// the same method signatures. This tests that satisfying a trait does NOT
    /// imply implementing an interface.
    /// </summary>
    private static Gen<TraitDoesNotImplyInterfaceScenario> GenTraitDoesNotImplyInterfaceScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from interfaceName in Gen.Elements(InterfaceNames)
               from traitName in Gen.Elements(TraitNames)
               from methodCount in Gen.Choose(1, 3)
               from shuffledMethods in Gen.Shuffle(MethodNames)
               from returnTypes in Gen.Elements("void", "int", "string").ArrayOf(methodCount)
               let methods = shuffledMethods.Take(methodCount)
                   .Zip(returnTypes, (name, ret) => (ret, name))
                   .ToImmutableArray()
               select new TraitDoesNotImplyInterfaceScenario(className, interfaceName, traitName, methods);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property tests
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 9a: For any class that simultaneously implements an interface
    /// (nominal typing) and structurally satisfies a trait (structural typing),
    /// the compiler SHALL process both independently without conflict. The class
    /// should satisfy the trait in the TraitSatisfactionMap AND produce no
    /// interface satisfaction errors (MIST_SM_0022).
    ///
    /// **Validates: Requirements 11.1**
    /// </summary>
    [Fact]
    public void InterfaceAndTrait_CoexistWithoutConflict()
    {
        var property = Prop.ForAll(
            GenCoexistenceScenario().ToArbitrary(),
            scenario =>
            {
                // Build: interface with methods, trait with fields, class implementing
                // the interface AND having public fields matching the trait.
                var ifaceDecl = MakeInterfaceWithMethods(
                    scenario.InterfaceName,
                    scenario.InterfaceMethods.Select(m => (m.ReturnType, m.MethodName)).ToArray());

                var traitDecl = MakeTraitWithFields(
                    scenario.TraitName,
                    scenario.TraitFields.Select(f => (f.FieldType, f.FieldName)).ToArray());

                // Class implements interface AND has public fields matching trait
                var classDecl = MakeClassWithFieldsMethodsAndInterface(
                    scenario.ClassName,
                    scenario.InterfaceName,
                    scenario.TraitFields.Select(f => (f.FieldType, f.FieldName)).ToArray(),
                    scenario.InterfaceMethods.Select(m => (m.ReturnType, m.MethodName)).ToArray());

                var ast = MakeCompilationUnit(
                    MakeInterfaceTypeItem(ifaceDecl),
                    MakeTraitTypeItem(traitDecl),
                    MakeClassTypeItem(classDecl));

                var (model, diagnostics) = BuildModelWithDiagnostics(ast);

                // Find the types
                var classType = FindType(model, scenario.ClassName, SymbolKind.Class);
                var traitType = FindTrait(model, scenario.TraitName);

                if (classType is null || traitType is null)
                    return false.ToProperty().Label("Class or trait type not found");

                // The class should satisfy the trait
                var satisfiesTrait = model.TraitSatisfactionMap.Satisfies(classType, traitType);

                // No interface satisfaction errors for this class
                var interfaceErrors = diagnostics
                    .Where(d => d.Id.Value == "MIST_SM_0022"
                                && d.Message.Contains(scenario.ClassName))
                    .ToList();

                // No trait-related errors
                var traitErrors = diagnostics
                    .Where(d => d.Id.Value == "MIST_TRAIT_UNSATISFIED"
                                && d.Message.Contains(scenario.ClassName))
                    .ToList();

                return (satisfiesTrait && interfaceErrors.Count == 0 && traitErrors.Count == 0)
                    .ToProperty()
                    .Label($"Expected {scenario.ClassName} to satisfy trait {scenario.TraitName} " +
                           $"AND implement {scenario.InterfaceName} without conflict. " +
                           $"TraitSatisfied={satisfiesTrait}, InterfaceErrors={interfaceErrors.Count}, " +
                           $"TraitErrors={traitErrors.Count}");
            });

        property.Check(Config.Default.WithMaxTest(100));
    }

    /// <summary>
    /// Property 9b: For any class that implements an interface (nominal) but does
    /// NOT have public members matching a trait with the same member signatures,
    /// implementing the interface SHALL NOT imply satisfying the trait. The
    /// TraitSatisfactionMap SHALL NOT report the class as satisfying the trait.
    ///
    /// **Validates: Requirements 11.2**
    /// </summary>
    [Fact]
    public void InterfaceImplementation_DoesNotImplyTraitSatisfaction()
    {
        var property = Prop.ForAll(
            GenSameSignatureIndependenceScenario().ToArbitrary(),
            scenario =>
            {
                // Build: interface with methods that have the same names as trait fields.
                // The interface has methods; the trait has fields with the same names.
                // The class implements the interface (has the methods) but does NOT
                // have public fields matching the trait.
                var ifaceMethods = scenario.SharedFields
                    .Select(f => (InterfaceMemberDeclarationNode)new InterfaceMethodMemberInterfaceMemberDeclarationNode
                    {
                        Method = new InterfaceMethodDeclarationNode
                        {
                            Name = Tok(f.FieldName),
                            ReturnType = MakePrimitiveTypeRef(f.FieldType),
                            Parameters = null,
                        },
                    }).ToImmutableArray();

                var ifaceDecl = new InterfaceDeclarationNode
                {
                    Name = Tok(scenario.InterfaceName),
                    Members = ifaceMethods,
                };

                // Trait with field members using the same names
                var traitDecl = MakeTraitWithFields(
                    scenario.TraitName,
                    scenario.SharedFields.Select(f => (f.FieldType, f.FieldName)).ToArray());

                // Class implements the interface (has methods matching interface)
                // but does NOT have public fields matching the trait fields.
                var classMembers = scenario.SharedFields
                    .Select(f => (MemberDeclarationNode)new MethodMemberMemberDeclarationNode
                    {
                        Method = new MethodDeclarationNode
                        {
                            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                            ReturnType = MakePrimitiveTypeRef(f.FieldType),
                            Name = Tok(f.FieldName),
                            Parameters = null,
                            Body = new MethodBlockBodyMethodBodyNode
                            {
                                Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
                            },
                        },
                    }).ToImmutableArray();

                var classDecl = new ClassDeclarationNode
                {
                    Name = Tok(scenario.ClassName),
                    Members = classMembers,
                };
                var baseList = MakeBaseList(scenario.InterfaceName);
                classDecl.BaseClause = baseList;
                classDecl.BaseList = baseList;

                var ast = MakeCompilationUnit(
                    MakeInterfaceTypeItem(ifaceDecl),
                    MakeTraitTypeItem(traitDecl),
                    MakeClassTypeItem(classDecl));

                var (model, diagnostics) = BuildModelWithDiagnostics(ast);

                var classType = FindType(model, scenario.ClassName, SymbolKind.Class);
                var traitType = FindTrait(model, scenario.TraitName);

                if (classType is null || traitType is null)
                    return false.ToProperty().Label("Class or trait type not found");

                // The class should NOT satisfy the trait — implementing the interface
                // does not imply trait satisfaction. The trait has field members, and
                // the class only has methods (even with the same names).
                var doesNotSatisfyTrait = !model.TraitSatisfactionMap.Satisfies(classType, traitType);

                return doesNotSatisfyTrait
                    .ToProperty()
                    .Label($"Expected {scenario.ClassName} NOT to satisfy trait {scenario.TraitName} " +
                           $"even though it implements interface {scenario.InterfaceName} with same member names");
            });

        property.Check(Config.Default.WithMaxTest(100));
    }

    /// <summary>
    /// Property 9c: For any class that structurally satisfies a trait (has public
    /// methods matching trait method members) but does NOT nominally implement an
    /// interface with the same method signatures, satisfying the trait SHALL NOT
    /// imply implementing the interface. The compiler SHALL emit MIST_SM_0022
    /// diagnostics for the missing interface methods if the class declares it
    /// implements the interface but doesn't.
    ///
    /// In this test, the class does NOT declare `: IFoo`, so there should be no
    /// interface satisfaction errors at all — the class simply isn't related to
    /// the interface. The trait satisfaction should be independent.
    ///
    /// **Validates: Requirements 11.2, 11.3**
    /// </summary>
    [Fact]
    public void TraitSatisfaction_DoesNotImplyInterfaceImplementation()
    {
        var property = Prop.ForAll(
            GenTraitDoesNotImplyInterfaceScenario().ToArbitrary(),
            scenario =>
            {
                // Build: trait with method members, interface with same method signatures.
                // Class has public methods matching both, but only structurally satisfies
                // the trait — it does NOT declare `: IFoo`.

                // Trait with method members
                var traitMembers = scenario.SharedMethods
                    .Select(m => (TraitMemberDeclarationNode)new TraitMethodMemberTraitMemberDeclarationNode
                    {
                        Method = new TraitMethodDeclarationNode
                        {
                            ReturnType = m.ReturnType == "void"
                                ? new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() }
                                : MakePrimitiveTypeRef(m.ReturnType),
                            Name = Tok(m.MethodName),
                            Parameters = null,
                        },
                    }).ToImmutableArray();

                var traitDecl = new TraitDeclarationNode
                {
                    Name = Tok(scenario.TraitName),
                    Members = traitMembers,
                };

                // Interface with same method signatures
                var ifaceDecl = MakeInterfaceWithMethods(
                    scenario.InterfaceName,
                    scenario.SharedMethods.Select(m => (m.ReturnType, m.MethodName)).ToArray());

                // Class with public methods matching both trait and interface,
                // but NOT declaring `: IFoo`
                var classMembers = scenario.SharedMethods
                    .Select(m => (MemberDeclarationNode)new MethodMemberMemberDeclarationNode
                    {
                        Method = new MethodDeclarationNode
                        {
                            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                            ReturnType = m.ReturnType == "void"
                                ? new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() }
                                : MakePrimitiveTypeRef(m.ReturnType),
                            Name = Tok(m.MethodName),
                            Parameters = null,
                            Body = new MethodBlockBodyMethodBodyNode
                            {
                                Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
                            },
                        },
                    }).ToImmutableArray();

                var classDecl = new ClassDeclarationNode
                {
                    Name = Tok(scenario.ClassName),
                    Members = classMembers,
                };
                // NOTE: No BaseClause/BaseList — class does NOT implement the interface

                var ast = MakeCompilationUnit(
                    MakeInterfaceTypeItem(ifaceDecl),
                    MakeTraitTypeItem(traitDecl),
                    MakeClassTypeItem(classDecl));

                var (model, diagnostics) = BuildModelWithDiagnostics(ast);

                var classType = FindType(model, scenario.ClassName, SymbolKind.Class);
                var traitType = FindTrait(model, scenario.TraitName);
                var ifaceType = FindInterface(model, scenario.InterfaceName);

                if (classType is null || traitType is null || ifaceType is null)
                    return false.ToProperty().Label("Class, trait, or interface type not found");

                // The class SHOULD satisfy the trait (structural match)
                var satisfiesTrait = model.TraitSatisfactionMap.Satisfies(classType, traitType);

                // The class should NOT be in the interface's implementors list.
                // Since the class doesn't declare `: IFoo`, the Interfaces list
                // on the ClassType should not contain the interface.
                var classTypeObj = classType as ClassType;
                var implementsInterface = classTypeObj?.Interfaces
                    .Any(i => i is InterfaceType it && it.DisplayName == scenario.InterfaceName) ?? false;

                return (satisfiesTrait && !implementsInterface)
                    .ToProperty()
                    .Label($"Expected {scenario.ClassName} to satisfy trait {scenario.TraitName} " +
                           $"but NOT implement interface {scenario.InterfaceName}. " +
                           $"TraitSatisfied={satisfiesTrait}, ImplementsInterface={implementsInterface}");
            });

        property.Check(Config.Default.WithMaxTest(100));
    }
}
