using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Standard;

namespace RazorComponents;

public class Program
{
    public static void Main(string[] args)
    {
        Microsoft.AspNetCore.Builder.WebApplicationBuilder builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        Microsoft.AspNetCore.Builder.WebApplication app = builder.Build();
        app.UseStaticFiles();
        app.UseAntiforgery();
        app.MapRazorComponents<RazorComponents.App>().AddInteractiveServerRenderMode();
        app.Run();
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
