using FluentFin.Contracts.Services;
using FluentFin.Controls;
using FluentFin.Core;
using FluentFin.Core.ViewModels;
using FluentFin.Playback.Presentation;
using FluentFin.Views;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace FluentFin;

public sealed partial class MainWindow : WindowEx
{
	private readonly IPlaybackPresentationManager _playbackPresentationManager;
	private readonly ILogger<MainWindow> _logger;
	private VideoPlayerPage? _videoPlayerPage;
	private MusicMiniPlayer? _musicMiniPlayer;
	private MusicExpandedPlayer? _musicExpandedPlayer;
	private MusicQueuePanel? _musicQueuePanel;
	private int _activeVideoRequestId;
	private int _playbackPresentationUpdateQueued;

	public IMainWindowViewModel ViewModel { get; } = App.GetService<IMainWindowViewModel>();

	public MainWindow()
	{
		_playbackPresentationManager = App.GetService<IPlaybackPresentationManager>();
		_logger = App.GetService<ILogger<MainWindow>>();
		InitializeComponent();
		ExtendsContentIntoTitleBar = true;
		AppWindow.SetIcon("Assets/jellyfin.ico");

		App.GetKeyedService<INavigationService>(NavigationRegions.InitialSetup).Frame = RootFrame;
		_playbackPresentationManager.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName is nameof(IPlaybackPresentationManager.Mode) or nameof(IPlaybackPresentationManager.MusicMode))
			{
				QueuePlaybackPresentationUpdate();
			}
		};

		RootGrid.KeyboardAccelerators.Add(BuildKeyboardAccelerator(VirtualKey.Left, VirtualKeyModifiers.Menu));
		RootGrid.KeyboardAccelerators.Add(BuildKeyboardAccelerator(VirtualKey.GoBack));
	}

	private static KeyboardAccelerator BuildKeyboardAccelerator(VirtualKey key, VirtualKeyModifiers? modifiers = null)
	{
		var keyboardAccelerator = new KeyboardAccelerator() { Key = key };

		if (modifiers.HasValue)
		{
			keyboardAccelerator.Modifiers = modifiers.Value;
		}

		keyboardAccelerator.Invoked += OnKeyboardAcceleratorInvoked;

		return keyboardAccelerator;
	}

	private void QueuePlaybackPresentationUpdate()
	{
		if (Interlocked.Exchange(ref _playbackPresentationUpdateQueued, 1) == 1)
		{
			return;
		}

		if (DispatcherQueue.HasThreadAccess)
		{
			_ = UpdatePlaybackPresentationAsync();
			return;
		}

		if (!DispatcherQueue.TryEnqueue(() => _ = UpdatePlaybackPresentationAsync()))
		{
			Interlocked.Exchange(ref _playbackPresentationUpdateQueued, 0);
		}
	}

	private static void OnKeyboardAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
	{
		var playbackPresentationManager = App.GetService<IPlaybackPresentationManager>();
		if (playbackPresentationManager.Mode is PlaybackPresentationMode.VideoOverlay ||
			playbackPresentationManager.MusicMode is MusicPresentationMode.Expanded or MusicPresentationMode.Queue)
		{
			_ = playbackPresentationManager.HideAsync();
			args.Handled = true;
		}
	}

	private async Task UpdatePlaybackPresentationAsync()
	{
		try
		{
			Interlocked.Exchange(ref _playbackPresentationUpdateQueued, 0);

			if (_playbackPresentationManager.Mode is PlaybackPresentationMode.VideoOverlay &&
				_playbackPresentationManager.VideoOverlay is { } state)
			{
				_logger.LogInformation("Activating full-window video playback overlay. RequestId={RequestId}, ParameterType={ParameterType}",
					state.RequestId,
					state.Parameter?.GetType().FullName ?? "<null>");
				PlaybackPresenter.Visibility = Visibility.Visible;
				MusicOverlayPresenter.Visibility = Visibility.Collapsed;
				MusicMiniPlayerPresenter.Visibility = Visibility.Collapsed;
				RootFrame.Margin = new Thickness(0);

				if (_activeVideoRequestId != state.RequestId)
				{
					if (_videoPlayerPage is not null)
					{
						await _videoPlayerPage.DeactivateAsync();
					}

					_videoPlayerPage = new VideoPlayerPage();
					PlaybackPresenter.Content = _videoPlayerPage;
					_activeVideoRequestId = state.RequestId;
					await _videoPlayerPage.ActivateAsync(state.Parameter);
				}

				return;
			}

			if (_videoPlayerPage is not null)
			{
				await _videoPlayerPage.DeactivateAsync();
				_videoPlayerPage = null;
				_activeVideoRequestId = 0;
			}

			PlaybackPresenter.Content = null;
			PlaybackPresenter.Visibility = Visibility.Collapsed;
			UpdateMusicPresentation();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to update playback presentation. Mode={Mode}", _playbackPresentationManager.Mode);
		}
	}

	private void UpdateMusicPresentation()
	{
		switch (_playbackPresentationManager.MusicMode)
		{
			case MusicPresentationMode.Expanded:
				_musicExpandedPlayer ??= new MusicExpandedPlayer();
				MusicOverlayPresenter.Content = _musicExpandedPlayer;
				MusicOverlayPresenter.Visibility = Visibility.Visible;
				MusicMiniPlayerPresenter.Visibility = Visibility.Collapsed;
				RootFrame.Margin = new Thickness(0);
				break;
			case MusicPresentationMode.Queue:
				_musicQueuePanel ??= new MusicQueuePanel();
				_musicMiniPlayer ??= new MusicMiniPlayer();
				MusicOverlayPresenter.Content = _musicQueuePanel;
				MusicMiniPlayerPresenter.Content = _musicMiniPlayer;
				MusicOverlayPresenter.Visibility = Visibility.Visible;
				MusicMiniPlayerPresenter.Visibility = Visibility.Visible;
				RootFrame.Margin = new Thickness(0, 0, 0, 96);
				break;
			case MusicPresentationMode.Compact:
				_musicMiniPlayer ??= new MusicMiniPlayer();
				MusicOverlayPresenter.Content = null;
				MusicMiniPlayerPresenter.Content = _musicMiniPlayer;
				MusicOverlayPresenter.Visibility = Visibility.Collapsed;
				MusicMiniPlayerPresenter.Visibility = Visibility.Visible;
				RootFrame.Margin = new Thickness(0, 0, 0, 96);
				break;
			default:
				MusicOverlayPresenter.Content = null;
				MusicMiniPlayerPresenter.Content = null;
				MusicOverlayPresenter.Visibility = Visibility.Collapsed;
				MusicMiniPlayerPresenter.Visibility = Visibility.Collapsed;
				RootFrame.Margin = new Thickness(0);
				break;
		}
	}
}
