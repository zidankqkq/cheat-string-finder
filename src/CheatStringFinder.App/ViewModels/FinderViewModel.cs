using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using HistoryParser.App.ViewModels;
using Microsoft.Win32;
using RatChecker.Core;

namespace CheatStringFinder.App.ViewModels;

/// <summary>One string in the results table. Ticked strings go in the saved list (proven ones start ticked).</summary>
public sealed class StringRow(DiffString s, Action changed) : ObservableObject
{
    public DiffString S { get; } = s;
    public string Text => S.Text;
    public string Type => S.Kind switch
    {
        DiffKind.ClassName => "Class name", DiffKind.WebAddress => "Web address", DiffKind.Identifier => "Name / key", _ => "Text",
    };
    public string Status => S.Verified ? "Proven" : "Not proven";
    public string Note => S.Note;
    public string Group => S.Group.Length == 0 ? "" : $"{S.Group.Replace('/', '.')} ({S.GroupSize})";
    public string FromFile => S.InCheatFile ? "Yes" : "";
    /// <summary>Tone for the dot: proven green, the rest faint.</summary>
    public object Tone => S.Verified ? true : "Low";

    bool _include = s.Verified;
    public bool Include { get => _include; set { if (Set(ref _include, value)) changed(); } }
}

public sealed class FinderViewModel : ObservableObject
{
    CancellationTokenSource? _cts;
    GameDiffResult? _result;

