using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

internal static class HandleProbe
{
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(nint process, int infoClass, nint buffer, int length, out int returned);
    [DllImport("ntdll.dll")] private static extern int NtQueryObject(nint handle, int infoClass, nint buffer, int length, out int returned);
    [StructLayout(LayoutKind.Sequential)] private struct Entry
    { internal nuint Handle, HandleCount, PointerCount; internal uint Access, TypeIndex, Attributes, Reserved; }
    internal static Dictionary<string, int> Capture(bool includeNames = false)
    {
        var result = new Dictionary<string, int>();
        var length = 65536; var buffer = Marshal.AllocHGlobal(length); var type = Marshal.AllocHGlobal(2048);
        try
        {
            int status;
            while ((status = NtQueryInformationProcess(-1, 51, buffer, length, out var needed)) != 0)
            {
                if (length > 16 * 1024 * 1024) throw new InvalidOperationException("Handle snapshot too large " + status);
                Marshal.FreeHGlobal(buffer); length = Math.Max(length * 2, needed); buffer = Marshal.AllocHGlobal(length);
            }
            var count = Marshal.ReadIntPtr(buffer).ToInt64(); var size = Marshal.SizeOf<Entry>();
            for (var i = 0; i < count; i++)
            {
                var entry = Marshal.PtrToStructure<Entry>(buffer + IntPtr.Size * 2 + (int)i * size);
                if (NtQueryObject((nint)entry.Handle, 2, type, 2048, out _) != 0) continue;
                var name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(type, 8), (ushort)Marshal.ReadInt16(type) / 2) ?? "unknown";
                result[name] = result.GetValueOrDefault(name) + 1;
                if (includeNames && name is "Mutant" or "Section" && NtQueryObject((nint)entry.Handle, 1, type, 2048, out _) == 0)
                {
                    var pointer = Marshal.ReadIntPtr(type, 8); var label = pointer == 0 ? "unnamed" : Marshal.PtrToStringUni(pointer, (ushort)Marshal.ReadInt16(type) / 2) ?? "unnamed";
                    result[name + ":" + label] = result.GetValueOrDefault(name + ":" + label) + 1;
                }
            }
            return result;
        }
        finally { Marshal.FreeHGlobal(buffer); Marshal.FreeHGlobal(type); }
    }
}