using System.Runtime.InteropServices;
using FluentFin.Mpv;

namespace FluentFin.Mpv.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct MpvEvent
{
	public MpvEventId EventId;
	public int Error;
	public ulong ReplyUserData;
	public nint Data;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvEventProperty
{
	public nint Name;
	public MpvFormat Format;
	public nint Data;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MpvEventEndFile
{
	public MpvEndFileReason Reason;
	public int Error;
	public ulong PlaylistEntryId;
	public int PlaylistInsertId;
	public int PlaylistInsertNumEntries;
}
