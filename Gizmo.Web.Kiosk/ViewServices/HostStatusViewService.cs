using Gizmo.UI;
using Gizmo.UI.View.Services;
using Gizmo.Web.Api.Clients;
using Gizmo.Web.Api.Models;
using Gizmo.Web.Kiosk.Configuration;
using Gizmo.Web.Kiosk.ViewStates;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gizmo.Web.Kiosk.ViewServices
{
    [Register]
    [Route("/")]
    [Route("/hoststatus")]
    public sealed class HostStatusViewService : ViewStateServiceBase<HostStatusViewState>
    {
        public HostStatusViewService(
            HostStatusViewState viewState,
            ILogger<HostStatusViewService> logger,
            IServiceProvider serviceProvider,
            HostsWebApiClient hostsClient,
            IOptions<KioskOptions> options) : base(viewState, logger, serviceProvider)
        {
            _hostsClient = hostsClient;
            _options = options.Value;
        }

        private readonly HostsWebApiClient _hostsClient;
        private readonly KioskOptions _options;

        protected override async Task OnNavigatedIn(NavigationParameters navigationParameters, CancellationToken cancellationToken = default)
        {
            await LoadAsync(cancellationToken);
        }

        private async Task LoadAsync(CancellationToken cancellationToken)
        {
            ViewState.IsInitializing = true;
            ViewState.ErrorMessage = null;
            RaiseViewStateChanged();

            try
            {
                var filter = new HostStatusFilter { LayoutId = _options.LayoutId };
                var hosts = (await _hostsClient.GetStatusAsync(filter, cancellationToken)).ToList();

                if (hosts.Count == 0)
                {
                    ViewState.ErrorMessage = "No hosts found. Please check layout configuration.";
                }
                else
                {
                    ViewState.Hosts = hosts;
                    ViewState.MaxRow = hosts.Max(h => h.Row);
                    ViewState.MaxCol = hosts.Max(h => h.Column);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to load host statuses.");
                ViewState.ErrorMessage = "Could not connect to server.";
            }
            finally
            {
                ViewState.IsInitializing = false;
                ViewState.IsInitialized = true;
                RaiseViewStateChanged();
            }
        }
    }
}
