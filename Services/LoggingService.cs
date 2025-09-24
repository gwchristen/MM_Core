
using System;
using System.IO;

namespace CmdRunnerPro.Services
{
    public static class LoggingService
    {
        public static string CurrentLogFile
        {
            get
            {
                var folder = SettingsService.LogsFolder;
                Directory.CreateDirectory(folder);
                var stamp = DateTime.Now.ToString("yyyy-MM-dd");
                return Path.Combine(folder, $"{stamp}.log");
            }
        }

        public static void Log(string line)
        {
            var entry = $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}";
            File.AppendAllText(CurrentLogFile, entry);
        }
    }
}
