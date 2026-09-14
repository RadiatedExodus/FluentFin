using FluentFin.Contracts.Services;
using FluentFin.Core;
using FluentFin.Core.Contracts.Services;
using FluentFin.Core.Services;
using FluentFin.Core.Settings;
using FluentFin.Core.ViewModels;
using FluentFin.ViewModels;
using Jellyfin.Sdk.Generated.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace FluentFin.Activation;

public class DefaultActivationHandler : ActivationHandler<LaunchActivatedEventArgs>
{
	private readonly INavigationService _navigationService;
	private readonly INavigationService _mainNavigationService;
	private readonly ISettings _settings;
	private readonly IJellyfinAuthenticationService _jellyfinAuthenticationService;
	private readonly IJellyfinClient _jellyfinClient;
	private readonly ILogger<DefaultActivationHandler> _logger;

	public DefaultActivationHandler([FromKeyedServices(NavigationRegions.InitialSetup)] INavigationService navigationService,
									INavigationService mainNavigationService,
									ISettings settings,
									IJellyfinAuthenticationService jellyfinAuthenticationService,
									IJellyfinClient jellyfinClient,
									ILogger<DefaultActivationHandler> logger)
	{
		_navigationService = navigationService;
		_mainNavigationService = mainNavigationService;
		_settings = settings;
		_jellyfinAuthenticationService = jellyfinAuthenticationService;
		_jellyfinClient = jellyfinClient;
		_logger = logger;
		_settings.ListenToChanges();
	}

	protected override bool CanHandleInternal(LaunchActivatedEventArgs args)
	{
		// None of the ActivationHandlers has handled the activation.
		return _navigationService.Frame?.Content == null;
	}

	protected async override Task HandleInternalAsync(LaunchActivatedEventArgs args)
	{
		_logger.LogInformation("Default activation started. ServerCount={ServerCount}, FirstServerUserCount={FirstServerUserCount}",
			_settings.Servers.Count,
			_settings.Servers.Count > 0 ? _settings.Servers[0].Users.Count : 0);

		if (_settings.Servers.Count == 1 && _settings.Servers[0].Users.Count == 1)
		{
			_logger.LogInformation("Attempting saved single-user authentication. ServerName={ServerName}, Username={Username}",
				_settings.Servers[0].DisplayName,
				_settings.Servers[0].Users[0].Username);
			var result = await _jellyfinAuthenticationService.Authenticate(_settings.Servers[0], _settings.Servers[0].Users[0]);
			_logger.LogInformation("Saved single-user authentication completed. Success={Success}", result);

			if (result)
			{
				var navigated = _navigationService.NavigateTo<ShellViewModel>();
				_logger.LogInformation("Navigated to shell after saved authentication. Navigated={Navigated}", navigated);

				var cmdArgs = Environment.GetCommandLineArgs();
				if(cmdArgs.Length == 2 && Guid.TryParse(cmdArgs[1], out var libraryId))
				{
					var library = await _jellyfinClient.GetItem(libraryId);
					var libraryNavigated = library?.CollectionType is BaseItemDto_CollectionType.Music
						? _mainNavigationService.NavigateTo<MusicAlbumListViewModel>(GlobalCommands.CreateMusicLibraryAlbumListParameter(library))
						: _mainNavigationService.NavigateTo<LibraryViewModel>(libraryId);
					_logger.LogInformation("Command-line library navigation requested. LibraryId={LibraryId}, CollectionType={CollectionType}, Navigated={Navigated}", libraryId, library?.CollectionType, libraryNavigated);
				}
			}
			else
			{
				var navigated = _navigationService.NavigateTo<SelectServerViewModel>();
				_logger.LogInformation("Saved authentication failed; navigated to server selection. Navigated={Navigated}", navigated);
			}
		}
		else
		{
			var navigated = _navigationService.NavigateTo<SelectServerViewModel>();
			_logger.LogInformation("No saved single-user session; navigated to server selection. Navigated={Navigated}", navigated);
		}
	}
}
