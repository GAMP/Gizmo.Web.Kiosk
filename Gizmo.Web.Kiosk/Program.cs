using Gizmo.UI;
using Gizmo.Web.Api.Clients.Builder;
using Gizmo.Web.Kiosk.Configuration;
using Gizmo.Web.Kiosk.Infrastructure;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Options;
using System.Reflection;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Gizmo.Web.Kiosk.App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.Configure<KioskOptions>(builder.Configuration.GetSection("Kiosk"));

builder.Services.AddTransient<ApiKeyMessageHandler>();

builder.Services.AddSecureWebApiClients("GizmoKiosk", (sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<KioskOptions>>().Value;
    var url = options.ServerUrl.TrimEnd('/') + "/";
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        throw new InvalidOperationException($"Kiosk:ServerUrl is not a valid absolute URI: '{options.ServerUrl}'");
    client.BaseAddress = uri;
})
.WithMessagePackSerialization()
.WithMessageHandler<ApiKeyMessageHandler>();

var assembly = Assembly.GetExecutingAssembly();
builder.Services.AddUIServices();
builder.Services.AddViewStates(assembly);
builder.Services.AddViewServices(assembly);

await builder.Build().RunAsync();
