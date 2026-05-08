using System.Globalization;
using Gizmo.UI;
using Gizmo.UI.View.Services;
using Gizmo.Web.Api.Clients;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Gizmo.Web.Kiosk.ViewServices
{
    [Register]
    public sealed class LocalizationViewService : ViewServiceBase
    {
        public LocalizationViewService(
            ILogger<LocalizationViewService> logger,
            IServiceProvider serviceProvider,
            PublicOptionsWebApiClient publicOptionsClient,
            IJSRuntime jsRuntime) : base(logger, serviceProvider)
        {
            _publicOptionsClient = publicOptionsClient;
            _jsRuntime = jsRuntime;
        }

        private readonly PublicOptionsWebApiClient _publicOptionsClient;
        private readonly IJSRuntime _jsRuntime;

        protected override async Task OnInitializing(CancellationToken ct)
        {
            try
            {
                var options = await _publicOptionsClient.GeneralAsync(ct);

                if (!string.IsNullOrWhiteSpace(options.DefaultCulture))
                {
                    // Check if culture already set (persisted from previous load)
                    var stored = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", "kiosk.culture");

                    if (stored != options.DefaultCulture)
                    {
                        // Persist and reload so satellite assemblies load for the new culture
                        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "kiosk.culture", options.DefaultCulture);
                        await _jsRuntime.InvokeVoidAsync("location.reload");
                        return;
                    }

                    var culture = new CultureInfo(options.DefaultCulture);
                    CultureInfo.DefaultThreadCurrentCulture = culture;
                    CultureInfo.DefaultThreadCurrentUICulture = culture;
                    Logger.LogInformation("Culture set to {culture}.", culture.Name);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to fetch server culture, using browser default.");
            }

            await base.OnInitializing(ct);
        }
    }
}
