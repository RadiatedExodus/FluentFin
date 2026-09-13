using FluentFin.Activation;
using FluentFin.Contracts.Services;
using FluentFin.Views;

using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentFin.Services;

public class ActivationService : IActivationService
{
	private readonly ActivationHandler<LaunchActivatedEventArgs> _defaultHandler;
	private readonly IEnumerable<IActivationHandler> _activationHandlers;
	private readonly ILogger<ActivationService> _logger;
	private readonly Guid _placementGuid = Guid.Parse("245b5fc3-a858-4106-8dc9-27de8e60e279");
	private UIElement? _shell = null;

	public ActivationService(ActivationHandler<LaunchActivatedEventArgs> defaultHandler, IEnumerable<IActivationHandler> activationHandlers, ILogger<ActivationService> logger)
	{
		_defaultHandler = defaultHandler;
		_activationHandlers = activationHandlers;
		_logger = logger;
	}

	public async Task ActivateAsync(object activationArgs)
	{
		_logger.LogInformation("Activation started. ArgsType={ArgsType}, MainWindowHasContent={HasContent}", activationArgs.GetType().Name, App.MainWindow.Content is not null);

		// Execute tasks before activation.
		await InitializeAsync();

		// Set the MainWindow Content.
		if (App.MainWindow.Content == null)
		{
			_logger.LogInformation("Creating shell page and assigning it to MainWindow.Content");
			_shell = App.GetService<ShellPage>();
			App.MainWindow.Content = _shell ?? new Frame();
		}
		else
		{
			_logger.LogInformation("MainWindow already has content. ExistingContentType={ContentType}", App.MainWindow.Content.GetType().FullName);
		}

		// Activate the MainWindow.
		App.MainWindow.Maximize();
		//App.MainWindow.AppWindow.EnablePlacementPersistence(_placementGuid, true, App.MainWindow.AppWindow.Id, Microsoft.UI.Windowing.PlacementPersistenceBehaviorFlags.OpenOverLastOpenedWindow | Microsoft.UI.Windowing.PlacementPersistenceBehaviorFlags.AllowLaunchIntoMaximized);
		App.MainWindow.Activate();
		_logger.LogInformation("MainWindow activated");

		_ = DeviceProfileFactory.Initialize();
		_logger.LogInformation("Device profile initialization started in background");

		// Handle activation via ActivationHandlers.
		await HandleActivationAsync(activationArgs);

		// Execute tasks after activation.
		await StartupAsync();
		_logger.LogInformation("Activation completed");
	}

	private async Task HandleActivationAsync(object activationArgs)
	{
		var activationHandler = _activationHandlers.FirstOrDefault(h => h.CanHandle(activationArgs));

		if (activationHandler != null)
		{
			_logger.LogInformation("Handling activation with {ActivationHandler}", activationHandler.GetType().FullName);
			await activationHandler.HandleAsync(activationArgs);
			_logger.LogInformation("Activation handler {ActivationHandler} completed", activationHandler.GetType().FullName);
		}

		if (_defaultHandler.CanHandle(activationArgs))
		{
			_logger.LogInformation("Handling activation with default handler {ActivationHandler}", _defaultHandler.GetType().FullName);
			await _defaultHandler.HandleAsync(activationArgs);
			_logger.LogInformation("Default activation handler completed");
		}
		else
		{
			_logger.LogInformation("Default activation handler skipped");
		}
	}

	private async Task InitializeAsync()
	{
		await Task.CompletedTask;
	}

	private async Task StartupAsync()
	{
		await Task.CompletedTask;
	}
}
