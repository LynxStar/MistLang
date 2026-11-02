using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace RazorComponents;

public class NavBar : Microsoft.AspNetCore.Components.ComponentBase
{
    private readonly Microsoft.AspNetCore.Components.NavigationManager Nav;

    public NavBar(Microsoft.AspNetCore.Components.NavigationManager Nav)
    {
        this.Nav = Nav;
    }

    private void GoAbout()
    {
        this.Nav.NavigateTo("about");
    }

    protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder __builder)
    {
        __builder.OpenElement(0, "nav");
        __builder.OpenElement(1, "span");
        __builder.AddContent(2, "At ");
        __builder.AddContent(3, this.Nav.ToBaseRelativePath(this.Nav.Uri));
        __builder.CloseElement();
        __builder.AddMarkupContent(4, "\n    ");
        __builder.OpenElement(5, "button");
        __builder.AddAttribute<Microsoft.AspNetCore.Components.Web.MouseEventArgs>(6, "onclick", Microsoft.AspNetCore.Components.EventCallback.Factory.Create<Microsoft.AspNetCore.Components.Web.MouseEventArgs>(this, GoAbout));
        __builder.AddContent(7, "About");
        __builder.CloseElement();
        __builder.CloseElement();
    }
}
