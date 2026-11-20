using AC_Controller.Models;

namespace AC_Controller;

// Every menu action and poll for one device runs in submission order.
internal sealed class TrayDeviceSession(
    Func<CancellationToken, Task<GreeStatus>> readStatus,
    Action<GreeStatus> publishStatus,
    CancellationToken lifetime)
{
    private readonly object _sync = new();
    private Task _tail = Task.CompletedTask;
    private bool _closed;

    public bool IsCancelled => lifetime.IsCancellationRequested;

    public Task RunAsync(Func<CancellationToken, Task>? command = null)
    {
        Task previous;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            if (_closed || lifetime.IsCancellationRequested)
                return Task.FromCanceled(new CancellationToken(true));
            previous = _tail;
            _tail = completed.Task;
        }

        return ExecuteAsync(previous, completed, command);
    }

    private async Task ExecuteAsync(
        Task previous, TaskCompletionSource completed, Func<CancellationToken, Task>? command)
    {
        try
        {
            await previous;
            lifetime.ThrowIfCancellationRequested();
            if (command is not null)
            {
                await command(lifetime);
                lifetime.ThrowIfCancellationRequested();
            }
            var status = await readStatus(lifetime);
            lifetime.ThrowIfCancellationRequested();
            publishStatus(status);
        }
        finally
        {
            completed.TrySetResult();
        }
    }

    // The caller cancels the session first, then awaits all accepted operations.
    public Task DrainAsync()
    {
        lock (_sync)
        {
            _closed = true;
            return _tail;
        }
    }
}
