namespace FluentFin.Playback;

public interface IVideoOutputEngine
{
	nint VideoSwapChain { get; }

	event EventHandler? VideoOutputChanged;

	Task SetVideoOutputSizeAsync(uint width, uint height, CancellationToken cancellationToken = default);
}
