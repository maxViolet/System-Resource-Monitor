using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using SystemResourceMonitor.Core.Metrics;

namespace SystemResourceMonitor.Desktop.Adapters.Metrics;

internal sealed partial class DxCoreAdapterApi : IGpuAdapterApi, IDisposable
{
    private static readonly Guid FactoryIid = new("78ee5945-c36e-4b13-a669-005dd11c0f06");
    private static readonly Guid ListIid = new("526c7776-40e9-459b-b711-f32ad76dfc28");
    private static readonly Guid AdapterIid = new("f0db4c7f-fe5a-42a2-bd62-f2a6cf6fc83e");
    private static readonly Guid D3D12Graphics = new("0c9ece4d-2f6e-4f01-8c96-e89e331b47b1");
    private static readonly StrategyBasedComWrappers ComWrappers = new();
    private IDxCoreAdapterFactory? _factory;
    private IDxCoreAdapterList? _list;

    public IReadOnlyList<GpuAdapterCandidate> Enumerate()
    {
        if (_factory is null)
        {
            Check(DXCoreCreateAdapterFactory(FactoryIid, out var pointer));
            _factory = Wrap<IDxCoreAdapterFactory>(pointer);
        }

        if (_list is not null)
        {
            Release(_list);
            _list = null;
        }

        Check(_factory.CreateAdapterList(1, D3D12Graphics, ListIid, out var listPointer));
        _list = Wrap<IDxCoreAdapterList>(listPointer);
        var result = new List<GpuAdapterCandidate>(checked((int)_list.GetAdapterCount()));
        for (uint index = 0; index < _list.GetAdapterCount(); index++)
        {
            Check(_list.GetAdapter(index, AdapterIid, out var adapterPointer));
            var adapter = Wrap<IDxCoreAdapter>(adapterPointer);
            try
            {
                var luid = ReadProperty<NativeLuid>(adapter, DxCoreAdapterProperty.InstanceLuid);
                result.Add(new GpuAdapterCandidate(
                    ReadDescription(adapter),
                    new AdapterLuid(luid.LowPart, luid.HighPart),
                    ReadProperty<byte>(adapter, DxCoreAdapterProperty.IsHardware) != 0,
                    ReadProperty<byte>(adapter, DxCoreAdapterProperty.IsIntegrated) != 0,
                    adapter.IsPropertySupported(DxCoreAdapterProperty.IsDetachable)
                        && ReadProperty<byte>(adapter, DxCoreAdapterProperty.IsDetachable) != 0));
            }
            finally
            {
                Release(adapter);
            }
        }

        return result;
    }

    public bool IsStale() => _list?.IsStale() ?? true;

    public void Dispose()
    {
        if (_list is not null) Release(_list);
        if (_factory is not null) Release(_factory);
        _list = null;
        _factory = null;
    }

    private static T Wrap<T>(nint pointer) where T : class
    {
        try
        {
            return (T)ComWrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.UniqueInstance);
        }
        finally
        {
            // The generated wrapper owns a separate AddRef of the returned interface pointer.
            _ = Marshal.Release(pointer);
        }
    }

    private static void Release(object value) => ((ComObject)value).FinalRelease();

    private static string ReadDescription(IDxCoreAdapter adapter)
    {
        var size = PropertySize(adapter, DxCoreAdapterProperty.DriverDescription);
        var buffer = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            Check(adapter.GetProperty(DxCoreAdapterProperty.DriverDescription, size, buffer));
            return Marshal.PtrToStringUTF8(buffer) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static T ReadProperty<T>(IDxCoreAdapter adapter, DxCoreAdapterProperty property) where T : unmanaged
    {
        var size = PropertySize(adapter, property);
        if (size != (nuint)Marshal.SizeOf<T>())
        {
            throw new InvalidOperationException($"DXCore property {property} has unexpected size.");
        }

        var buffer = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            Check(adapter.GetProperty(property, size, buffer));
            return Marshal.PtrToStructure<T>(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static nuint PropertySize(IDxCoreAdapter adapter, DxCoreAdapterProperty property)
    {
        Check(adapter.GetPropertySize(property, out var size));
        return size;
    }

    private static void Check(int hresult)
    {
        if (hresult < 0) Marshal.ThrowExceptionForHR(hresult);
    }

    [LibraryImport("dxcore.dll", EntryPoint = "DXCoreCreateAdapterFactory")]
    private static partial int DXCoreCreateAdapterFactory(in Guid iid, out nint factory);

    [GeneratedComInterface]
    [Guid("78ee5945-c36e-4b13-a669-005dd11c0f06")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal partial interface IDxCoreAdapterFactory
    {
        [PreserveSig] int CreateAdapterList(uint count, in Guid attributes, in Guid iid, out nint list);
    }

    [GeneratedComInterface]
    [Guid("526c7776-40e9-459b-b711-f32ad76dfc28")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal partial interface IDxCoreAdapterList
    {
        [PreserveSig] int GetAdapter(uint index, in Guid iid, out nint adapter);
        [PreserveSig] uint GetAdapterCount();
        [PreserveSig]
        [return: MarshalAs(UnmanagedType.U1)] bool IsStale();
    }

    [GeneratedComInterface]
    [Guid("f0db4c7f-fe5a-42a2-bd62-f2a6cf6fc83e")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal partial interface IDxCoreAdapter
    {
        [PreserveSig]
        [return: MarshalAs(UnmanagedType.U1)] bool IsValid();
        [PreserveSig]
        [return: MarshalAs(UnmanagedType.U1)] bool IsAttributeSupported(in Guid attribute);
        [PreserveSig]
        [return: MarshalAs(UnmanagedType.U1)] bool IsPropertySupported(DxCoreAdapterProperty property);
        [PreserveSig] int GetProperty(DxCoreAdapterProperty property, nuint bufferSize, nint buffer);
        [PreserveSig] int GetPropertySize(DxCoreAdapterProperty property, out nuint bufferSize);
    }

    internal enum DxCoreAdapterProperty : uint { InstanceLuid = 0, DriverDescription = 2, IsHardware = 11, IsIntegrated = 12, IsDetachable = 13 }
}
