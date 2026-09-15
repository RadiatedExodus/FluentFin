namespace FluentFin.Mpv;

public sealed class MpvOptions
{
	public string VideoOutput { get; init; } = "gpu-next";
	public string GpuApi { get; init; } = "d3d11";
	public string GpuContext { get; init; } = "d3d11";
	public string D3D11OutputMode { get; init; } = "composition";
	public string HardwareDecoding { get; init; } = "auto-safe";
}
