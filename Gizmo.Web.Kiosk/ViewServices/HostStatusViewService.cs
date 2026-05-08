using System.Net.Http.Json;
using System.Text.Json;
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
            IHttpClientFactory httpClientFactory,
            IOptions<KioskOptions> options,
            NavigationManager navigationManager) : base(viewState, logger, serviceProvider)
        {
            _hostsClient = hostsClient;
            _httpClientFactory = httpClientFactory;
            _options = options.Value;
            _navigationManager = navigationManager;
        }

        private readonly HostsWebApiClient _hostsClient;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly KioskOptions _options;
        private readonly NavigationManager _navigationManager;
        private CancellationTokenSource? _streamCts;

        protected override async Task OnNavigatedIn(NavigationParameters navigationParameters, CancellationToken cancellationToken = default)
        {
            await LoadAsync(cancellationToken);

            // Start SSE stream after initial load
            _streamCts = new CancellationTokenSource();
            _ = StreamEventsAsync(_streamCts.Token);
        }

        protected override Task OnNavigatedOut(NavigationParameters navigationParameters, CancellationToken cancellationToken = default)
        {
            _streamCts?.Cancel();
            _streamCts = null;
            return Task.CompletedTask;
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

        private async Task StreamEventsAsync(CancellationToken cancellationToken)
        {
            var baseUrl = string.IsNullOrWhiteSpace(_options.ServerUrl)
                ? _navigationManager.BaseUri.TrimEnd('/')
                : _options.ServerUrl.TrimEnd('/');

            var streamUrl = $"{baseUrl}/api/v3/hosts/status/stream";

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using var client = _httpClientFactory.CreateClient();
                    using var response = await client.GetAsync(streamUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    using var reader = new StreamReader(stream);

                    while (!cancellationToken.IsCancellationRequested && !reader.EndOfStream)
                    {
                        var line = await reader.ReadLineAsync(cancellationToken);

                        if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:"))
                            continue;

                        var json = line["data:".Length..].Trim();

                        try
                        {
                            var notification = JsonSerializer.Deserialize<HostStatusChangedDto>(json,
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                            if (notification?.HostId > 0)
                                await RefreshHostAsync(notification.HostId, cancellationToken);
                        }
                        catch (JsonException ex)
                        {
                            Logger.LogWarning(ex, "Failed to parse SSE event: {json}", json);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "SSE stream disconnected, reconnecting in 3s.");
                    await Task.Delay(3000, cancellationToken);
                }
            }
        }

        private async Task RefreshHostAsync(int hostId, CancellationToken cancellationToken)
        {
            try
            {
                var updated = await _hostsClient.GetStatusByIdAsync(hostId, cancellationToken);

                var hosts = ViewState.Hosts.ToList();
                var idx = hosts.FindIndex(h => h.HostId == hostId);

                if (idx >= 0)
                    hosts[idx] = updated;
                else
                    hosts.Add(updated);

                ViewState.Hosts = hosts;
                RaiseViewStateChanged();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to refresh host {HostId}.", hostId);
            }
        }

        protected override void OnDisposing(bool isDisposing)
        {
            _streamCts?.Cancel();
            _streamCts = null;
            base.OnDisposing(isDisposing);
        }

        private sealed class HostStatusChangedDto
        {
            public int HostId { get; init; }
        }
    }
}
