using FluentFin.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace FluentFin.Views;

public sealed partial class MusicPlaylistPage : Page
{
	public MusicPlaylistViewModel ViewModel { get; } = App.GetService<MusicPlaylistViewModel>();

	public MusicPlaylistPage()
	{
		InitializeComponent();
	}

	private async void Track_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
	{
		if ((sender as FrameworkElement)?.DataContext is not MusicAlbumTrackViewModel track)
		{
			return;
		}

		await ViewModel.PlayFromTrack(track.Dto);
	}

	private async void RenamePlaylist_Click(object sender, RoutedEventArgs e)
	{
		var textBox = new TextBox
		{
			Text = ViewModel.PlaylistName,
			PlaceholderText = "Playlist name"
		};
		textBox.Loaded += (_, _) =>
		{
			textBox.Focus(FocusState.Programmatic);
			textBox.SelectAll();
		};

		var dialog = new ContentDialog
		{
			Title = "Rename playlist",
			Content = textBox,
			PrimaryButtonText = "Save",
			CloseButtonText = "Cancel",
			DefaultButton = ContentDialogButton.Primary,
			XamlRoot = XamlRoot
		};

		dialog.Closing += (_, args) =>
		{
			if (args.Result == ContentDialogResult.Primary && string.IsNullOrWhiteSpace(textBox.Text))
			{
				args.Cancel = true;
			}
		};

		if (await dialog.ShowAsync() == ContentDialogResult.Primary)
		{
			await ViewModel.RenamePlaylist(textBox.Text);
		}
	}

	private async void DeletePlaylist_Click(object sender, RoutedEventArgs e)
	{
		var dialog = new ContentDialog
		{
			Title = "Delete playlist",
			Content = $"Delete \"{ViewModel.PlaylistName}\"? This removes the playlist, not the audio files.",
			PrimaryButtonText = "Delete",
			CloseButtonText = "Cancel",
			DefaultButton = ContentDialogButton.Close,
			XamlRoot = XamlRoot
		};

		if (await dialog.ShowAsync() == ContentDialogResult.Primary)
		{
			await ViewModel.DeletePlaylist();
		}
	}

	private async void RemoveTrack_Click(object sender, RoutedEventArgs e)
	{
		if ((sender as FrameworkElement)?.Tag is not MusicAlbumTrackViewModel track)
		{
			return;
		}

		await ViewModel.RemoveTrackCommand.ExecuteAsync(track);
	}
}
