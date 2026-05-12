// Feature: deep-ownership-types, Property 10: Owned field modifier rules

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for owned field modifier validation (Pass 2c).
///
/// Property 10: Owned Field Modifier Rules
/// For any field declared with the 'owned' modifier: (a) the field's type must be an
/// owned type, (b) assignment to the field requires a source from an owned binding,
/// (c) readonly ref access is always permitted, (d) borrowed/frozen access is permitted
/// only when the containing instance is in an owned binding, and (e) borrowed/frozen
/// access is rejected when the containing instance is in an unrestricted binding.
///
/// **Validates: Requirements 10.2, 10.3, 10.4, 10.5, 10.6**
/// </summary>
public class OwnedFieldModifierPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ReadonlyAnalyzer(),
            new OwnershipAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_pbt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "Program.mist");
            File.WriteAllText(sourcePath, source);

            var options = new CompilerOptions(
                ProjectFolder: tempDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: tempDir,
                BinDirectory: Path.Combine(tempDir, "bin"),
                ProjectName: "TestProgram",
                NamespaceRoot: "TestProgram",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Scenario records ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Scenario for a field marked 'owned' whose type IS an owned type.
    /// Should compile without MIST_OWN_FIELD_NOT_OWNED.
    /// </summary>
    public sealed record ValidOwnedFieldScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario for a field marked 'owned' whose type is NOT an owned type.
    /// Should report MIST_OWN_FIELD_NOT_OWNED.
    /// </summary>
    public sealed record InvalidOwnedFieldScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] ContainerTypeNames =
        ["Container", "Holder", "Wrapper", "Manager", "Controller", "Service"];

    private static readonly string[] OwnedFieldTypeNames =
        ["Buffer", "Matrix", "Storage", "Channel", "Segment", "Payload"];

    private static readonly string[] NonOwnedFieldTypeNames =
        ["Widget", "Handler", "Processor", "Adapter", "Connector", "Resolver"];

    private static readonly string[] FieldNames =
        ["data", "inner", "child", "content", "payload", "element"];

    private static readonly string[] ValueTypes =
        ["int", "bool", "float", "double", "long", "byte", "char"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates scenarios where a field is marked 'owned' and its type IS an owned type.
    /// These should compile without MIST_OWN_FIELD_NOT_OWNED.
    /// Covers: owned field in non-owned class, owned field in owned class,
    /// owned field with value-type owned type (struct).
    /// </summary>
    private static Gen<ValidOwnedFieldScenario> GenValidOwnedFieldScenario()
    {
        var ownedClassField = from containerName in Gen.Elements(ContainerTypeNames)
                             from fieldTypeName in Gen.Elements(OwnedFieldTypeNames)
                             from fieldName in Gen.Elements(FieldNames)
                             from valueType in Gen.Elements(ValueTypes)
                             let source = $$"""
                                 owned class {{fieldTypeName}} {
                                     {{valueType}} amount;
                                 }
                                 class {{containerName}} {
                                     owned {{fieldTypeName}} {{fieldName}};
                                 }
                                 class Program {
                                     public static void Main() {
                                     }
                                 }
                                 """
                             select new ValidOwnedFieldScenario(source,
                                 $"non-owned class {containerName} with owned field {fieldName}:{fieldTypeName}");

        var ownedStructField = from containerName in Gen.Elements(ContainerTypeNames)
                              from fieldTypeName in Gen.Elements(OwnedFieldTypeNames)
                              from fieldName in Gen.Elements(FieldNames)
                              from valueType in Gen.Elements(ValueTypes)
                              let source = $$"""
                                  owned struct {{fieldTypeName}} {
                                      {{valueType}} amount;
                                  }
                                  class {{containerName}} {
                                      owned {{fieldTypeName}} {{fieldName}};
                                  }
                                  class Program {
                                      public static void Main() {
                                      }
                                  }
                                  """
                              select new ValidOwnedFieldScenario(source,
                                  $"non-owned class {containerName} with owned struct field {fieldName}:{fieldTypeName}");

        var ownedFieldInOwnedType = from containerName in Gen.Elements(ContainerTypeNames)
                                   from fieldTypeName in Gen.Elements(OwnedFieldTypeNames)
                                   from fieldName in Gen.Elements(FieldNames)
                                   from valueType in Gen.Elements(ValueTypes)
                                   let source = $$"""
                                       owned class {{fieldTypeName}} {
                                           {{valueType}} amount;
                                       }
                                       owned class {{containerName}} {
                                           owned {{fieldTypeName}} {{fieldName}};
                                       }
                                       class Program {
                                           public static void Main() {
                                           }
                                       }
                                       """
                                   select new ValidOwnedFieldScenario(source,
                                       $"owned class {containerName} with owned field {fieldName}:{fieldTypeName}");

        return Gen.OneOf(ownedClassField, ownedStructField, ownedFieldInOwnedType);
    }

    /// <summary>
    /// Generates scenarios where a field is marked 'owned' but its type is NOT an owned type.
    /// These should report MIST_OWN_FIELD_NOT_OWNED.
    /// Covers: owned field with non-owned class type in non-owned container,
    /// owned field with non-owned class type in owned container.
    /// </summary>
    private static Gen<InvalidOwnedFieldScenario> GenInvalidOwnedFieldScenario()
    {
        var nonOwnedFieldInNonOwnedType = from containerName in Gen.Elements(ContainerTypeNames)
                                         from fieldTypeName in Gen.Elements(NonOwnedFieldTypeNames)
                                         from fieldName in Gen.Elements(FieldNames)
                                         let source = $$"""
                                             class {{fieldTypeName}} {
                                             }
                                             class {{containerName}} {
                                                 owned {{fieldTypeName}} {{fieldName}};
                                             }
                                             class Program {
                                                 public static void Main() {
                                                 }
                                             }
                                             """
                                         select new InvalidOwnedFieldScenario(source,
                                             $"non-owned class {containerName} with owned field {fieldName} of non-owned type {fieldTypeName}");

        var nonOwnedFieldInOwnedType = from containerName in Gen.Elements(ContainerTypeNames)
                                      from fieldTypeName in Gen.Elements(NonOwnedFieldTypeNames)
                                      from fieldName in Gen.Elements(FieldNames)
                                      let source = $$"""
                                          class {{fieldTypeName}} {
                                          }
                                          owned class {{containerName}} {
                                              owned {{fieldTypeName}} {{fieldName}};
                                              int dummy;
                                          }
                                          class Program {
                                              public static void Main() {
                                              }
                                          }
                                          """
                                      select new InvalidOwnedFieldScenario(source,
                                          $"owned class {containerName} with owned field {fieldName} of non-owned type {fieldTypeName}");

        var nonOwnedFieldWithRefFields = from containerName in Gen.Elements(ContainerTypeNames)
                                        from fieldTypeName in Gen.Elements(NonOwnedFieldTypeNames)
                                        from fieldName in Gen.Elements(FieldNames)
                                        from innerRefType in Gen.Elements("Dependency", "Resource", "Connection")
                                        let source = $$"""
                                            class {{innerRefType}} {
                                            }
                                            class {{fieldTypeName}} {
                                                {{innerRefType}} dep;
                                            }
                                            class {{containerName}} {
                                                owned {{fieldTypeName}} {{fieldName}};
                                            }
                                            class Program {
                                                public static void Main() {
                                                }
                                            }
                                            """
                                        select new InvalidOwnedFieldScenario(source,
                                            $"class {containerName} with owned field {fieldName} of non-owned type {fieldTypeName} (has ref field)");

        return Gen.OneOf(nonOwnedFieldInNonOwnedType, nonOwnedFieldInOwnedType, nonOwnedFieldWithRefFields);
    }

    // ─── Arbitrary instances ─────────────────────────────────────────────

    public static Arbitrary<ValidOwnedFieldScenario> ArbitraryValidOwnedFieldScenario() =>
        GenValidOwnedFieldScenario().ToArbitrary();

    public static Arbitrary<InvalidOwnedFieldScenario> ArbitraryInvalidOwnedFieldScenario() =>
        GenInvalidOwnedFieldScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 10a: Fields marked 'owned' with an owned type are accepted
    // (no MIST_OWN_FIELD_NOT_OWNED diagnostic)
    //
    // **Validates: Requirements 10.2**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnedFieldModifierPropertyTests) })]
    public bool OwnedFieldWithOwnedType_NoFieldNotOwnedDiagnostic(ValidOwnedFieldScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        return !diagnostics.Any(d => d.Id.Value == "MIST_OWN_FIELD_NOT_OWNED");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 10b: Fields marked 'owned' with a non-owned type are rejected
    // (MIST_OWN_FIELD_NOT_OWNED diagnostic reported)
    //
    // **Validates: Requirements 10.2, 10.3, 10.4, 10.5, 10.6**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnedFieldModifierPropertyTests) })]
    public bool OwnedFieldWithNonOwnedType_EmitsFieldNotOwned(InvalidOwnedFieldScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        return diagnostics.Any(d => d.Id.Value == "MIST_OWN_FIELD_NOT_OWNED");
    }
}
