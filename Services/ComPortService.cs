// Services/ComPortService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using CmdRunnerPro.Models;

namespace CmdRunnerPro.Services
{
    public static class ComPortService
    {
        // Registry path that lists COM ports (value = "COMx")
        private const string SerialCommKey = @"HARDWARE\DEVICEMAP\SERIALCOMM";

        // P/Invoke to probe if a COM port is open (in use) without System.IO.Ports
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        private const uint FILE_SHARE_NONE = 0x00000000;

        private const int ERROR_ACCESS_DENIED = 5;
        private const int ERROR_SHARING_VIOLATION = 32;

        public static List<PortInfo> GetPorts()
        {
            var ports = new List<PortInfo>();

            using (var key = Registry.LocalMachine.OpenSubKey(SerialCommKey, writable: false))
            {
                if (key != null)
                {
                    foreach (var valueName in key.GetValueNames())
                    {
                        var portName = key.GetValue(valueName) as string;
                        if (string.IsNullOrWhiteSpace(portName))
                            continue;

                        var normalized = portName.Trim();
                        var info = new PortInfo
                        {
                            Name = normalized,
                            InUse = IsPortInUse(normalized)
                        };
                        ports.Add(info);
                    }
                }
            }

            // Deduplicate and sort naturally by COM number
            var ordered = ports
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(p => PortNumber(p.Name))
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return ordered;
        }

        private static int PortNumber(string name)
        {
            // Extract numeric suffix from "COMx"
            if (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(name.Substring(3), out var n))
                    return n;
            }
            return int.MaxValue;
        }

        private static bool IsPortInUse(string comName)
        {
            // Always use the \\.\COMx device path to handle COM10+ correctly
            string devicePath = @"\\.\" + comName;

            // Try to open with no sharing; success => not in use, failure with AccessDenied/SharingViolation => in use
            using var handle = CreateFile(
                devicePath,
                GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_NONE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL,
                IntPtr.Zero);

            if (!handle.IsInvalid)
            {
                // Successfully opened — not in use
                return false;
            }

            int err = Marshal.GetLastWin32Error();
            if (err == ERROR_ACCESS_DENIED || err == ERROR_SHARING_VIOLATION)
            {
                return true; // in use
            }

            // Other errors (e.g., file not found) — treat as not in use for discovery purposes
            return false;
        }
    }
}