using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentFin.Contracts.Services;
using FluentFin.Core;
using FluentFin.Core.Contracts.Services;
using FluentFin.Core.Services;
using FluentFin.Core.Settings;
using FluentFin.Core.ViewModels;
using FluentFin.Playback.Presentation;
using FluentFin.UI.Core.Contracts.Services;
using FluentFin.Views;
using FluentFin.Views.JellyfinSettings;
using Jellyfin.Sdk.Generated.Models;
using Microsoft.Extensions.DependencyInjection;

namespace FluentFin.ViewModels;

public partial class TitleBarViewModel : ObservableObject, ITitleBarViewModel
{
	private readonly INavigationService _navigationService;
	private readonly INavigationService _setupNavigationService;
	private readonly INavigationViewService _navigationViewService;
	private readonly IJellyfinClient _jellyfinClient;
	private readonly IPlaybackPresentationManager _playbackPresentationManager;
	private Type? _currentPageType;

	public TitleBarViewModel(INavigationService navigationService,
							 [FromKeyedServices(NavigationRegions.InitialSetup)] INavigationService setupNavigationService,
							 INavigationViewService navigationViewService,
							 IJellyfinClient jellyfinClient,
							 IPlaybackPresentationManager playbackPresentationManager)
	{
		_navigationService = navigationService;
		_setupNavigationService = setupNavigationService;
		_navigationViewService = navigationViewService;
		_jellyfinClient = jellyfinClient;
		_playbackPresentationManager = playbackPresentationManager;

		navigationService.Navigated += NavigationService_Navigated;
		playbackPresentationManager.PropertyChanged += PlaybackPresentationManager_PropertyChanged;
	}

	[ObservableProperty]
	public partial string Title { get; set; } = "";

	[ObservableProperty]
	public partial string Version { get; set; } = Assembly.GetEntryAssembly()!.GetName().Version!.ToString();

	[ObservableProperty]
	public partial bool CanGoBack { get; set; }

	[ObservableProperty]
	public partial bool IsBackButtonVisible { get; set; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsSearchAndProfileVisible))]
	public partial bool IsOverlayChromeMode { get; set; }

	public bool IsSearchAndProfileVisible => !IsOverlayChromeMode;

	[ObservableProperty]
	public partial UserDto? User { get; set; }

	[ObservableProperty]
	public partial bool IsVisible { get; set; } = true;

	[ObservableProperty]
	public partial SavedServer? CurrentServer { get; set; }

	public void TogglePane()
	{
		_navigationViewService.TogglePane();
	}

	public void GoBack()
	{
		if (HasDismissablePresentation())
		{
			_ = _playbackPresentationManager.HideAsync();
			RefreshCanGoBack();
			return;
		}

		_navigationService.GoBack();
	}

	[RelayCommand]
	public async Task Logout()
	{
		User = null;
		_setupNavigationService.NavigateTo<SelectServerViewModel>();
		await _jellyfinClient.Logout();
	}

	[RelayCommand]
	public async Task SwitchUser()
	{
		User = null;
		_setupNavigationService.NavigateTo<LoginViewModel>(CurrentServer);
		await _jellyfinClient.Logout();
	}


	[RelayCommand]
	private void GoToDashboard()
	{
		_navigationService.NavigateTo(typeof(JellyfinSettingsViewModel).FullName!, new object());
	}

	private void NavigationService_Navigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
	{
		_currentPageType = e.SourcePageType;
		RefreshCanGoBack();
	}

	private void PlaybackPresentationManager_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(IPlaybackPresentationManager.Mode) or nameof(IPlaybackPresentationManager.MusicMode) or nameof(IPlaybackPresentationManager.HasActivePresentation))
		{
			EnqueueRefreshCanGoBack();
		}
	}

	private void EnqueueRefreshCanGoBack()
	{
		var dispatcherQueue = App.MainWindow.DispatcherQueue;
		if (dispatcherQueue.HasThreadAccess)
		{
			RefreshCanGoBack();
			return;
		}

		dispatcherQueue.TryEnqueue(RefreshCanGoBack);
	}

	private void RefreshCanGoBack()
	{
		var hasDismissablePresentation = HasDismissablePresentation();
		CanGoBack = _navigationService.CanGoBack || hasDismissablePresentation;
		IsBackButtonVisible = hasDismissablePresentation || IsChildNavigationPage();
	}

	private bool HasDismissablePresentation() =>
		_playbackPresentationManager.Mode is PlaybackPresentationMode.VideoOverlay ||
		_playbackPresentationManager.MusicMode is MusicPresentationMode.Expanded or MusicPresentationMode.Queue or MusicPresentationMode.Lyrics;

	private bool IsChildNavigationPage()
	{
		if (User is null || _currentPageType is null)
		{
			return false;
		}

		return _currentPageType != typeof(HomePage) &&
			   _currentPageType != typeof(MusicLibraryPage) &&
			   _currentPageType != typeof(LibrariesLandingPage) &&
			   _currentPageType != typeof(SettingsPage);
	}

}
