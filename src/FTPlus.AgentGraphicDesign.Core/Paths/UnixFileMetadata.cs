using System.Runtime.InteropServices;

namespace FTPlus.AgentGraphicDesign.Core.Paths;

/// <summary>The .NET 10 PAL layout, not a platform-specific native stat layout.</summary>
internal static class UnixFileMetadata
{
    internal const int TypeMask = 0xF000;
    internal const int DirectoryType = 0x4000;
    internal const int RegularType = 0x8000;
    internal const int LinkType = 0xA000;

    // dotnet/runtime v10.0.14: Common/src/Interop/Unix/System.Native/Interop.Stat.cs.
    [DllImport("System.Native", EntryPoint = "SystemNative_LStat", SetLastError = true)]
    internal static extern int LStat([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out Status status);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Status
    {
        public int Flags;
        public int Mode;
        public uint Uid;
        public uint Gid;
        public long Size;
        public long ATime;
        public long ATimeNsec;
        public long MTime;
        public long MTimeNsec;
        public long CTime;
        public long CTimeNsec;
        public long BirthTime;
        public long BirthTimeNsec;
        public long Dev;
        public long RDev;
        public long Ino;
        public uint UserFlags;
    }
}
