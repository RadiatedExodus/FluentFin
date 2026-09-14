namespace FluentFin.Services;

public interface IWindowsMediaSessionService : IDisposable
{
	void Initialize();
	void Clear();
}
