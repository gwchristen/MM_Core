
# CMD Runner Pro (WPF, .NET 8)

A WPF utility to queue and run Windows CMD commands with live output, logging, COM port selection, MeterMate integration, secure password handling (PasswordBox + DPAPI), template editor, presets/sequences, and export/import.

## Requirements
- Windows 10/11
- .NET 8 SDK or Visual Studio 2022 (17.8+) with .NET 8 workloads

## Build
```
cd MMCore
# Option 1: CLI
dotnet build
# Option 2: Visual Studio
#   - Open MMCore.csproj and press F5
```

## Run
First run will create `%AppData%/MMCore/settings.json` and a `Logs` folder.

## Features
- Execute CMD commands (runs via `cmd.exe /C`) with selectable Working Directory
- Live stdout/stderr (stderr colorized) and daily log files
- Auto-detect MeterMate install directory and test button (path + version)
- COM port discovery + in-use indication
- 6 inputs: COM1, COM2, Username, Password (masked + encrypted at rest), Opco, Program
- Templates (with token expansion), Template Editor
- Queue: add templates, reorder, run sequentially, stop on error
- Presets & Sequences (sequences reference template names only)
- Export/Import: Templates, Presets (portable or with encrypted passwords), Sequences

## Security
- Password is masked in UI (PasswordBox)
- Password stored **encrypted** at rest (Windows DPAPI, user scope)
- Queue/logs show **redacted** commands so secrets never leak
- Sequences persist **template names only** (no expanded commands)

## Tokens
Use tokens inside templates, e.g.:
- `{comport1}`, `{comport2}`
- `{username}`, `{password}`, `{opco}`, `{program}`
- `{wd}` for working directory
- `{Q:token}` variant will auto-quote if value contains spaces
- Aliases supported: `{COM1}`, `{COM2}`, `{FIELD3..6}`, `{WD}`

## Notes
- For Folder selection we use WinForms `FolderBrowserDialog`; project enables `<UseWindowsForms>true</UseWindowsForms>`.
- `System.IO.Ports` is used to enumerate COM ports (Windows).
- MeterMate detection looks for `MeterMate X.YZ` under Program Files and selects highest version.

## Export/Import
- Templates: JSON array of `{ Name, Template }`
- Presets: JSON array; default export **omits passwords**; optional export includes `PasswordEnc` (DPAPI-bound)
- Sequences: JSON array `{ Name, TemplateNames: [..] }`

## License
Internal utility for Gary Christen. Adapt as needed.
