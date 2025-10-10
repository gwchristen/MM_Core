
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using static MMCore.ViewModels.MainViewModel;
using MMCore.ViewModels;

namespace MMCore.Services
{
    public class CommandOutput
    {
        public bool IsError { get; set; }
        public string Line { get; set; } = "";
    }

    public class CommandRunner
    {
        public async Task<int> RunSingleAsync(
            string command,
            string workingDirectory,
            IProgress<CommandOutput> progress,
            CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/C " + command,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

            var tcs = new TaskCompletionSource<int>();

            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    progress.Report(new CommandOutput { IsError = false, Line = e.Data });
                    LoggingService.Log(e.Data);
                }
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    progress.Report(new CommandOutput { IsError = true, Line = e.Data });
                    LoggingService.Log("[ERR] " + e.Data);
                }
            };

            ct.Register(() =>
            {
                try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            });

            proc.Exited += (_, __) => tcs.TrySetResult(proc.ExitCode);

            if (!proc.Start())
                throw new InvalidOperationException("Failed to start process.");

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            var exitCode = await tcs.Task.ConfigureAwait(false);
            return exitCode;
        }


        public async Task<bool> RunQueueAsync(
            IEnumerable<(string Command, string DisplayVerbose, string DisplayFriendly)> queueItems,
            string workingDirectory,
            bool stopOnError,
            bool showDetailed, // new
            IProgress<MainViewModel.CommandOutputWithState> progress,
            CancellationToken ct)
        {
            foreach (var item in queueItems)
            {
                if (ct.IsCancellationRequested) return false;

                // Pick display mode
                var display = showDetailed ? item.DisplayVerbose : item.DisplayFriendly;

                // Show the pre-exec line ("> ...")
                progress.Report(new MainViewModel.CommandOutputWithState
                {
                    Output = new CommandOutput { IsError = false, Line = "> " + display },
                    CurrentCommand = display
                });
                LoggingService.Log("> " + display);

                // Run the command
                var code = await RunSingleAsync(item.Command, workingDirectory,
                    new Progress<CommandOutput>(output =>
                    {
                        progress.Report(new MainViewModel.CommandOutputWithState
                        {
                            Output = output,
                            CurrentCommand = display
                        });
                    }), ct);

                // Exit code line: only when detailed output is ON
                if (showDetailed)
                {
                    progress.Report(new MainViewModel.CommandOutputWithState
                    {
                        Output = new CommandOutput { IsError = false, Line = "[exit " + code + "]" },
                        CurrentCommand = display
                    });
                    LoggingService.Log("[exit " + code + "]");
                }

                if (code != 0 && stopOnError)
                    return false;
            }

            return true;
        }
    }
}
