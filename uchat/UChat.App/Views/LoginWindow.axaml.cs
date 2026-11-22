
using System;
using Avalonia.Controls;
using Avalonia.Input;
using UChat.App.Services;

namespace UChat.App.Views;

public partial class LoginWindow : Window
{
    private readonly AuthApiClient _authApi = new();

    public LoginWindow()
    {
        InitializeComponent();
    }

    private void OnRegisterClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var registrationWindow = new RegistrationWindow();
        registrationWindow.Show();
        Close();
    }

    private void OnDragAreaPointerPressed(
        object? sender,
        PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private async void OnLoginClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        ErrorText.IsVisible = false;

        var username = UsernameBox.Text?.Trim() ?? "";
        var password = PasswordBox.Text ?? "";

        if (string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password))
        {
            ShowError("Enter username and password.");
            return;
        }

        LoginButton.IsEnabled = false;

        try
        {
            var token = await _authApi.LoginAsync(username, password);

            AuthSession.SetToken(token);

            new MainWindow().Show();
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }
}
