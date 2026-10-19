using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using XScreen.Core;

namespace XScreen.App;

sealed record MonitorIoTrace(long Sequence, long Connection, string Operation, string Source, string DeviceKey,
    DateTimeOffset StartedAt, int Thread, double DurationMs, double? IdleBeforeMs, int ConcurrentIoCalls,
    byte? Code, uint? Target, MonitorValue? Reply, bool WriteCallEntered, string? ExceptionType, int? ExceptionHResult);

// Passive observation only: one forwarded call, no retry, delay, cached reply or recovery.
sealed class TracingMonitorTransport(IMonitorTransport inner, Action<MonitorIoTrace> observe, string source) : IMonitorTransport
{
    static long nextConnection;
    long sequence, lastIoCompleted;
    int activeIo;
    public IReadOnlyList<MonitorDevice> Enumerate() => Measure(0, "enumerate", "", null, null, false, inner.Enumerate);
    public IMonitorConnection Open(string deviceId)
    {
        long id = Interlocked.Increment(ref nextConnection);
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(deviceId)))[..16];
        var connection = Measure(id, "open", key, null, null, false, () => inner.Open(deviceId));
        return new Connection(this, connection, id, key);
    }
    T Measure<T>(long connection, string operation, string device, byte? code, uint? target, bool writeEntered, Func<T> call)
    {
        bool io = operation is "read" or "write";
        long ordinal = Interlocked.Increment(ref sequence), previous = Interlocked.Read(ref lastIoCompleted);
        var at = DateTimeOffset.Now; long start = Stopwatch.GetTimestamp();
        int thread = Environment.CurrentManagedThreadId, concurrent = io ? Interlocked.Increment(ref activeIo) : Volatile.Read(ref activeIo);
        T? result = default; Exception? error = null;
        try { result = call(); return result; }
        catch (Exception ex) { error = ex; throw; }
        finally
        {
            long end = Stopwatch.GetTimestamp();
            if (io)
            {
                long observed;
                do { observed = Interlocked.Read(ref lastIoCompleted); if (observed >= end) break; }
                while (Interlocked.CompareExchange(ref lastIoCompleted, end, observed) != observed);
                Interlocked.Decrement(ref activeIo);
            }
            try
            {
                observe(new(ordinal, connection, operation, source, device, at, thread, Stopwatch.GetElapsedTime(start, end).TotalMilliseconds,
                    io && previous != 0 ? Stopwatch.GetElapsedTime(previous, start).TotalMilliseconds : null, concurrent,
                    code, target, result is MonitorValue value ? value : null, writeEntered, error?.GetType().FullName, error?.HResult));
            }
            catch (Exception) { /* Diagnostic failure must never alter I/O, retries or fault classification. */ }
        }
    }
    sealed class Connection(TracingMonitorTransport owner, IMonitorConnection inner, long id, string key) : IMonitorConnection
    {
        int disposed, writeEntered;
        public MonitorValue Read(byte code)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            return owner.Measure(id, "read", key, code, null, Volatile.Read(ref writeEntered) != 0, () => inner.Read(code));
        }
        public void Write(byte code, uint value)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            Interlocked.Exchange(ref writeEntered, 1);
            owner.Measure(id, "write", key, code, value, true, () => { inner.Write(code, value); return true; });
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            owner.Measure(id, "close", key, null, null, Volatile.Read(ref writeEntered) != 0, () => { inner.Dispose(); return true; });
        }
    }
}
