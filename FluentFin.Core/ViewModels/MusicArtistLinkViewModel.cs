using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace FluentFin.Core.ViewModels;

public sealed class MusicArtistLinkViewModel(string name, Guid? id, Func<string, Guid?, Task> openArtist)
{
	public string Name { get; } = name;

	public Guid? Id { get; } = id;

	public ICommand OpenCommand { get; } = new AsyncRelayCommand(() => openArtist(name, id));
}
