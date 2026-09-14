using FluentFin.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace FluentFin.Controls;

public sealed partial class MusicQueuePanel : UserControl
{
	public MusicPlaybackViewModel ViewModel { get; } = App.GetService<MusicPlaybackViewModel>();

	public MusicQueuePanel()
	{
		InitializeComponent();
	}

	private void QueueItem_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
	{
		if ((sender as FrameworkElement)?.DataContext is not MusicQueueItemViewModel item)
		{
			return;
		}

		ViewModel.JumpToQueueItemCommand.Execute(item);
		e.Handled = true;
	}

	private void QueueItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
	{
		if ((sender as FrameworkElement)?.DataContext is not MusicQueueItemViewModel item)
		{
			return;
		}

		var flyout = new MenuFlyout();
		flyout.Items.Add(new MenuFlyoutItem
		{
			Text = "Play",
			Icon = new SymbolIcon { Symbol = Symbol.Play },
			Command = ViewModel.JumpToQueueItemCommand,
			CommandParameter = item
		});
		flyout.Items.Add(new MenuFlyoutItem
		{
			Text = "Remove",
			Icon = new SymbolIcon { Symbol = Symbol.Delete },
			Command = ViewModel.RemoveQueueItemCommand,
			CommandParameter = item
		});

		flyout.ShowAt((FrameworkElement)sender, e.GetPosition((FrameworkElement)sender));
		e.Handled = true;
	}
}
