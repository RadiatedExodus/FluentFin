
using CommunityToolkit.Mvvm.ComponentModel;
using FluentFin.Contracts.Services;
using FluentFin.Contracts.ViewModels;
using FluentFin.Core;
using FluentFin.Core.Services;
using FluentFin.Core.ViewModels;
using FluentFin.UI.Core.Contracts.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Navigation;

namespace FluentFin.ViewModels;

public partial class ShellViewModel : ObservableObject, INavigationAware
{
	[ObservableProperty] public partial bool IsBackEnabled { get; set; }
	[ObservableProperty] public partial object? Selected { get; set; }

	public INavigationService NavigationService { get; }
	public INavigationViewService NavigationViewService { get; }
	public bool IsReportingVisible { get; } = SessionInfo.HasPlaybackReporting();
	private readonly ILogger<ShellViewModel> _logger;

	public ShellViewModel(INavigationService navigationService, INavigationViewService navigationViewService, ILogger<ShellViewModel> logger)
	{
		NavigationService = navigationService;
		NavigationService.Navigated += OnNavigated;
		NavigationViewService = navigationViewService;
		_logger = logger;
	}

	private void OnNavigated(object sender, NavigationEventArgs e)
	{
		IsBackEnabled = NavigationService.CanGoBack;
		var selectedItem = NavigationViewService.GetSelectedItem(e.SourcePageType);
		Selected = selectedItem;
		_logger.LogInformation("Shell navigation state updated. SourcePageType={SourcePageType}, CanGoBack={CanGoBack}, HasSelectedItem={HasSelectedItem}",
			e.SourcePageType.FullName,
			IsBackEnabled,
			selectedItem is not null);
	}

	public Task OnNavigatedTo(object parameter)
	{
		_logger.LogInformation("ShellViewModel navigated to. Forwarding to HomeViewModel. ParameterType={ParameterType}", parameter?.GetType().FullName ?? "<null>");
		var navigated = NavigationService.NavigateTo<HomeViewModel>(parameter);
		_logger.LogInformation("ShellViewModel Home navigation requested. Navigated={Navigated}", navigated);
		return Task.CompletedTask;
	}

	public Task OnNavigatedFrom() => Task.CompletedTask;
}
