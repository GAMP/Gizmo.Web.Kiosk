using Gizmo.UI;
using Gizmo.Web.Api.Clients.Builder;
using Gizmo.Web.Kiosk.Configuration;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Options;
using System.Reflection;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Gizmo.Web.Kiosk.App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.Configure<KioskOptions>(builder.Configuration.GetSection("Kiosk"));

static void httpClientConfig(IServiceProvider sp, HttpClient client)
{
    var options = sp.GetRequiredService<IOptions<KioskOptions>>().Value;

    var baseUrl = string.IsNullOrWhiteSpace(options.ServerUrl)
        ? sp.GetRequiredService<NavigationManager>().BaseUri
        : options.ServerUrl.TrimEnd('/') + "/";

    client.BaseAddress = new Uri(baseUrl);
}

builder.Services.AddSecureWebApiClients("GizmoKiosk", httpClientConfig)
    .WithMessagePackSerialization();

builder.Services.AddUnsecureWebApiClients("GizmoKioskUnsecured", httpClientConfig)
    .WithMessagePackSerialization();

builder.Services.AddLocalization();

var assembly = Assembly.GetExecutingAssembly();
builder.Services.AddUIServices();
builder.Services.AddViewStates(assembly);
builder.Services.AddViewServices(assembly);

await builder.Build().RunAsync();
