using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using UChat.App.ViewModels;

namespace UChat.App.Views;

public partial class MainWindow : Window
{
    private readonly TranslateTransform _leftPupilOffset = new();
    private readonly TranslateTransform _rightPupilOffset = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        LeftPupil.RenderTransform = _leftPupilOffset;
        RightPupil.RenderTransform = _rightPupilOffset;
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

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var cursor = e.GetPosition(MascotContainer);
        _leftPupilOffset.X = Math.Clamp((cursor.X - 33.1) / 75 * 2.8, -2.8, 2.8);
        _leftPupilOffset.Y = Math.Clamp((cursor.Y - 41.3) / 55 * 2.2, -2.2, 2.2);
        _rightPupilOffset.X = Math.Clamp((cursor.X - 60.7) / 75 * 2.8, -2.8, 2.8);
        _rightPupilOffset.Y = Math.Clamp((cursor.Y - 41.2) / 55 * 2.2, -2.2, 2.2);
    }

}
