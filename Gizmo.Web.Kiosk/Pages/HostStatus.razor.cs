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

        protected const int GapPx = 6;
        protected const int MinCellPx = 60;
        protected const int MaxCellPx = 72;

        private int _vpWidth = 1280;
        private int _vpHeight = 800;

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

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                var size = await JS.InvokeAsync<int[]>("eval", "[window.innerWidth, window.innerHeight]");
                _vpWidth  = size[0];
                _vpHeight = size[1];
                StateHasChanged();
            }
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

        protected static string FormatTime(double? seconds)
        {
            if (seconds is null) return "∞";
            var ts = TimeSpan.FromSeconds(seconds.Value);
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}"
                : $"{ts.Minutes}:{ts.Seconds:D2}";
        }

        public void Dispose()
        {
            ViewState.OnChange -= OnViewStateChanged;
        }
    }
}
