using System;
using System.Windows;
using music_lyric_snyc_server.Services;
using music_lyric_snyc_server.ViewModels;

namespace music_lyric_snyc_server;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(
            new SmtcService(),
            new QqMusicApiClient(),
            new LyricParser(),
            new LyricSocketServerService());
        DataContext = _viewModel;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.StartAsync();
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        await _viewModel.DisposeAsync();
    }
}
