using Gizmo.UI;
using Gizmo.UI.View.States;
using Gizmo.Web.Api.Models;

namespace Gizmo.Web.Kiosk.ViewStates
{
    [Register]
    public sealed class HostStatusViewState : ViewStateBase
    {
        public IReadOnlyList<HostStatusModel> Hosts { get; set; } = [];
        public int MaxRow { get; set; }
        public int MaxCol { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
