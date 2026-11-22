using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using System;
using Avalonia.Controls;
using Avalonia.Input;
using UChat.App.Services;

namespace UChat.App.Views;

public partial class RegistrationWindow : Window
{
    private readonly AuthApiClient _authApi = new();

    public RegistrationWindow()
    {
        InitializeComponent();
    }

    private void OnLoginClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var loginWindow = new LoginWindow();

        if (Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = loginWindow;
        }

        loginWindow.Show();
        Close();
    }

    private void OnDragAreaPointerPressed(
        object? sender,
        PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private async void OnSignUpClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        ErrorText.IsVisible = false;

        var username = UsernameBox.Text?.Trim() ?? "";
        var displayName = DisplayNameBox.Text?.Trim() ?? "";
        var password = PasswordBox.Text ?? "";

        if (string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(displayName) ||
            string.IsNullOrWhiteSpace(password))
        {
            ShowError("Fill in all fields.");
            return;
        }

        SignUpButton.IsEnabled = false;

        try
        {
            await _authApi.RegisterAsync(username, displayName, password);

            new LoginWindow().Show();
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SignUpButton.IsEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }
}
