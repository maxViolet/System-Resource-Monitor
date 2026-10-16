using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using SystemResourceMonitor.Core.Metrics;

namespace SystemResourceMonitor.Desktop.Adapters.Metrics;

internal sealed partial class PdhGpuCounterApi : IGpuCounterApi
{
    private const uint PdhMoreData = 0x800007D2;
    private const uint PdhFmtDouble = 0x00000200;
    private const uint PdhFmtNoCap100 = 0x00008000;
    private SafePdhQueryHandle? _query;
    private nint _counter;
    private nint _buffer;
    private uint _bufferBytes;
    private bool _disposed;

    public bool TryCollect(out IReadOnlyList<GpuCounterItem> items, out int status)
    {
        items = [];
        status = 0;
        if (_disposed)
        {
            status = unchecked((int)0xC0000001);
            return false;
        }

        if (!EnsureQuery(out status))
        {
            return false;
        }

        status = unchecked((int)PdhCollectQueryData(_query!));
        if (status != 0) return false;

        uint count = 0;
        uint bytes = 0;
        var result = PdhGetFormattedCounterArrayW(_counter, PdhFmtDouble | PdhFmtNoCap100, ref bytes, ref count, 0);
        if (result != PdhMoreData && result != 0)
        {
            status = unchecked((int)result);
            return false;
        }

        if (bytes > _bufferBytes)
        {
            if (_buffer != 0)
            {
                Marshal.FreeHGlobal(_buffer);
            }

            _buffer = Marshal.AllocHGlobal(checked((int)bytes));
            _bufferBytes = bytes;
        }

        result = PdhGetFormattedCounterArrayW(_counter, PdhFmtDouble | PdhFmtNoCap100, ref _bufferBytes, ref count, _buffer);
        if (result != 0)
        {
            status = unchecked((int)result);
            return false;
        }

        var values = new List<GpuCounterItem>(checked((int)count));
        var itemSize = Marshal.SizeOf<PdhFmtCounterValueItem>();
        for (var index = 0; index < count; index++)
        {
            var item = Marshal.PtrToStructure<PdhFmtCounterValueItem>(_buffer + checked((int)index * itemSize));
            var name = Marshal.PtrToStringUni(item.Name) ?? string.Empty;
            values.Add(new GpuCounterItem(name, item.Value.Value, item.Value.Status is 0 or 1));
        }

        items = values;
        return true;
    }

    private bool EnsureQuery(out int status)
    {
        status = 0;
        if (_query is not null)
        {
            return true;
        }

        var result = PdhOpenQueryW(null, 0, out var query);
        if (result != 0)
        {
            status = unchecked((int)result);
            return false;
        }

        _query = query;
        result = PdhAddEnglishCounterW(query, "\\GPU Engine(*)\\Utilization Percentage", 0, out _counter);
        if (result != 0)
        {
            status = unchecked((int)result);
            query.Dispose();
            _query = null;
            return false;
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _query?.Dispose();
        _query = null;
        _counter = 0;
        if (_buffer != 0)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = 0;
            _bufferBytes = 0;
        }
    }

    private sealed class SafePdhQueryHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafePdhQueryHandle() : base(true) { }
        protected override bool ReleaseHandle() => PdhCloseQuery(handle) == 0;
    }

    [LibraryImport("pdh.dll", EntryPoint = "PdhOpenQueryW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint PdhOpenQueryW(string? dataSource, nuint userData, out SafePdhQueryHandle query);
    [LibraryImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint PdhAddEnglishCounterW(SafePdhQueryHandle query, string path, nuint userData, out nint counter);
    [LibraryImport("pdh.dll", EntryPoint = "PdhCollectQueryData")]
    private static partial uint PdhCollectQueryData(SafePdhQueryHandle query);
    [LibraryImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")]
    private static partial uint PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint bufferBytes, ref uint itemCount, nint items);
    [LibraryImport("pdh.dll", EntryPoint = "PdhCloseQuery")]
    private static partial uint PdhCloseQuery(nint query);
}
