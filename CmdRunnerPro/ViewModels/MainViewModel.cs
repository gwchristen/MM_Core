using CmdRunnerPro.Services;
using CmdRunnerPro.Views;
using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;              // Dispatcher
using System.Windows.Threading;    // DispatcherTimer
using WpfApp = System.Windows.Application;
using System.Runtime.CompilerServices;


namespace CmdRunnerPro.ViewModels
{
    public sealed class MainViewModel : INotifyPropertyChanged
    {
        // ---- Bindable output (listbox) ----
        public ObservableCollection<string> OutputLines { get; } = new ObservableCollection<string>();

        // Queue of (Command, Display). Your CommandRunner expects this shape.
        public ObservableCollection<(string Command, string Display)> QueueItems { get; } =
            new ObservableCollection<(string Command, string Display)>();

        private string _workingDirectory = Environment.CurrentDirectory;
        public string WorkingDirectory
        {
            get => _workingDirectory;
            set { if (_workingDirectory != value) { _workingDirectory = value; OnPropertyChanged(nameof(WorkingDirectory)); } }
        }

        private bool _stopOnError = true;
        public bool StopOnError
        {
            get => _stopOnError;
            set { if (_stopOnError != value) { _stopOnError = value; OnPropertyChanged(nameof(StopOnError)); } }
        }

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            private set { if (_isRunning != value) { _isRunning = value; OnPropertyChanged(nameof(IsRunning)); } }
        }

        // For your "Open Log" handler
        public string LogFile { get; private set; } = "";

        // If your UI binds to this:
        public object SelectedPreset { get; set; }

        // ---- Services & state ----
        private readonly CommandRunner _runner = new CommandRunner();
        private CancellationTokenSource? _cts;

        // Output batching (avoid per-line UI thrash)
        private readonly ConcurrentQueue<string> _pending = new ConcurrentQueue<string>();
        private readonly DispatcherTimer _flushTimer;
        private const int MaxUiLines = 10_000;    // keep last 10K lines visible
        private const int MaxFlushPerTick = 500;  // cap per tick to keep UI snappy

        public MainViewModel()
        {
            _flushTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(75)
            };
            _flushTimer.Tick += (_, __) => FlushPendingToUi();
            _flushTimer.Start();
        }

        // Called by Run button in code-behind
        public async Task RunQueueAsync()
        {
            if (IsRunning || QueueItems.Count == 0) return;

            IsRunning = true;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            var progress = new Progress<CommandOutput>(co =>
            {
                var text = co.IsError ? "[ERR] " + co.Line : co.Line;
                _pending.Enqueue(text);
            });

            try
            {
                await _runner.RunQueueAsync(QueueItems, WorkingDirectory, StopOnError, progress, ct)
                             .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected on Stop()
            }
            catch (Exception ex)
            {
                _pending.Enqueue("[ERR] " + ex.Message);
                LoggingService.Log("[ERR] " + ex);
            }

            finally
            {
                IsRunning = false;
                _cts?.Dispose();
                _cts = null;

                // use WPF Application alias
                await WpfApp.Current.Dispatcher.InvokeAsync(FlushPendingToUi);
            }


        }

        // Called by Stop button in code-behind
        public void Stop() => _cts?.Cancel();

        // --- Queue helpers your window already calls ---
        public void MoveQueueItem(int index, int delta)
        {
            if (index < 0 || QueueItems.Count == 0) return;
            int newIndex = Clamp(index + delta, 0, QueueItems.Count - 1);
            if (newIndex == index) return;

            var item = QueueItems[index];
            QueueItems.RemoveAt(index);
            QueueItems.Insert(newIndex, item);
        }

        public void RemoveQueueItem(int index)
        {
            if (index >= 0 && index < QueueItems.Count)
                QueueItems.RemoveAt(index);
        }

        public void ClearQueue() => QueueItems.Clear();

        // --- Stubs you already reference from code-behind (fill as needed) ---
        public void AutoDetectWorkingDirectory() { /* TODO */ }
        public void TestMeterMate() { /* TODO */ }
        public void RefreshPorts() { /* TODO */ }
        public void AddSelectedTemplateToQueue() { /* TODO: QueueItems.Add((cmd, display)); */ }
        public void SavePreset(string name) { /* TODO */ }
        public void LoadPreset(object preset) { /* TODO */ }

        public void OpenTemplateEditor()
        {
            var editor = new TemplateEditor(this) { Owner = WpfApp.Current.MainWindow };
            if (editor.ShowDialog() == true)
            {
                var edited = editor.VM;
                // ApplyTemplateEdits(edited);
            }
        }

        public void SaveAll() { /* TODO: persist settings/profiles */ }
        public void ExportTemplatesTo(string file) { /* TODO */ }
        public void ImportTemplatesFrom(string file) { /* TODO */ }
        public void ExportPresetsTo(string file, bool includeEncryptedPasswords) { /* TODO */ }
        public void ImportPresetsFrom(string file) { /* TODO */ }
        public void ExportSequencesTo(string file) { /* TODO */ }
        public void ImportSequencesFrom(string file) { /* TODO */ }

        // ---- batching & UI update ----
        private void FlushPendingToUi()
        {
            if (_pending.IsEmpty) return;

            int added = 0;
            while (added < MaxFlushPerTick && _pending.TryDequeue(out var line))
            {
                OutputLines.Add(line);
                added++;
            }

            int overflow = OutputLines.Count - MaxUiLines;
            if (overflow > 0)
            {
                // remove oldest entries (batched)
                for (int i = 0; i < overflow; i++)
                    OutputLines.RemoveAt(0);
            }
        }

        private static int Clamp(int value, int min, int max)
            => value < min ? min : (value > max ? max : value);

        // ---- INotifyPropertyChanged ----

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));


    }
}