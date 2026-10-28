using System;
using System.Runtime.InteropServices;
using Inv = global::Inventor;

namespace OpenInventorExport.Inventor;

public static class InventorConnection
{
    [DllImport("ole32.dll")]
    private static extern int CLSIDFromProgID(
        [MarshalAs(UnmanagedType.LPWStr)] string progId, out Guid clsid);

    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(
        ref Guid clsid, IntPtr reserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object obj);

    public static Inv.Application GetRunningInventor()
    {
        int hr = CLSIDFromProgID("Inventor.Application", out Guid clsid);
        if (hr != 0) Marshal.ThrowExceptionForHR(hr);

        GetActiveObject(ref clsid, IntPtr.Zero, out object obj);
        return (Inv.Application)obj;
    }
}