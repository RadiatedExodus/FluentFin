namespace FluentFin.Mpv;

public sealed class MpvPropertyChangedEventArgs(string name, object? value) : EventArgs
{
	public string Name { get; } = name;
	public object? Value { get; } = value;
}

public sealed class MpvEndFileEventArgs(string reason, int errorCode) : EventArgs
{
	public string Reason { get; } = reason;
	public int ErrorCode { get; } = errorCode;
}
