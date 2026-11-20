using Avalonia.Controls;
using Avalonia.Input;

namespace UChat.App.Views;

public partial class LoginWindow : Window
{
    public LoginWindow()
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

    private void OnLoginClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        new MainWindow().Show();
        Close();
    }
}
