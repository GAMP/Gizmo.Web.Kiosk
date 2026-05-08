using System.Net.Http.Json;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using Gizmo.UI;
using Gizmo.UI.View.Services;
using Gizmo.Web.Api.Clients;
using Gizmo.Web.Api.Models;
using Gizmo.Web.Kiosk.Configuration;
using Gizmo.Web.Kiosk.Resources;
using Gizmo.Web.Kiosk.ViewStates;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
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
            NavigationManager navigationManager,
            IStringLocalizer<Resources.Resources> localizer) : base(viewState, logger, serviceProvider)
        {
            _hostsClient = hostsClient;
            _httpClientFactory = httpClientFactory;
            _options = options.Value;
            _navigationManager = navigationManager;
            _localizer = localizer;
        }

        private readonly HostsWebApiClient _hostsClient;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly KioskOptions _options;
        private readonly NavigationManager _navigationManager;
        private readonly IStringLocalizer<Resources.Resources> _localizer;
        private CancellationTokenSource? _streamCts;
        private readonly Subject<int> _hostChangedSubject = new();
        private IDisposable? _debounceSubscription;

        protected override async Task OnNavigatedIn(NavigationParameters navigationParameters, CancellationToken cancellationToken = default)
        {
            _debounceSubscription = _hostChangedSubject
                .GroupBy(id => id)
                .SelectMany(g => g.Throttle(TimeSpan.FromMilliseconds(300)))
                .Subscribe(hostId => _ = RefreshHostAsync(hostId, CancellationToken.None));

            _streamCts = new CancellationTokenSource();
            _ = LoadWithRetryAsync(_streamCts.Token);
        }

        protected override Task OnNavigatedOut(NavigationParameters navigationParameters, CancellationToken cancellationToken = default)
        {
            _streamCts?.Cancel();
            _streamCts = null;
            _debounceSubscription?.Dispose();
            _debounceSubscription = null;
            return Task.CompletedTask;
        }

        private static readonly TimeSpan[] RetryDelays =
        [
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30),
        ];

        private async Task LoadWithRetryAsync(CancellationToken cancellationToken)
        {
            var attempt = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                var success = await LoadAsync(cancellationToken);
                if (success)
                {
                    _ = StreamEventsAsync(cancellationToken);
                    return;
                }

                var delay = RetryDelays[Math.Min(attempt, RetryDelays.Length - 1)];
                attempt++;
                Logger.LogInformation("Load failed, retrying in {delay}s (attempt {attempt}).", delay.TotalSeconds, attempt);

                try { await Task.Delay(delay, cancellationToken); }
                catch (OperationCanceledException) { return; }
            }
        }

        private async Task<bool> LoadAsync(CancellationToken cancellationToken)
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
                    ViewState.ErrorMessage = _localizer[Resources.Resources.ERROR_NO_HOSTS];
                    return false;
                }

                ViewState.Hosts = hosts;
                ViewState.MaxRow = hosts.Max(h => h.Row);
                ViewState.MaxCol = hosts.Max(h => h.Column);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to load host statuses.");
                ViewState.ErrorMessage = _localizer[Resources.Resources.ERROR_CONNECTION];
                return false;
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

                    string? line;
                    while (!cancellationToken.IsCancellationRequested &&
                           (line = await reader.ReadLineAsync(cancellationToken)) is not null)
                    {
                        if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:"))
                            continue;

                        var json = line["data:".Length..].Trim();

                        try
                        {
                            var notification = JsonSerializer.Deserialize<HostStatusChangedDto>(json,
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                            if (notification?.HostId > 0)
                            _hostChangedSubject.OnNext(notification.HostId);
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
                {
                    // Preserve layout position from the existing entry — GetStatusByIdAsync
                    // has no layout context so it returns Row=0, Column=0.
                    var existing = hosts[idx];
                    hosts[idx] = new HostStatusModel
                    {
                        HostId = updated.HostId,
                        Number = updated.Number,
                        Name = updated.Name,
                        HostGroupId = updated.HostGroupId,
                        HostGroupName = updated.HostGroupName,
                        Row = existing.Row,
                        Column = existing.Column,
                        IsLocked = updated.IsLocked,
                        IsOutOfOrder = updated.IsOutOfOrder,
                        IsInMaintenance = updated.IsInMaintenance,
                        IsConnected = updated.IsConnected,
                        Sessions = updated.Sessions,
                    };
                }
                else
                {
                    hosts.Add(updated);
                }

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
            _debounceSubscription?.Dispose();
            _hostChangedSubject.OnCompleted();
            base.OnDisposing(isDisposing);
        }

        private sealed class HostStatusChangedDto
        {
            public int HostId { get; init; }
        }
    }
}
