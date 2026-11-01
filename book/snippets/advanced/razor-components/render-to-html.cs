using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Standard;
using System;

namespace RazorComponents;

public class Program
{
    public static async System.Threading.Tasks.Task Main(string[] args)
    {
        Microsoft.Extensions.DependencyInjection.ServiceCollection services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        Microsoft.Extensions.DependencyInjection.ServiceProvider provider = services.BuildServiceProvider();
        Microsoft.AspNetCore.Components.Web.HtmlRenderer renderer = new Microsoft.AspNetCore.Components.Web.HtmlRenderer(provider, provider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>());
        string html = await renderer.Dispatcher.InvokeAsync<string>(async () => 
        {
            Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent output = await renderer.RenderComponentAsync<RazorComponents.Counter>();
            return output.ToHtmlString();
        }
);
        System.Console.WriteLine(html);
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
