namespace Kadr.Desktop.Services.Diagnostics;

internal sealed class OfflineHttpHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("The offline distribution check attempted a network request.");
    }
}
