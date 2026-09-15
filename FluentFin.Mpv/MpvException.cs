namespace FluentFin.Mpv;

public sealed class MpvException : Exception
{
	public int ErrorCode { get; }

	public MpvException(int errorCode, string message)
		: base(message)
	{
		ErrorCode = errorCode;
	}
}
