using Gizmo.Web.Api.Clients;
using Gizmo.Web.Api.Messaging;
using Gizmo.Web.Api.Models;
using Gizmo.Web.Api.Models.Abstractions;
using Gizmo.Web.Kiosk.Configuration;
using MessagePack;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gizmo.Web.Kiosk.Services
{
    public sealed class HostStatusService : IAsyncDisposable
    {
        public HostStatusService(
            HostsWebApiClient hostsClient,
            BranchesWebApiClient branchesClient,
            HostLayoutGroupsWebApiClient layoutGroupsClient,
            UsersWebApiClient usersClient,
            IOptions<KioskOptions> options,
            ILogger<HostStatusService> logger)
        {
            _hostsClient = hostsClient;
            _branchesClient = branchesClient;
            _layoutGroupsClient = layoutGroupsClient;
            _usersClient = usersClient;
            _options = options.Value;
            _logger = logger;
        }

        private readonly HostsWebApiClient _hostsClient;
        private readonly BranchesWebApiClient _branchesClient;
        private readonly HostLayoutGroupsWebApiClient _layoutGroupsClient;
        private readonly UsersWebApiClient _usersClient;
        private readonly KioskOptions _options;
        private readonly ILogger<HostStatusService> _logger;
        private HubConnection? _hub;

        public IReadOnlyDictionary<int, HostEntry> Hosts => _hosts;
        private readonly Dictionary<int, HostEntry> _hosts = [];

        public IReadOnlyList<HostLayoutModel>? Layout => _layout;
        private List<HostLayoutModel>? _layout;

        public int? ResolvedLayoutId { get; private set; }
        public string? ErrorMessage { get; private set; }

        public event EventHandler? Changed;

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (!await ResolveLayoutAsync(cancellationToken))
            {
                NotifyChanged();
                return;
            }

            await LoadHostsAsync(cancellationToken);
            await ConnectHubAsync(cancellationToken);
        }

        private async Task<bool> ResolveLayoutAsync(CancellationToken cancellationToken)
        {
            try
            {
                HostLayoutGroupModel? layoutGroup = null;

                if (_options.LayoutId.HasValue)
                {
                    layoutGroup = await _layoutGroupsClient.GetByIdAsync(
                        _options.LayoutId.Value,
                        new ModelFilterOptions { Expand = ["HostLayoutGroupLayouts"] },
                        cancellationToken);
                }
                else
                {
                    // Resolve default branch then first layout group
                    var branches = await _branchesClient.GetAsync(new BranchFilter
                    {
                        IsDeleted = false,
                        Pagination = new ModelFilterPagination { Limit = 1 }
                    }, cancellationToken);

                    var firstBranch = branches.Data.FirstOrDefault();
                    if (firstBranch is null)
                    {
                        ErrorMessage = "No branches found on the server.";
                        return false;
                    }

                    var layouts = await _layoutGroupsClient.GetAsync(new HostLayoutGroupsFilter
                    {
                        BranchId = firstBranch.Id,
                        Expand = ["HostLayoutGroupLayouts"],
                        Pagination = new ModelFilterPagination { Limit = 1 }
                    }, cancellationToken);

                    layoutGroup = layouts.Data.FirstOrDefault();
                }

                if (layoutGroup is null)
                {
                    ErrorMessage = "No layout found. Please configure a layout in Gizmo Manager.";
                    return false;
                }

                ResolvedLayoutId = layoutGroup.Id;
                _layout = layoutGroup.HostLayouts.ToList();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resolve layout.");
                ErrorMessage = "Could not connect to server.";
                return false;
            }
        }

        private async Task LoadHostsAsync(CancellationToken cancellationToken)
        {
            try
            {
                var layoutMap = _layout?
                    .Where(l => !l.IsHidden)
                    .ToDictionary(l => l.HostId);

                if (layoutMap is null || layoutMap.Count == 0)
                    return;

                var result = await _hostsClient.GetAsync(new HostsFilter
                {
                    IsDeleted = false,
                    Pagination = new ModelFilterPagination { Limit = 1000 }
                }, cancellationToken);

                _hosts.Clear();

                foreach (var host in result.Data)
                {
                    if (host is not IHostModelV3 h || host is not IModelIntIdentifier id)
                        continue;

                    if (!layoutMap.TryGetValue(id.Id, out var layoutEntry))
                        continue;

                    _hosts[id.Id] = new HostEntry(id.Id, h.Name, h.Number, h.IsOutOfOrder, h.IsLocked,
                        false, null, layoutEntry.Row, layoutEntry.Column);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load hosts.");
                ErrorMessage = "Failed to load hosts from server.";
                NotifyChanged();
            }
        }

        private async Task ConnectHubAsync(CancellationToken cancellationToken)
        {
            var serverUrl = _options.ServerUrl.TrimEnd('/');
            var eventsUrl = $"{serverUrl}/api/events?api_key={Uri.EscapeDataString(_options.ApiKey)}";

            _hub = new HubConnectionBuilder()
                .AddMessagePackProtocol(opt =>
                {
                    opt.SerializerOptions = MessagePackSerializerOptions
                        .Standard
                        .WithResolver(MessagePack.Resolvers.StandardResolver.Instance)
                        .WithSecurity(MessagePackSecurity.TrustedData);
                })
                .WithAutomaticReconnect()
                .WithUrl(eventsUrl, options =>
                {
                    options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets;
                    options.DefaultTransferFormat = Microsoft.AspNetCore.Connections.TransferFormat.Binary;
                })
                .Build();

            _hub.On<IAPIEventMessage>("Event", OnEventMessage);
            _hub.Reconnected += async _ => { await LoadHostsAsync(default); NotifyChanged(); };

            try
            {
                await _hub.StartAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to event hub.");
            }
        }

        private void OnEventMessage(IAPIEventMessage message)
        {
            var changed = false;

            switch (message)
            {
                case UserSessionCreatedEventMessage m:
                    changed = ApplySessionState(m.HostId, m.State, m.UserId);
                    if (changed && _hosts[m.HostId].IsOccupied)
                        _ = FetchAndApplyBalanceAsync(m.HostId, m.UserId);
                    break;

                case UserSessionStateChangedEventMessage m:
                    changed = ApplySessionState(m.HostId, m.State, m.UserId);
                    if (changed && _hosts[m.HostId].IsOccupied && _hosts[m.HostId].CreditedTime == null)
                        _ = FetchAndApplyBalanceAsync(m.HostId, m.UserId);
                    break;

                case UserBalanceChangedEventMessage m:
                    changed = ApplyUserBalance(m.UserId, m.CreditedTime);
                    break;

                case HostLockStateChanged m when _hosts.TryGetValue(m.HostId, out var h):
                    _hosts[m.HostId] = h with { IsLocked = m.IsLocked };
                    changed = true;
                    break;

                case HostInOrderStateChanged m when _hosts.TryGetValue(m.HostId, out var h):
                    _hosts[m.HostId] = h with { IsOutOfOrder = !m.InOrder };
                    changed = true;
                    break;
            }

            if (changed)
                NotifyChanged();
        }

        private bool ApplySessionState(int hostId, UserSessionState state, int userId)
        {
            if (!_hosts.TryGetValue(hostId, out var entry))
                return false;

            var isOccupied = (state & UserSessionState.Active) != 0 && (state & UserSessionState.Ended) == 0;
            _hosts[hostId] = entry with { IsOccupied = isOccupied, ActiveUserId = isOccupied ? userId : null, CreditedTime = isOccupied ? entry.CreditedTime : null };
            return true;
        }

        private bool ApplyUserBalance(int userId, double? creditedTime)
        {
            var host = _hosts.Values.FirstOrDefault(h => h.ActiveUserId == userId);
            if (host is null) return false;
            _hosts[host.Id] = host with { CreditedTime = creditedTime };
            return true;
        }

        private async Task FetchAndApplyBalanceAsync(int hostId, int userId)
        {
            try
            {
                var balance = await _usersClient.BalanceAsync(userId, preferCache: false);
                if (_hosts.TryGetValue(hostId, out var entry) && entry.ActiveUserId == userId)
                {
                    _hosts[hostId] = entry with { CreditedTime = balance.CreditedTime };
                    NotifyChanged();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch balance for user {UserId}.", userId);
            }
        }

        private void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

        public async ValueTask DisposeAsync()
        {
            if (_hub is not null)
                await _hub.DisposeAsync();
        }
    }

    public sealed record HostEntry(int Id, string Name, int Number, bool IsOutOfOrder, bool IsLocked, bool IsOccupied, int? ActiveUserId, int? Row = null, int? Column = null, double? CreditedTime = null);
}
