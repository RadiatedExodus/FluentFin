using Microsoft.Win32.SafeHandles;

namespace FluentFin.Mpv.Interop;

internal sealed class SafeMpvHandle : SafeHandleZeroOrMinusOneIsInvalid
{
	public SafeMpvHandle()
		: base(true)
	{
		SetHandle(MpvNative.Create());
	}

	protected override bool ReleaseHandle()
	{
		MpvNative.TerminateDestroy(handle);
		return true;
	}
}
