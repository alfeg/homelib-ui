namespace MyHomeLib.Files.Torrents;

public class Gate 
{
    private readonly SemaphoreSlim _semaphoreSlim;

    public Gate(SemaphoreSlim? semaphoreSlim = null)
    {
        _semaphoreSlim = semaphoreSlim ?? new SemaphoreSlim(1);
    }

    public async Task<IDisposable> Wait()
    {
        await _semaphoreSlim.WaitAsync();
        return new GateHolder(_semaphoreSlim);
    }

    class GateHolder : IDisposable
    {
        private readonly SemaphoreSlim _semaphoreSlim;

        public GateHolder(SemaphoreSlim semaphoreSlim)
        {
            _semaphoreSlim = semaphoreSlim;
        }
        
        public void Dispose()
        {
            _semaphoreSlim.Release();
        }
    }
}