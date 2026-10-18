namespace Kadr.Desktop.Services.Input;

internal sealed class SingleInstanceGuard : IDisposable
{
    internal bool IsPrimary
    {
        get;
    }

    private readonly Mutex _mutex;

    internal SingleInstanceGuard()
    {
        _mutex = new Mutex(true, @"Local\Kadr.Desktop." + Environment.UserName, out bool created);
        IsPrimary = created;
    }

    public void Dispose()
    {
        if (IsPrimary)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}
