using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Windows.Input;
using CommunityToolkit.WinUI;
using FluentFin.Core.Contracts.Services;
using FluentFin.Core.ViewModels;
using FluentFin.MediaPlayers;
using FluentFin.Playback;
using FluentFin.ViewModels;
using FlyleafLib.Controls.WinUI;
using LibVLCSharp.Platforms.Windows;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReactiveUI;


namespace FluentFin.Controls;

public sealed partial class MediaPlayerHost : UserControl
{
	private readonly Subject<Unit> _pointerMoved = new();
	private static bool _flyleafStarted;

	[GeneratedDependencyProperty]
	public partial bool IsSkipButtonVisible { get; set; }

	[GeneratedDependencyProperty]
	public partial PlaylistViewModel? Playlist { get; set; }

	[GeneratedDependencyProperty]
	public partial ICommand? SkipCommand { get; set; }

	[GeneratedDependencyProperty]
	public partial TrickplayViewModel? Trickplay { get; set; }

	[GeneratedDependencyProperty]
	public partial MediaPlayerType? MediaPlayerType { get; set; }

	[GeneratedDependencyProperty]
	public partial IJellyfinClient? JellyfinClient { get; set; }

	public IMediaPlayerController? Player
	{
		get
		{
			try
			{
				return (IMediaPlayerController)GetValue(PlayerProperty);
			}
			catch
			{
				return null;
			}
		}
		set { SetValue(PlayerProperty, value); }
	}

	public static readonly DependencyProperty PlayerProperty =
		DependencyProperty.Register("Player", typeof(IMediaPlayerController), typeof(MediaPlayerHost), new PropertyMetadata(null));

	public MediaPlayerHost()
	{
		InitializeComponent();

		this.WhenAnyValue(x => x.MediaPlayerType)
			.WhereNotNull()
			.ObserveOn(RxApp.MainThreadScheduler)
			.Subscribe(type =>
			{
				if (RootGrid.Children.Count > 1)
				{
					RootGrid.Children.RemoveAt(0);
				}

				UIElement host = type switch
				{
					Core.Contracts.Services.MediaPlayerType.Vlc => CreateVLC(),
					Core.Contracts.Services.MediaPlayerType.Flyleaf => CreateFlyleaf(),
					Core.Contracts.Services.MediaPlayerType.WindowsMediaPlayer => CreateWindowsMediaPlayer(),
					Core.Contracts.Services.MediaPlayerType.Mpv => CreateMpv(),
					_ => throw new NotImplementedException()
				};

				RootGrid.Children.Insert(0, host);
			});

		_pointerMoved
			.Throttle(TimeSpan.FromSeconds(3))
			.Subscribe(_ =>
			{
				TransportControls.Bar.DispatcherQueue.TryEnqueue(() =>
				{
					TransportControls.Bar.Visibility = Visibility.Collapsed;
					TransportControls.TitleSection.Visibility = Visibility.Collapsed;
					ProtectedCursor.Dispose();
				});
			});

		TransportControls!.FullWindowButton.Click += (sender, e) => OnPlayerDoubleTapped(sender, null!);
	}

	private MediaPlayerElement CreateWindowsMediaPlayer()
	{
		var element = new MediaPlayerElement();
		element.Loaded += Element_Loaded;
		return element;
	}

	private FlyleafHost CreateFlyleaf()
	{
		var host = new FlyleafHost();
		host.Loaded += Host_Loaded;
		return host;
	}

	private VideoView CreateVLC()
	{
		var view = new VideoView();
		view.Initialized += VLCInitialized;
		return view;
	}

	private MpvVideoView CreateMpv()
	{
		var view = new MpvVideoView();
		var player = App.GetService<PlaybackServiceMediaPlayerControllerAdapter>();
		player.MediaLoaded
			.ObserveOn(RxApp.MainThreadScheduler)
			.Subscribe(_ => RefreshMpvAudioTracksFlyout(player));
		Player = player;
		return view;
	}

	private void RefreshMpvAudioTracksFlyout(IMediaPlayerController player)
	{
		var flyout = Converters.Converters.GetAudiosFlyout(player, player.AudioTrackIndex ?? -1);
		TransportControls.AudioSelectionButton.Flyout = flyout;
		TransportControls.AudioSelectionButton.Visibility = flyout is null ? Visibility.Collapsed : Visibility.Visible;
	}

	private void VLCInitialized(object? sender, InitializedEventArgs e)
	{
		if (sender is not VideoView view)
		{
			return;
		}

		DispatcherQueue.TryEnqueue(() =>
		{
			var player = new VlcMediaPlayerController(view, e.SwapChainOptions, TransportControls.AudioSelectionButton);
			RegisterPlaybackEngine(Core.Contracts.Services.MediaPlayerType.Vlc, player);
			Player = player;
		});
	}

	private void Host_Loaded(object sender, RoutedEventArgs e)
	{
		if (sender is not FlyleafHost host)
		{
			return;
		}

		DispatcherQueue.TryEnqueue(() =>
		{
			if (!_flyleafStarted)
			{
				App.StartFlyleaf();
				_flyleafStarted = true;
			}

			var player = new FlyleafMediaPlayerController(host, TransportControls.AudioSelectionButton);
			RegisterPlaybackEngine(Core.Contracts.Services.MediaPlayerType.Flyleaf, player);
			Player = player;
		});
	}

	private void Element_Loaded(object sender, RoutedEventArgs e)
	{
		if (sender is not MediaPlayerElement element)
		{
			return;
		}

		DispatcherQueue.TryEnqueue(() =>
		{
			var player = new WindowsMediaPlayerController(element, TransportControls.AudioSelectionButton);
			RegisterPlaybackEngine(Core.Contracts.Services.MediaPlayerType.WindowsMediaPlayer, player);
			Player = player;
		});
	}

	private void RegisterPlaybackEngine(Core.Contracts.Services.MediaPlayerType type, IMediaPlayerController player)
	{
		if (player is null)
		{
			return;
		}

		App.GetService<IHostedPlaybackEngineRegistry>().RegisterHostedPlayer(type, player);
	}


	private void FSC_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
	{
		ShowTransportControls();
	}

	private void ShowTransportControls()
	{
		TransportControls.Bar.DispatcherQueue.TryEnqueue(() =>
		{
			TransportControls.Bar.Visibility = Visibility.Visible;
			TransportControls.TitleSection.Visibility = Visibility.Visible;
			TransportControls.TxtTitleTime.Text = DateTime.Now.ToString("hh:mm tt");
			ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
		});

		_pointerMoved.OnNext(Unit.Default);
	}

	public void OnPlayerDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
	{
		var current = App.MainWindow.AppWindow.Presenter.Kind;
		var presenterKind = current == AppWindowPresenterKind.Overlapped ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Overlapped;

		TransportControls.FullWindowSymbol.Symbol = presenterKind == AppWindowPresenterKind.FullScreen ? Symbol.BackToWindow : Symbol.FullScreen;

		if (this.FindAscendant<NavigationView>() is { } navView)
		{
			navView.IsPaneVisible ^= true;
		}

		App.MainWindow.AppWindow.SetPresenter(presenterKind);
	}
}
