using System.Diagnostics.CodeAnalysis;
using FluentFin.Contracts.Services;
using FluentFin.Contracts.ViewModels;
using FluentFin.Helpers;
using FluentFin.Playback.Presentation;
using FluentFin.ViewModels;
using FluentFin.UI.Core.Contracts.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace FluentFin.Services;

// For more information on navigation between pages see
// https://github.com/microsoft/TemplateStudio/blob/main/docs/WinUI/navigation.md
public class NavigationService : INavigationService
{
	private readonly IPageService _pageService;
	private readonly IPlaybackPresentationManager _playbackPresentationManager;
	private readonly ILogger<NavigationService> _logger;
	private object? _lastParameterUsed;
	private Frame? _frame;

	public event NavigatedEventHandler? Navigated;

	public Frame? Frame
	{
		get
		{
			if (_frame == null)
			{
				_frame = App.MainWindow.Content as Frame;
				RegisterFrameEvents();
			}

			return _frame;
		}

		set
		{
			UnregisterFrameEvents();
			_frame = value;
			RegisterFrameEvents();
		}
	}

	[MemberNotNullWhen(true, nameof(Frame), nameof(_frame))]
	public bool CanGoBack => Frame != null && Frame.CanGoBack;

	public NavigationService(IPageService pageService, IPlaybackPresentationManager playbackPresentationManager, ILogger<NavigationService> logger)
	{
		_pageService = pageService;
		_playbackPresentationManager = playbackPresentationManager;
		_logger = logger;
	}

	private void RegisterFrameEvents()
	{
		if (_frame != null)
		{
			_frame.Navigated += OnNavigated;
		}
	}

	private void UnregisterFrameEvents()
	{
		if (_frame != null)
		{
			_frame.Navigated -= OnNavigated;
		}
	}

	public bool GoBack()
	{
		if (CanGoBack)
		{
			var vmBeforeNavigation = _frame.GetPageViewModel();
			_frame.GoBack();
			if (vmBeforeNavigation is INavigationAware navigationAware)
			{
				navigationAware.OnNavigatedFrom();
			}

			return true;
		}

		return false;
	}

	public bool NavigateTo(string pageKey, object? parameter = null, bool clearNavigation = false)
	{
		try
		{
			if (pageKey == typeof(VideoPlayerViewModel).FullName)
			{
				_logger.LogInformation("Routing video navigation to playback presentation. PageKey={PageKey}, ParameterType={ParameterType}",
					pageKey,
					parameter?.GetType().FullName ?? "<null>");
				_playbackPresentationManager.ShowVideo(parameter);
				return true;
			}

			_logger.LogInformation("Navigation requested. PageKey={PageKey}, ParameterType={ParameterType}, HasFrame={HasFrame}, CurrentContent={CurrentContent}, ClearNavigation={ClearNavigation}",
				pageKey,
				parameter?.GetType().FullName ?? "<null>",
				_frame is not null,
				_frame?.Content?.GetType().FullName ?? "<null>",
				clearNavigation);

			var pageType = _pageService.GetPageType(pageKey);

			if (_frame != null && (_frame.Content?.GetType() != pageType || (parameter != null && !parameter.Equals(_lastParameterUsed))))
			{
				_frame.Tag = clearNavigation;
				var vmBeforeNavigation = _frame.GetPageViewModel();
				var navigated = _frame.Navigate(pageType, parameter);
				_logger.LogInformation("Frame.Navigate completed. PageKey={PageKey}, PageType={PageType}, Navigated={Navigated}", pageKey, pageType.FullName, navigated);
				if (navigated)
				{
					_lastParameterUsed = parameter;
					if (vmBeforeNavigation is INavigationAware navigationAware)
					{
						navigationAware.OnNavigatedFrom();
					}
				}

				return navigated;
			}

			_logger.LogInformation("Navigation skipped. PageKey={PageKey}, Reason={Reason}",
				pageKey,
				_frame is null ? "FrameIsNull" : "AlreadyOnPageWithSameParameter");
			return false;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Navigation failed. PageKey={PageKey}, ParameterType={ParameterType}", pageKey, parameter?.GetType().FullName ?? "<null>");
			return false;
		}

	}

	private async void OnNavigated(object sender, NavigationEventArgs e)
	{
		if (sender is Frame frame)
		{
			_logger.LogInformation("Frame navigated. SourcePageType={SourcePageType}, ParameterType={ParameterType}", e.SourcePageType.FullName, e.Parameter?.GetType().FullName ?? "<null>");
			var clearNavigation = (bool)frame.Tag;
			if (clearNavigation)
			{
				frame.BackStack.Clear();
			}

			if (frame.GetPageViewModel() is INavigationAware navigationAware)
			{
				try
				{
					_logger.LogInformation("Calling OnNavigatedTo. ViewModel={ViewModel}", navigationAware.GetType().FullName);
					await navigationAware.OnNavigatedTo(e.Parameter!);
					_logger.LogInformation("OnNavigatedTo completed. ViewModel={ViewModel}", navigationAware.GetType().FullName);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "OnNavigatedTo failed. ViewModel={ViewModel}, SourcePageType={SourcePageType}", navigationAware.GetType().FullName, e.SourcePageType.FullName);
				}
			}
			else
			{
				_logger.LogInformation("Navigated page has no INavigationAware view model. SourcePageType={SourcePageType}", e.SourcePageType.FullName);
			}

			Navigated?.Invoke(sender, e);
		}
	}
}
