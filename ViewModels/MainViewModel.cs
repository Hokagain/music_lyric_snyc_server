using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using music_lyric_snyc_server.Models;
using music_lyric_snyc_server.Services;

namespace music_lyric_snyc_server.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private const int MaxLogEntries = 400;
    private static readonly TimeSpan LyricAdvance = TimeSpan.FromMilliseconds(200);

    private readonly SmtcService _smtcService;
    private readonly QqMusicApiClient _qqMusicApiClient;
    private readonly LyricParser _lyricParser;
    private readonly DispatcherTimer _progressTimer;
    private readonly Dispatcher _uiDispatcher;

    private CancellationTokenSource? _lyricCts;
    private string _currentTrackKey = string.Empty;
    private int _lastLoggedLyricIndex = -1;
    private string _lastPlaybackInfoSummary = string.Empty;
    private int _lyricLoadVersion = 0;

    public ObservableCollection<LyricLine> LyricLines { get; } = [];
    public ObservableCollection<LogEntry> LogEntries { get; } = [];
    public ICollectionView FilteredLogEntries { get; }
    public ICommand ClearLogsCommand { get; }

    private string _songTitle = "未检测到 QQ 音乐播放";
    public string SongTitle
    {
        get => _songTitle;
        private set => SetField(ref _songTitle, value);
    }

    private string _artist = "";
    public string Artist
    {
        get => _artist;
        private set => SetField(ref _artist, value);
    }

    private string _playbackStatus = "Stopped";
    public string PlaybackStatus
    {
        get => _playbackStatus;
        private set => SetField(ref _playbackStatus, value);
    }

    private string _progressText = "00:00 / 00:00";
    public string ProgressText
    {
        get => _progressText;
        private set => SetField(ref _progressText, value);
    }

    private string _currentLyric = "";
    public string CurrentLyric
    {
        get => _currentLyric;
        private set => SetField(ref _currentLyric, value);
    }

    private int _currentLyricIndex = -1;
    public int CurrentLyricIndex
    {
        get => _currentLyricIndex;
        private set => SetField(ref _currentLyricIndex, value);
    }

    private BitmapImage? _coverImage;
    public BitmapImage? CoverImage
    {
        get => _coverImage;
        private set => SetField(ref _coverImage, value);
    }

    private LogEntry? _selectedLogEntry;
    public LogEntry? SelectedLogEntry
    {
        get => _selectedLogEntry;
        private set => SetField(ref _selectedLogEntry, value);
    }

    private bool _showInfo = true;
    public bool ShowInfo
    {
        get => _showInfo;
        set
        {
            if (!SetField(ref _showInfo, value))
            {
                return;
            }

            FilteredLogEntries.Refresh();
        }
    }

    private bool _showWarn = true;
    public bool ShowWarn
    {
        get => _showWarn;
        set
        {
            if (!SetField(ref _showWarn, value))
            {
                return;
            }

            FilteredLogEntries.Refresh();
        }
    }

    private bool _showError = true;
    public bool ShowError
    {
        get => _showError;
        set
        {
            if (!SetField(ref _showError, value))
            {
                return;
            }

            FilteredLogEntries.Refresh();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel(SmtcService smtcService, QqMusicApiClient qqMusicApiClient, LyricParser lyricParser)
    {
        _smtcService = smtcService;
        _qqMusicApiClient = qqMusicApiClient;
        _lyricParser = lyricParser;
        _uiDispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        FilteredLogEntries = CollectionViewSource.GetDefaultView(LogEntries);
        FilteredLogEntries.Filter = FilterLogEntry;
        ClearLogsCommand = new RelayCommand(ClearLogs);

        _progressTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _progressTimer.Tick += OnProgressTimerTick;
    }

    public async Task StartAsync()
    {
        await AddLogAsync("INFO", "开始初始化 SMTC 监听。");
        _smtcService.PlaybackChanged += OnPlaybackChanged;
        await _smtcService.InitializeAsync();
        await AddLogAsync("INFO", "SMTC 初始化完成，已开始监听 QQ 音乐会话。");
        _progressTimer.Start();
        await AddLogAsync("INFO", "进度定时器已启动，间隔 500ms。");
    }

    private async void OnProgressTimerTick(object? sender, EventArgs e)
    {
        try
        {
            var snapshot = await _smtcService.GetCurrentSnapshotAsync();
            if (snapshot is null)
            {
                return;
            }

            UpdateProgress(snapshot.Position, snapshot.Duration);
            UpdateCurrentLyric(snapshot.Position);
        }
        catch (Exception ex)
        {
            await AddLogAsync("ERROR", $"读取播放进度失败: {ex.Message}");
        }
    }

    private async void OnPlaybackChanged(object? sender, PlaybackSnapshot snapshot)
    {
        if (!_uiDispatcher.CheckAccess())
        {
            _ = _uiDispatcher.BeginInvoke(new Action(() => OnPlaybackChanged(sender, snapshot)));
            return;
        }

        SongTitle = string.IsNullOrWhiteSpace(snapshot.Title) ? "未检测到 QQ 音乐播放" : snapshot.Title;
        Artist = snapshot.Artist;
        PlaybackStatus = snapshot.IsPlaying ? "Playing" : "Paused";
        var playbackSummary = $"标题='{SongTitle}', 歌手='{Artist}', 状态={PlaybackStatus}";
        if (!string.Equals(playbackSummary, _lastPlaybackInfoSummary, StringComparison.Ordinal))
        {
            _lastPlaybackInfoSummary = playbackSummary;
            await AddLogAsync("INFO", $"播放状态更新: {playbackSummary}");
        }

        UpdateProgress(snapshot.Position, snapshot.Duration);
        UpdateCurrentLyric(snapshot.Position);

        if (snapshot.ThumbnailBytes is { Length: > 0 })
        {
            CoverImage = ToBitmapImage(snapshot.ThumbnailBytes);
        }
        else if (!string.IsNullOrWhiteSpace(snapshot.Title))
        {
            CoverImage = null;
        }


        if (!string.IsNullOrWhiteSpace(snapshot.Title) && snapshot.TrackKey != _currentTrackKey)
        {
            _currentTrackKey = snapshot.TrackKey;
            _lastLoggedLyricIndex = -1;
            _lastPlaybackInfoSummary = string.Empty;
            _lyricLoadVersion++;
            LyricLines.Clear();
            CurrentLyric = "歌词加载中...";
            CurrentLyricIndex = -1;
            await AddLogAsync("INFO", $"检测到歌曲切换，开始拉取歌词: {snapshot.Title} - {snapshot.Artist}");
            await LoadLyricsForTrackAsync(snapshot.Title, snapshot.Artist, _lyricLoadVersion);
        }

        if (string.IsNullOrWhiteSpace(snapshot.Title))
        {
            _currentTrackKey = string.Empty;
            LyricLines.Clear();
            CurrentLyric = string.Empty;
            CurrentLyricIndex = -1;
            CoverImage = null;
            _lyricLoadVersion++;
            await AddLogAsync("WARN", "当前未检测到 QQ 音乐播放。");
        }
    }

    private async Task LoadLyricsForTrackAsync(string title, string artist, int version)
    {
        _lyricCts?.Cancel();
        _lyricCts?.Dispose();
        _lyricCts = new CancellationTokenSource();
        await AddLogAsync("DEBUG", $"歌词请求上下文已重置，关键词: 标题={title}, 歌手={artist}");

        try
        {
            var ct = _lyricCts.Token;
            await AddLogAsync("INFO", $"调用搜索接口匹配歌曲+歌手获取 ID: 标题={title}, 歌手={artist}");
            var songId = await _qqMusicApiClient.SearchSongIdBySongAndSingerAsync(title, artist, ct);
            if (!songId.HasValue)
            {
                if (version != _lyricLoadVersion)
                {
                    return;
                }

                LyricLines.Clear();
                CurrentLyric = "未找到歌词";
                CurrentLyricIndex = -1;
                await AddLogAsync("WARN", $"未匹配到歌曲 ID: 标题={title}, 歌手={artist}");
                return;
            }

            await AddLogAsync("INFO", $"匹配成功，歌曲 ID={songId.Value}，开始请求歌词。");
            var lrc = await _qqMusicApiClient.GetLyricBySongIdAsync(songId.Value, ct);
            await AddLogAsync("DEBUG", $"歌词接口返回长度={(lrc?.Length ?? 0)} 字符。");
            var parsed = _lyricParser.Parse(lrc);

            if (version != _lyricLoadVersion)
            {
                await AddLogAsync("DEBUG", $"歌词回包已过期，丢弃: {title}");
                return;
            }

            LyricLines.Clear();
            foreach (var line in parsed)
            {
                LyricLines.Add(line);
            }

            CurrentLyric = LyricLines.Count > 0 ? LyricLines[0].Text : "未找到歌词";
            CurrentLyricIndex = LyricLines.Count > 0 ? 0 : -1;
            await AddLogAsync("INFO", $"歌词解析完成，行数={LyricLines.Count}");
        }
        catch (OperationCanceledException)
        {
            await AddLogAsync("DEBUG", "歌词请求已取消（通常是快速切歌触发）。");
        }
        catch (Exception ex)
        {
            LyricLines.Clear();
            CurrentLyric = "歌词加载失败";
            CurrentLyricIndex = -1;
            await AddLogAsync("ERROR", $"歌词加载失败: {ex.Message}");
        }
    }

    private void UpdateCurrentLyric(TimeSpan position)
    {
        if (LyricLines.Count == 0)
        {
            return;
        }

        var adjustedPosition = position + LyricAdvance;
        var index = _lyricParser.GetCurrentLineIndex(LyricLines, adjustedPosition);
        CurrentLyricIndex = index;
        CurrentLyric = index >= 0 ? LyricLines[index].Text : string.Empty;
        if (index >= 0 && index != _lastLoggedLyricIndex)
        {
            _lastLoggedLyricIndex = index;
            _ = AddLogAsync("DEBUG", $"歌词行切换: #{index + 1} [{LyricLines[index].Time:mm\\:ss}] {CurrentLyric}");
        }
    }

    private void UpdateProgress(TimeSpan position, TimeSpan duration)
    {
        ProgressText = $"{ToTime(position)} / {ToTime(duration)}";
    }

    private static string ToTime(TimeSpan time)
    {
        return $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";
    }

    private static BitmapImage ToBitmapImage(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        _progressTimer.Stop();
        _progressTimer.Tick -= OnProgressTimerTick;
        await AddLogAsync("INFO", "进度定时器已停止。");

        _smtcService.PlaybackChanged -= OnPlaybackChanged;
        await _smtcService.DisposeAsync();
        await AddLogAsync("INFO", "SMTC 监听已释放。");

        _lyricCts?.Cancel();
        _lyricCts?.Dispose();
        await AddLogAsync("INFO", "歌词同步模块已释放。");
    }

    private Task AddLogAsync(string level, string message)
    {
        if (_uiDispatcher.CheckAccess())
        {
            AddLogCore(level, message);
            return Task.CompletedTask;
        }

        return _uiDispatcher.InvokeAsync(() => AddLogCore(level, message)).Task;
    }

    private void AddLogCore(string level, string message)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Message = message
        };

        LogEntries.Add(entry);
        SelectedLogEntry = entry;

        if (LogEntries.Count <= MaxLogEntries)
        {
            return;
        }

        var overflow = LogEntries.Count - MaxLogEntries;
        for (var i = 0; i < overflow; i++)
        {
            LogEntries.RemoveAt(0);
        }
    }

    private bool FilterLogEntry(object item)
    {
        if (item is not LogEntry entry)
        {
            return false;
        }

        return entry.Level switch
        {
            "ERROR" => ShowError,
            "WARN" => ShowWarn,
            _ => ShowInfo
        };
    }

    private void ClearLogs()
    {
        LogEntries.Clear();
        SelectedLogEntry = null;
    }
}
