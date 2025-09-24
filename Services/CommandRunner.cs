
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace CmdRunnerPro.Services
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
            IEnumerable<(string Command, string Display)> queueItems,
            string workingDirectory,
            bool stopOnError,
            IProgress<CommandOutput> progress,
            CancellationToken ct)
        {
            foreach (var (command, display) in queueItems)
            {
                if (ct.IsCancellationRequested) return false;

                progress.Report(new CommandOutput { Line = "> " + display });
                LoggingService.Log("> " + display);

                var code = await RunSingleAsync(command, workingDirectory, progress, ct);
                progress.Report(new CommandOutput { Line = "[exit " + code + "]" });

                if (code != 0 && stopOnError)
                    return false;
            }
            return true;
        }
    }
}
