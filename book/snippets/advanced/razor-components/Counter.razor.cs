using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace RazorComponents;

[Microsoft.AspNetCore.Components.RouteAttribute("/counter")]
public class Counter : Microsoft.AspNetCore.Components.ComponentBase
{
    private int count = 0;
    private int? lastReset = null;

    private int Next(int value)
    {
        return value + 1;
    }

    private void Increment()
    {
        this.count = Next(this.count);
    }

    private void Reset()
    {
        this.lastReset = this.count;
        this.count = 0;
    }

    protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder __builder)
    {
        __builder.OpenComponent<Microsoft.AspNetCore.Components.Web.PageTitle>(0);
        __builder.AddComponentParameter(1, "ChildContent", ((Microsoft.AspNetCore.Components.RenderFragment)((Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder __b1) => 
{
    __b1.AddContent(2, "Counter");
}
)));
        __builder.CloseComponent();
        __builder.AddMarkupContent(3, "\n\n");
        __builder.AddMarkupContent(4, "<h1>Counter</h1>\n\n");
        __builder.OpenElement(5, "p");
        __builder.AddAttribute(6, "role", "status");
        __builder.AddContent(7, "Current count: ");
        __builder.AddContent(8, this.count);
        __builder.CloseElement();
        __builder.AddMarkupContent(9, "\n");
        __builder.OpenElement(10, "p");
        __builder.AddContent(11, "The count is a ");
        __builder.AddContent(12, typeof(int).Name);
        __builder.AddContent(13, ".");
        __builder.CloseElement();
        if (this.lastReset is null)
        {
            __builder.AddMarkupContent(14, "<p>Never reset.</p>");
        }
        else
        {
            __builder.OpenElement(15, "p");
            __builder.AddContent(16, "Last reset at count ");
            __builder.AddContent(17, this.lastReset);
            __builder.AddContent(18, ".");
            __builder.CloseElement();
        }
        __builder.OpenElement(19, "button");
        __builder.AddAttribute(20, "class", "btn btn-primary");
        __builder.AddAttribute<Microsoft.AspNetCore.Components.Web.MouseEventArgs>(21, "onclick", Microsoft.AspNetCore.Components.EventCallback.Factory.Create<Microsoft.AspNetCore.Components.Web.MouseEventArgs>(this, Increment));
        __builder.AddContent(22, "Click me");
        __builder.CloseElement();
        __builder.AddMarkupContent(23, "\n");
        __builder.OpenElement(24, "button");
        __builder.AddAttribute(25, "class", "btn btn-secondary");
        __builder.AddAttribute<Microsoft.AspNetCore.Components.Web.MouseEventArgs>(26, "onclick", Microsoft.AspNetCore.Components.EventCallback.Factory.Create<Microsoft.AspNetCore.Components.Web.MouseEventArgs>(this, Reset));
        __builder.AddContent(27, "Reset");
        __builder.CloseElement();
        __builder.AddMarkupContent(28, "\n\n");
        __builder.OpenComponent<RazorComponents.Greeting>(29);
        __builder.AddComponentParameter(30, "Title", "Counter says");
        __builder.AddComponentParameter(31, "Count", this.count);
        __builder.AddComponentParameter(32, "ChildContent", ((Microsoft.AspNetCore.Components.RenderFragment)((Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder __b2) => 
{
    __b2.AddMarkupContent(33, "<em>Nested from the counter.</em>");
}
)));
        __builder.CloseComponent();
    }
}
