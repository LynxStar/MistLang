using System.Reflection;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Integration test verifying that span-containment heuristic methods have been
/// removed from <see cref="SemanticModelBuilder"/>. These methods were replaced
/// by the NodeOrigin-based location resolution.
/// </summary>
public class SemanticModelBuilderNoSpanContainmentTests
{
    private static readonly string[] RemovedMethodNames =
    [
        "FindSourceTextByContainment",
        "FindSourceTextForNode",
        "FindSourceTextForClassDecl",
        "FindSourceTextForSymbol",
    ];

    private const BindingFlags AllMethods =
        BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Static | BindingFlags.Instance;

    [Fact]
    public void SemanticModelBuilder_DoesNotContain_SpanContainmentMethods()
    {
        var type = typeof(SemanticModelBuilder);
        var methods = type.GetMethods(AllMethods);

        foreach (var removedName in RemovedMethodNames)
        {
            var found = methods.FirstOrDefault(m => m.Name == removedName);
            Assert.True(
                found is null,
                $"SemanticModelBuilder should not contain method '{removedName}', " +
                $"but it was found with signature: {found}");
        }
    }
}
