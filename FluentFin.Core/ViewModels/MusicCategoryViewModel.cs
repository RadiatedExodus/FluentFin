using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FluentFin.Core.ViewModels;

public sealed partial class MusicCategoryViewModel(
	string title,
	MusicAlbumCategoryKind kind,
	Guid? parentId,
	ICommand headerCommand,
	bool showWhenEmpty = false) : ObservableObject
{
	public string Title { get; } = title;

	public MusicAlbumCategoryKind Kind { get; } = kind;

	public Guid? ParentId { get; } = parentId;

	public ICommand HeaderCommand { get; } = headerCommand;

	public bool ShowWhenEmpty { get; } = showWhenEmpty;

	public ObservableCollection<BaseItemViewModel> Items { get; } = [];

	public bool HasItems => Items.Count > 0;

	[ObservableProperty]
	public partial bool IsLoading { get; set; }
}
