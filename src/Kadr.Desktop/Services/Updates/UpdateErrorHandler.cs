using System.Security.Cryptography;

using Kadr.Desktop.Resources;
using Kadr.Desktop.Services.Errors;
using Microsoft.Extensions.Logging;

namespace Kadr.Desktop.Services.Updates;

public sealed class UpdateErrorHandler : IViewModelErrorHandler
{
    private readonly ILogger<UpdateErrorHandler> _logger;

    public UpdateErrorHandler(ILogger<UpdateErrorHandler> logger)
    {
        _logger = logger;
    }

    public void Log(Exception exception, string operation)
    {
        _logger.LogError(exception, "Update operation failed: {Operation}", operation);
    }

    public string GetUserMessage(Exception exception)
    {
        string key = exception switch
        {
            InvalidDataException or CryptographicException => "Kadr.UpdateVerificationFailed",
            UnauthorizedAccessException => "Kadr.UpdateFolderReadOnly",
            _ => "Kadr.UpdateFailed"
        };
        return UiText.Get(key);
    }
}
