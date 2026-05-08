using Gizmo.Web.Kiosk.Configuration;
using Microsoft.Extensions.Options;

namespace Gizmo.Web.Kiosk.Infrastructure
{
    public sealed class ApiKeyMessageHandler : DelegatingHandler
    {
        private readonly KioskOptions _options;

        public ApiKeyMessageHandler(IOptions<KioskOptions> options)
        {
            _options = options.Value;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(_options.ApiKey))
            {
                request.Headers.Remove("X-Api-Key");
                request.Headers.TryAddWithoutValidation("X-Api-Key", _options.ApiKey);
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
