
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MMCore.Services
{
    public static class MeterMateService
    {
        private static readonly string[] ProgramFilesCandidates = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };

        public static string? FindInstallDirectory()
        {
            foreach (var baseDir in ProgramFilesCandidates)
            {
                if (string.IsNullOrWhiteSpace(baseDir) || !Directory.Exists(baseDir)) continue;

                var dirs = Directory.EnumerateDirectories(baseDir, "MeterMate *", SearchOption.TopDirectoryOnly)
                                    .Where(d => File.Exists(Path.Combine(d, "MeterMate.exe")))
                                    .ToList();
                if (dirs.Count == 0) continue;

                var rx = new Regex(@"MeterMate\s+(\d+(?:\.\d+)*)", RegexOptions.IgnoreCase);
                var best = dirs
                    .Select(d =>
                    {
                        var name = Path.GetFileName(d);
                        var m = rx.Match(name);
                        Version? v = null;
                        if (m.Success && Version.TryParse(m.Groups[1].Value, out var parsed)) v = parsed;
                        return new { Dir = d, Version = v };
                    })
                    .OrderByDescending(x => x.Version ?? new Version(0, 0))
                    .FirstOrDefault();

                if (best != null) return best.Dir;
            }
            return null;
        }

        public static string? FindExecutable(string? workingDirectory)
        {
            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                var wdExe = Path.Combine(workingDirectory, "MeterMate.exe");
                if (File.Exists(wdExe)) return wdExe;
            }

            var installDir = FindInstallDirectory();
            if (!string.IsNullOrWhiteSpace(installDir))
            {
                var exe = Path.Combine(installDir, "MeterMate.exe");
                if (File.Exists(exe)) return exe;
            }

            try
            {
                var path = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (var p in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    var exe = Path.Combine(p.Trim(), "MeterMate.exe");
                    if (File.Exists(exe)) return exe;
                }
            }
            catch { }

            return null;
        }

        public static string? GetFileVersion(string exePath)
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(exePath);
                return info.ProductVersion ?? info.FileVersion ?? "";
            }
            catch { return null; }
        }
    }
}
