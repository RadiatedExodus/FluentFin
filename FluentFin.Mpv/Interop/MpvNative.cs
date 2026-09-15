using System.Runtime.InteropServices;

namespace FluentFin.Mpv.Interop;

internal static partial class MpvNative
{
	[LibraryImport("libmpv-2", EntryPoint = "mpv_create")]
	internal static partial nint Create();

	[LibraryImport("libmpv-2", EntryPoint = "mpv_initialize")]
	internal static partial int Initialize(nint handle);

	[LibraryImport("libmpv-2", EntryPoint = "mpv_terminate_destroy")]
	internal static partial void TerminateDestroy(nint handle);

	[LibraryImport("libmpv-2", EntryPoint = "mpv_error_string", StringMarshalling = StringMarshalling.Utf8)]
	internal static partial string ErrorString(int error);

	[LibraryImport("libmpv-2", EntryPoint = "mpv_command")]
	internal static partial int Command(nint handle, nint args);

	[LibraryImport("libmpv-2", EntryPoint = "mpv_wait_event")]
	internal static partial nint WaitEvent(nint handle, double timeout);

	[LibraryImport("libmpv-2", EntryPoint = "mpv_observe_property", StringMarshalling = StringMarshalling.Utf8)]
	internal static partial int ObserveProperty(nint handle, ulong replyUserData, string name, MpvFormat format);

	[LibraryImport("libmpv-2", EntryPoint = "mpv_unobserve_property")]
	internal static partial int UnobserveProperty(nint handle, ulong registeredReplyUserData);

	[LibraryImport("libmpv-2", EntryPoint = "mpv_get_property", StringMarshalling = StringMarshalling.Utf8)]
	internal static partial int GetProperty(nint handle, string name, MpvFormat format, nint data);

	[LibraryImport("libmpv-2", EntryPoint = "mpv_set_property", StringMarshalling = StringMarshalling.Utf8)]
	internal static partial int SetProperty(nint handle, string name, MpvFormat format, nint data);

	[LibraryImport("libmpv-2", EntryPoint = "mpv_set_property_string", StringMarshalling = StringMarshalling.Utf8)]
	internal static partial int SetPropertyString(nint handle, string name, string value);

	[LibraryImport("libmpv-2", EntryPoint = "mpv_free")]
	internal static partial void Free(nint data);
}
