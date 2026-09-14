using FluentFin.Contracts.Services;
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
	private int _activeVideoRequestId;

	public IMainWindowViewModel ViewModel { get; } = App.GetService<IMainWindowViewModel>();

	public MainWindow()
	{
		_playbackPresentationManager = App.GetService<IPlaybackPresentationManager>();
		_logger = App.GetService<ILogger<MainWindow>>();
		InitializeComponent();
		ExtendsContentIntoTitleBar = true;
		AppWindow.SetIcon("Assets/jellyfin.ico");

		App.GetKeyedService<INavigationService>(NavigationRegions.InitialSetup).Frame = RootFrame;
		_playbackPresentationManager.PropertyChanged += async (_, e) =>
		{
			if (e.PropertyName is nameof(IPlaybackPresentationManager.Mode))
			{
				await UpdatePlaybackPresentationAsync();
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

	private static void OnKeyboardAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
	{
		var playbackPresentationManager = App.GetService<IPlaybackPresentationManager>();
		if (playbackPresentationManager.HasActivePresentation)
		{
			_ = playbackPresentationManager.HideAsync();
			args.Handled = true;
		}
	}

	private async Task UpdatePlaybackPresentationAsync()
	{
		try
		{
			if (_playbackPresentationManager.Mode is PlaybackPresentationMode.VideoOverlay &&
				_playbackPresentationManager.VideoOverlay is { } state)
			{
				_logger.LogInformation("Activating full-window video playback overlay. RequestId={RequestId}, ParameterType={ParameterType}",
					state.RequestId,
					state.Parameter?.GetType().FullName ?? "<null>");
				PlaybackPresenter.Visibility = Visibility.Visible;

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
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to update playback presentation. Mode={Mode}", _playbackPresentationManager.Mode);
		}
	}
}
