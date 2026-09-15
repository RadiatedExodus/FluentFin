using System.Collections.ObjectModel;
using FluentFin.Core.Contracts.Services;
using FluentFin.Core.Playback;

namespace FluentFin.Core.Settings;

public static class SettingKeys
{
	public static Key<ObservableCollection<SavedServer>> Servers { get; } = new Key<ObservableCollection<SavedServer>>("Servers", []);
	public static Key<MediaPlayerType> MediaPlayerType { get; } = new Key<MediaPlayerType>("MediaPlayerType", FluentFin.Core.Playback.MediaPlayerType.WindowsMediaPlayer);
}

