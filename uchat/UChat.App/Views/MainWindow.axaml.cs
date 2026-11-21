using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using System;
using Avalonia.Controls;
using UChat.App.ViewModels;

namespace UChat.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();

        DataContext = _viewModel;

        Opened += async (_, _) =>
        {
            await _viewModel.InitializeAsync();
        };

        Closed += async (_, _) =>
        {
            await _viewModel.DisposeAsync();
        };
    }
}
