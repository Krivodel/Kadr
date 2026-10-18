namespace Kadr.Desktop.Services.Errors;

public interface IViewModelErrorHandler
{
    void Log(Exception exception, string operation);
    string GetUserMessage(Exception exception);
}
