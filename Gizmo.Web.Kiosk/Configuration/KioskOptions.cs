namespace Gizmo.Web.Kiosk.Configuration
{
    public sealed class KioskOptions
    {
        public string ServerUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public int? LayoutId { get; set; }
    }
}
