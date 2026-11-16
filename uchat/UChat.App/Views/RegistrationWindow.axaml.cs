using Avalonia.Controls;
using Avalonia.Input;

namespace UChat.App.Views;

public partial class RegistrationWindow : Window
{
    public RegistrationWindow()
    {
        InitializeComponent();
    }

    private void OnDragAreaPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnSignUpClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        new LoginWindow().Show();
        Close();
    }
}
