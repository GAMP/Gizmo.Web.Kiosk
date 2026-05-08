using System.Globalization;
using Gizmo.UI;
using Gizmo.UI.View.Services;
using Gizmo.Web.Api.Clients;
using Microsoft.Extensions.Logging;

namespace Gizmo.Web.Kiosk.ViewServices
{
    [Register]
    public sealed class LocalizationViewService : ViewServiceBase
    {
        public LocalizationViewService(
            ILogger<LocalizationViewService> logger,
            IServiceProvider serviceProvider,
            PublicOptionsWebApiClient publicOptionsClient) : base(logger, serviceProvider)
        {
            _publicOptionsClient = publicOptionsClient;
        }

        private readonly PublicOptionsWebApiClient _publicOptionsClient;

        protected override async Task OnInitializing(CancellationToken ct)
        {
            try
            {
                var options = await _publicOptionsClient.GeneralAsync(ct);

                if (!string.IsNullOrWhiteSpace(options.DefaultCulture))
                {
                    var culture = new CultureInfo(options.DefaultCulture);
                    CultureInfo.DefaultThreadCurrentCulture = culture;
                    CultureInfo.DefaultThreadCurrentUICulture = culture;
                    Logger.LogInformation("Culture set to {culture} from server options.", culture.Name);
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
