namespace Gizmo.Web.Kiosk.Resources
{
    /// <summary>
    /// Marker class for IStringLocalizer resource resolution.
    /// </summary>
    public sealed class Resources
    {
        public const string HOST_STATE_FREE          = nameof(HOST_STATE_FREE);
        public const string HOST_STATE_IN_USE        = nameof(HOST_STATE_IN_USE);
        public const string HOST_STATE_LOCKED        = nameof(HOST_STATE_LOCKED);
        public const string HOST_STATE_OUT_OF_ORDER  = nameof(HOST_STATE_OUT_OF_ORDER);
        public const string HOST_STATE_MAINTENANCE   = nameof(HOST_STATE_MAINTENANCE);
        public const string ERROR_NO_HOSTS           = nameof(ERROR_NO_HOSTS);
        public const string ERROR_CONNECTION         = nameof(ERROR_CONNECTION);
        public const string ERROR_NO_HOSTS_FOUND     = nameof(ERROR_NO_HOSTS_FOUND);
    }
}
