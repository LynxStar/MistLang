using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace RazorComponents;

public class Greeting : Microsoft.AspNetCore.Components.ComponentBase
{
    [Microsoft.AspNetCore.Components.ParameterAttribute]
    public string Title { get; set; } = "Hello";

    [Microsoft.AspNetCore.Components.ParameterAttribute]
    public int Count { get; set; }

    [Microsoft.AspNetCore.Components.ParameterAttribute]
    public Microsoft.AspNetCore.Components.RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder __builder)
    {
        __builder.OpenElement(0, "section");
        __builder.AddAttribute(1, "class", "greeting");
        __builder.OpenElement(2, "h2");
        __builder.AddContent(3, Title);
        __builder.CloseElement();
        {
            if (ChildContent is Microsoft.AspNetCore.Components.RenderFragment body)
            {
                __builder.OpenElement(4, "div");
                __builder.AddAttribute(5, "class", "body");
                __builder.AddContent(6, body);
                __builder.CloseElement();
            }
        }
        __builder.OpenElement(7, "p");
        __builder.AddContent(8, "Shown ");
        __builder.AddContent(9, Count);
        __builder.AddContent(10, " times.");
        __builder.CloseElement();
        __builder.CloseElement();
    }
}
