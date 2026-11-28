using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using UChat.App.ViewModels;

namespace UChat.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Mascot.TargetTitle = TitleText;

        var viewModel = new MainViewModel();
        DataContext = viewModel;

        Opened += async (_, _) =>
        {
            await viewModel.InitializeAsync();
        };

        Closed += async (_, _) =>
        {
            await viewModel.DisposeAsync();
        };
    }

    private void OnEditMessageClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm &&
            sender is MenuItem item &&
            item.CommandParameter is Models.ChatMessage message)
        {
            vm.StartEditCommand.Execute(message);
        }
    }

    private async void OnDeleteMessageClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm &&
            sender is MenuItem item &&
            item.CommandParameter is Models.ChatMessage message &&
            vm.DeleteMessageCommand.CanExecute(message))
        {
            await vm.DeleteMessageCommand.ExecuteAsync(message);
        }
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnInputGotFocus(object? sender, RoutedEventArgs e)
    {
        SetInputFrameFocus(sender, true);
    }

    private void OnInputLostFocus(object? sender, RoutedEventArgs e)
    {
        SetInputFrameFocus(sender, false);
    }

    private static void SetInputFrameFocus(object? sender, bool isFocused)
    {
        if (sender is not Control input)
        {
            return;
        }

        for (var ancestor = input.GetVisualParent(); ancestor is not null; ancestor = ancestor.GetVisualParent())
        {
            if (ancestor is Border frame && frame.Classes.Contains("InputFrame"))
            {
                frame.Classes.Set("focused", isFocused);
                return;
            }
        }
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
}