    public FinderViewModel()
    {
        RowsView = new System.Windows.Data.ListCollectionView(new List<StringRow>());
        RefreshGames();
        ChooseFileCommand = new RelayCommand(ChooseFile, () => !IsBusy);
        ClearFileCommand = new RelayCommand(() => CheatFile = null, () => !IsBusy && CheatFile != null);
        RefreshCommand = new RelayCommand(RefreshGames, () => !IsBusy);
        SwapCommand = new RelayCommand(() => (CleanGame, InjectedGame) = (InjectedGame, CleanGame), () => !IsBusy && (CleanGame != null || InjectedGame != null));
        FindCommand = new RelayCommand(async () => await FindAsync(), () => !IsBusy && CleanGame != null && InjectedGame != null && CleanGame.Pid != InjectedGame.Pid);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy);
        SaveCommand = new RelayCommand(Save, () => !IsBusy && Ticked > 0);
        CopyCommand = new RelayCommand(() => Copy(Rows.Where(r => r.Include).Select(r => r.Text)), () => Ticked > 0);
        CopySelectedCommand = new RelayCommand(p => Copy(Selected(p).Select(r => r.Text)), p => Selected(p).Any());
        TickSelectedCommand = new RelayCommand(p => { foreach (var r in Selected(p)) r.Include = true; }, p => Selected(p).Any());
        UntickSelectedCommand = new RelayCommand(p => { foreach (var r in Selected(p)) r.Include = false; }, p => Selected(p).Any());
        OpenFolderCommand = new RelayCommand(() =>
        {
            if (_lastSaved != null) System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{_lastSaved}\"");
        }, () => _lastSaved != null);
        CrossCheckCommand = new RelayCommand(CrossCheck, () => !IsBusy && Rows.Count > 0);
    }

    public RelayCommand CrossCheckCommand { get; }

    /// <summary>
    /// Keeps ticked only the strings an earlier list (from another injection of the same client) also has. Random names
    /// that change every injection drop out; what the client always brings stays.
    /// </summary>
    void CrossCheck()
    {
        var dlg = new OpenFileDialog { Title = "Choose a list made from another injection of the same client", Filter = "String lists|*.txt|All files|*.*" };
        if (dlg.ShowDialog() != true) return;
        HashSet<string> earlier;
        try
        {
            var (core, supporting) = GameDiff.ReadList(dlg.FileName);
            earlier = core.Concat(supporting).ToHashSet(StringComparer.Ordinal);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Notes = $"Could not read {Path.GetFileName(dlg.FileName)}: {e.Message}";
            return;
        }
        int kept = 0;
        foreach (var r in Rows)
        {
            bool both = earlier.Contains(r.Text);
            r.Include = both;
            if (both) kept++;
        }
        Notes = $"Cross-checked with {Path.GetFileName(dlg.FileName)}: {kept:N0} string(s) are in both lists and stay ticked; the rest were unticked " +
                "(they changed between injections, or the earlier list left them out).";
        Show = "Ticked";
    }

    public RelayCommand ChooseFileCommand { get; }
    public RelayCommand ClearFileCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand SwapCommand { get; }
    public RelayCommand FindCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand CopyCommand { get; }
    /// <summary>Right-click on rows: the selected rows come in as the command parameter.</summary>
    public RelayCommand CopySelectedCommand { get; }
    public RelayCommand TickSelectedCommand { get; }
    public RelayCommand UntickSelectedCommand { get; }
    public RelayCommand OpenFolderCommand { get; }

    static IEnumerable<StringRow> Selected(object? p) => p is System.Collections.IList list ? list.OfType<StringRow>().ToList() : [];

    static void Copy(IEnumerable<string> lines)
    {
        try { Clipboard.SetText(string.Join(Environment.NewLine, lines)); }
        catch (System.Runtime.InteropServices.ExternalException) { }
    }

    string? _lastSaved;

    // ---- the table: search and filters --------------------------------------------------------------
    public System.Windows.Data.ListCollectionView RowsView { get; private set; }

    string _search = "";
    public string Search { get => _search; set { if (Set(ref _search, value)) RefreshView(); } }

    string _show = "All";
    /// <summary>"All", "Ticked" or "NotTicked".</summary>
    public string Show { get => _show; set { if (Set(ref _show, value)) RefreshView(); } }

    bool Filter(object o) => o is StringRow r &&
        (Show == "All" || Show == "Ticked" == r.Include) &&
        (Search.Length == 0 || r.Text.Contains(Search, StringComparison.OrdinalIgnoreCase) || r.Group.Contains(Search, StringComparison.OrdinalIgnoreCase));

    void RefreshView()
    {
        RowsView.Refresh();
        OnPropertyChanged(nameof(VisibleRows));
    }

    public int VisibleRows => RowsView.Count;
    public int Ticked => Rows.Count(r => r.Include);
    public string TickedText => $"{Ticked:N0} ticked for the list";

    void TicksChanged()
    {
        OnPropertyChanged(nameof(Ticked));
        OnPropertyChanged(nameof(TickedText));
        CommandManager.InvalidateRequerySuggested();
        if (Show != "All") RefreshView();
    }

    string _page = "Find";
    public string Page { get => _page; set => Set(ref _page, value); }

    string? _cheatFile;
    /// <summary>Optional: the client's file. Strings also in it rank higher; it never adds strings the games do not show.</summary>
    public string? CheatFile
    {
        get => _cheatFile;
        set { if (Set(ref _cheatFile, value)) OnPropertyChanged(nameof(CheatFileName)); }
    }
    public string CheatFileName => CheatFile == null ? "No file (optional)" : Path.GetFileName(CheatFile);

    public ObservableCollection<GameProcess> Games { get; } = new();

    GameProcess? _clean;
    public GameProcess? CleanGame { get => _clean; set { if (Set(ref _clean, value)) CommandManager.InvalidateRequerySuggested(); } }

    GameProcess? _injected;
    public GameProcess? InjectedGame { get => _injected; set { if (Set(ref _injected, value)) CommandManager.InvalidateRequerySuggested(); } }

    string _clientName = "";
    /// <summary>The name written at the top of the saved list (for your own reference).</summary>
    public string ClientName { get => _clientName; set => Set(ref _clientName, value); }

    bool _busy;
    public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) CommandManager.InvalidateRequerySuggested(); } }

    string _progress = "";
    public string Progress { get => _progress; private set => Set(ref _progress, value); }

    public ObservableCollection<StringRow> Rows { get; } = new();
    public bool HasResults => _result != null;

    string _headline = "";
    public string Headline { get => _headline; private set => Set(ref _headline, value); }

    string _stats = "";
    public string Stats { get => _stats; private set => Set(ref _stats, value); }

    string _warnings = "";
    /// <summary>Ways the two games differ that add noise (different version or mods).</summary>
    public string Warnings { get => _warnings; private set => Set(ref _warnings, value); }

    string _differences = "";
    /// <summary>Other evidence only the injected game has: DLLs, agents, hidden code, file paths.</summary>
    public string Differences { get => _differences; private set => Set(ref _differences, value); }

    string _notes = "";
    public string Notes { get => _notes; private set => Set(ref _notes, value); }

    public void RefreshGames()
    {
        int? keepClean = CleanGame?.Pid, keepInjected = InjectedGame?.Pid;
        Games.Clear();
        foreach (var g in CheatStrings.JavaProcesses().OrderBy(g => g.Started ?? DateTime.MaxValue)) Games.Add(Labelled(g));
        CleanGame = Games.FirstOrDefault(g => g.Pid == keepClean);
        InjectedGame = Games.FirstOrDefault(g => g.Pid == keepInjected);
        Notes = Games.Count < 2 ? "Start two copies of the same game (same version, same mods), leave both on the title screen, inject the client into one, then press Refresh." : "";
    }

    /// <summary>The game with what tells two copies apart: when it started, which profile, how much memory it uses.</summary>
    static GameProcess Labelled(GameProcess g)
    {
        var parts = new List<string>();
        if (g.Title.Length > 0) parts.Add(g.Title);
        if (g.Started is { } s) parts.Add($"started {s:HH:mm:ss}");
        if (HistoryParser.Core.Native.WinApi.GetCommandLine(g.Pid) is { } cmd &&
            System.Text.RegularExpressions.Regex.Match(cmd, @"--gameDir\s+(""[^""]+""|\S+)") is { Success: true } m)
            parts.Add("profile " + Path.GetFileName(m.Groups[1].Value.Trim('"').TrimEnd('\\')));
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(g.Pid);
            parts.Add($"{p.WorkingSet64 >> 20:N0} MB");
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { }
        return g with { Title = string.Join(" · ", parts) };
    }

    void ChooseFile()
    {
        var dlg = new OpenFileDialog { Title = "Choose the cheat client (optional; it is only read, never run)", Filter = "Cheat clients|*.jar;*.dll;*.exe|All files|*.*" };
        if (dlg.ShowDialog() == true) CheatFile = dlg.FileName;
    }

    public async Task FindAsync()
    {
        if (IsBusy || CleanGame is not { } clean || InjectedGame is not { } injected) return;
        IsBusy = true;
        _cts = new CancellationTokenSource();
        Rows.Clear();
        RowsView = new System.Windows.Data.ListCollectionView(new List<StringRow>());
        OnPropertyChanged(nameof(RowsView));
        _result = null;
        Notes = Warnings = Differences = Stats = "";
        OnPropertyChanged(nameof(HasResults));
        try
        {
            string? file = CheatFile;
            var ct = _cts.Token;
            _result = await Task.Run(() => GameDiff.Compare(clean, injected, file, s => Progress = s, ct), ct);
            foreach (var s in _result.Strings) Rows.Add(new StringRow(s, TicksChanged));
            RowsView = new System.Windows.Data.ListCollectionView(Rows.ToList()) { Filter = Filter };
            OnPropertyChanged(nameof(RowsView));
            RefreshView();
            TicksChanged();
            int proven = _result.List.Count();
            Headline = (proven == 0 ? "No strings only the injected game holds"
                : $"{proven} proven string(s) only the injected game holds - all ticked for the list") +
                (_result.Warnings.Count > 0 ? " - but the two games are not set up the same, so the list can hold their mods. Make it again from one profile." : "");
            Stats = $"Read {_result.CleanBytes >> 20:N0} MB of the clean game ({_result.CleanStrings:N0} different strings) and {_result.InjectedBytes >> 20:N0} MB of the injected game, " +
                    $"each twice. Only in the injected game: {_result.FirstDifference:N0} on the first read, {_result.Stable:N0} still there on the second; " +
                    $"{_result.Strings.Count:N0} kept as readable names and text.";
            Warnings = string.Join("\n", _result.Warnings);
            Differences = string.Join("\n", _result.Differences);
            Notes = string.Join("\n", _result.Notes);
        }
        catch (OperationCanceledException) { Notes = "Stopped."; }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or InvalidDataException or ArgumentException)
        {
            Notes = e.Message;
        }
        finally
        {
            Progress = "";
            IsBusy = false;
            OnPropertyChanged(nameof(HasResults));
        }
    }

    void Save()
    {
        if (_result == null) return;
        var name = string.Concat((string.IsNullOrWhiteSpace(ClientName) ? "cheat" : ClientName).Split(Path.GetInvalidFileNameChars())).Trim();
        var dlg = new SaveFileDialog { Filter = "Text file|*.txt", FileName = $"{name} - strings.txt" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, GameDiff.ToText(_result, ClientName, Rows.Where(r => r.Include).Select(r => r.S)));
            _lastSaved = dlg.FileName;
            Notes = $"Saved {Ticked:N0} strings to {dlg.FileName}. Load it in Client Parser (Add string lists).";
            CommandManager.InvalidateRequerySuggested();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Could not save: {e.Message}", "Cheat String Finder", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
