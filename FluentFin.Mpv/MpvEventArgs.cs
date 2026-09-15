namespace FluentFin.Mpv;

public enum MpvEndFileReason
{
	Eof = 0,
	Stop = 2,
	Quit = 3,
	Error = 4,
	Redirect = 5
}

public sealed class MpvPropertyChangedEventArgs(string name, object? value) : EventArgs
{
	public string Name { get; } = name;
	public object? Value { get; } = value;
}

public sealed class MpvEndFileEventArgs(MpvEndFileReason reason, int errorCode) : EventArgs
{
	public MpvEndFileReason Reason { get; } = reason;
	public int ErrorCode { get; } = errorCode;
}
