using Gizmo.Web.Api.Models;
using Gizmo.Web.Kiosk.ViewStates;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace Gizmo.Web.Kiosk.Pages
{
    public abstract class HostStatusBase : ComponentBase, IDisposable
    {
        [Inject] protected HostStatusViewState ViewState { get; set; } = null!;
        [Inject] protected IJSRuntime JS { get; set; } = null!;
        [Inject] protected IStringLocalizer<Gizmo.Web.Kiosk.Resources.Resources> L { get; set; } = null!;

        private DotNetObjectReference<HostStatusBase>? _selfRef;

        protected const int GapPx = 6;
        protected const int MinCellPx = 60;
        protected const int MaxCellPx = 72;

        protected int _vpWidth = 1280;
        protected int _vpHeight = 800;

        protected Dictionary<(int, int), HostStatusModel> HostsByCell { get; private set; } = [];

        protected int MaxRow => ViewState.MaxRow;
        protected int MaxCol => ViewState.MaxCol;
        protected int GridCols => Math.Max(MaxCol + 1, (_vpWidth - GapPx) / Math.Max(CellSize + GapPx, 1));
        protected int GridRows => Math.Max(MaxRow + 1, (_vpHeight - GapPx) / Math.Max(CellSize + GapPx, 1));

        protected int CellSize
        {
            get
            {
                if (!ViewState.Hosts.Any()) return 82;
                var byWidth  = (_vpWidth  - GapPx) / (MaxCol + 1) - GapPx;
                var byHeight = (_vpHeight - GapPx) / (MaxRow + 1) - GapPx;
                return Math.Clamp(Math.Min(byWidth, byHeight), MinCellPx, MaxCellPx);
            }
        }

        protected static string CellStyle(int row, int col, int size) =>
            $"left:{col * (size + GapPx) + GapPx}px; top:{row * (size + GapPx) + GapPx}px; width:{size}px; height:{size}px;";

        protected override Task OnInitializedAsync()
        {
            ViewState.OnChange += OnViewStateChanged;
            BuildCellMap();
            return Task.CompletedTask;
        }

        protected bool _canInstall;

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                var size = await JS.InvokeAsync<int[]>("eval", "[window.innerWidth, window.innerHeight]");
                _vpWidth  = size[0];
                _vpHeight = size[1];

                _selfRef = DotNetObjectReference.Create(this);
                await JS.InvokeVoidAsync("registerResizeCallback", _selfRef);

                _canInstall = await JS.InvokeAsync<bool>("pwaCanInstall");
                StateHasChanged();
            }
        }

        [JSInvokable]
        public void OnWindowResize(int width, int height)
        {
            _vpWidth  = width;
            _vpHeight = height;
            InvokeAsync(StateHasChanged);
        }

        protected async Task OnInstallClick()
        {
            await JS.InvokeAsync<bool>("pwaInstall");
            _canInstall = await JS.InvokeAsync<bool>("pwaCanInstall");
            StateHasChanged();
        }

        protected void BuildCellMap()
        {
            HostsByCell = ViewState.Hosts
                .ToDictionary(h => (h.Row, h.Column));
        }

        protected void OnViewStateChanged(object? sender, EventArgs e)
        {
            BuildCellMap();
            InvokeAsync(StateHasChanged);
        }

        protected static string GetCellClass(HostStatusModel host)
        {
            var state = host.IsOutOfOrder || host.IsInMaintenance ? "host-cell--out-of-order"
                      : host.IsLocked                            ? "host-cell--locked"
                      : host.Sessions.Any()                      ? "host-cell--occupied"
                      :                                            "host-cell--free";

            return host.IsConnected ? state : $"{state} host-cell--offline";
        }

        protected string GetStatusLabel(HostStatusModel host)
        {
            if (host.IsOutOfOrder)    return L[nameof(Gizmo.Web.Kiosk.Resources.Resources.HOST_STATE_OUT_OF_ORDER)];
            if (host.IsInMaintenance) return L[nameof(Gizmo.Web.Kiosk.Resources.Resources.HOST_STATE_MAINTENANCE)];
            if (host.IsLocked)        return L[nameof(Gizmo.Web.Kiosk.Resources.Resources.HOST_STATE_LOCKED)];
            return L[nameof(Gizmo.Web.Kiosk.Resources.Resources.HOST_STATE_FREE)];
        }

        protected string FormatUsername(string username)
        {
            // Guest usernames are URL-safe base64-encoded GUIDs truncated to 22 chars
            // (+→-, /→_, no padding). Detect by length and charset.
            if (username.Length == 22 &&
                username.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'))
            {
                return L[nameof(Gizmo.Web.Kiosk.Resources.Resources.USER_GUEST)];
            }
            return username;
        }

        protected string FormatSessionState(Gizmo.Web.Api.Models.UserSessionState state)
        {
            if (state.HasFlag(Gizmo.Web.Api.Models.UserSessionState.Paused))
                return L[nameof(Gizmo.Web.Kiosk.Resources.Resources.SESSION_STATE_PAUSED)];
            if (state.HasFlag(Gizmo.Web.Api.Models.UserSessionState.Pending))
                return L[nameof(Gizmo.Web.Kiosk.Resources.Resources.SESSION_STATE_PENDING)];
            if (state.HasFlag(Gizmo.Web.Api.Models.UserSessionState.Grace))
                return L[nameof(Gizmo.Web.Kiosk.Resources.Resources.SESSION_STATE_GRACE)];
            return L[nameof(Gizmo.Web.Kiosk.Resources.Resources.SESSION_STATE_ACTIVE)];
        }

        protected static string FormatTime(double? seconds)
        {
            if (seconds is null) return "∞";
            var ts = TimeSpan.FromSeconds(seconds.Value);
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}h {ts.Minutes}m"
                : $"{(int)ts.TotalMinutes}m";
        }

        public void Dispose()
        {
            ViewState.OnChange -= OnViewStateChanged;
            if (_selfRef is not null)
            {
                JS.InvokeVoidAsync("unregisterResizeCallback");
                _selfRef.Dispose();
            }
        }
    }
}
