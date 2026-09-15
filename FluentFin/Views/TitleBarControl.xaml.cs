using FluentFin.Core.ViewModels;
using FluentFin.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;

namespace FluentFin.Views;

public sealed partial class TitleBarControl : UserControl
{
	public TitleBarViewModel ViewModel { get; } = (TitleBarViewModel)App.GetService<ITitleBarViewModel>();

	public TitleBarControl()
	{
		InitializeComponent();
		Loaded += OnLoaded;
		ViewModel.PropertyChanged += ViewModel_PropertyChanged;
	}

	private void TitleBar_BackRequested(Microsoft.UI.Xaml.Controls.TitleBar sender, object args)
	{
		ViewModel.GoBack();
	}

	private void BackButton_Click(object sender, RoutedEventArgs e)
	{
		ViewModel.GoBack();
	}

	private void CloseTitleBarFooterFlyout(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
	{
		FooterFlyout.Hide();
	}

	private void KeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
	{
		if (FocusManager.GetFocusedElement() is AutoSuggestBox sb && sb == SearchBox)
		{
			return;
		}

		SearchBox.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
	}

	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		ApplyBackButtonState(ViewModel.IsBackButtonVisible, false);
		ApplyChromeVisibilityState(ViewModel.IsVisible, false);
	}

	private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (!DispatcherQueue.HasThreadAccess)
		{
			DispatcherQueue.TryEnqueue(() => ViewModel_PropertyChanged(sender, e));
			return;
		}

		if (e.PropertyName is nameof(TitleBarViewModel.IsBackButtonVisible))
		{
			ApplyBackButtonState(ViewModel.IsBackButtonVisible, true);
		}
		else if (e.PropertyName is nameof(TitleBarViewModel.IsVisible))
		{
			ApplyChromeVisibilityState(ViewModel.IsVisible, true);
		}
	}

	private void ApplyBackButtonState(bool isVisible, bool animate)
	{
		BackButtonSlot.IsHitTestVisible = isVisible;
		if (!animate)
		{
			BackButtonSlot.Width = isVisible ? 44 : 0;
			BackButtonSlot.Opacity = isVisible ? 1 : 0;
			return;
		}

		RunDoubleTransition(BackButtonSlot, "Width", isVisible ? 44 : 0, TimeSpan.FromMilliseconds(180));
		RunDoubleTransition(BackButtonSlot, "Opacity", isVisible ? 1 : 0, TimeSpan.FromMilliseconds(140));
	}

	private void ApplyChromeVisibilityState(bool isVisible, bool animate)
	{
		RootChrome.IsHitTestVisible = isVisible;
		if (isVisible)
		{
			RootChrome.Visibility = Visibility.Visible;
		}

		if (!animate)
		{
			RootChrome.Opacity = isVisible ? 1 : 0;
			ChromeTransform.Y = isVisible ? 0 : -56;
			if (!isVisible)
			{
				RootChrome.Visibility = Visibility.Collapsed;
			}

			return;
		}

		var storyboard = new Storyboard();
		storyboard.Children.Add(CreateDoubleAnimation(RootChrome, "Opacity", isVisible ? 1 : 0, TimeSpan.FromMilliseconds(180)));
		storyboard.Children.Add(CreateDoubleAnimation(ChromeTransform, "Y", isVisible ? 0 : -56, TimeSpan.FromMilliseconds(180)));
		if (!isVisible)
		{
			storyboard.Completed += (_, _) =>
			{
				if (!ViewModel.IsVisible)
				{
					RootChrome.Visibility = Visibility.Collapsed;
				}
			};
		}

		storyboard.Begin();
	}

	private static void RunDoubleTransition(DependencyObject target, string property, double to, TimeSpan duration)
	{
		var storyboard = new Storyboard();
		storyboard.Children.Add(CreateDoubleAnimation(target, property, to, duration));
		storyboard.Begin();
	}

	private static DoubleAnimation CreateDoubleAnimation(DependencyObject target, string property, double to, TimeSpan duration)
	{
		var animation = new DoubleAnimation
		{
			To = to,
			Duration = new Duration(duration),
			EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
			EnableDependentAnimation = true
		};
		Storyboard.SetTarget(animation, target);
		Storyboard.SetTargetProperty(animation, property);
		return animation;
	}
}
