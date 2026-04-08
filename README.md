# disk0 - Visual Disk Space Analyzer

**disk0** is a powerful, visual disk space analyzer for Windows. It scans drives and directories, displaying storage consumption as interactive Sunburst or Treemap charts.

![disk0](https://img.shields.io/badge/version-1.1-blue) ![License](https://img.shields.io/badge/license-Freeware-green) ![Platform](https://img.shields.io/badge/platform-Windows%2064--bit-lightgrey)

## Features

### Visualization
- **Sunburst chart** (radial) - rings build up live during scan, from inside out
- **Treemap chart** (rectangular) - proportional area view
- **Three color modes**: Default, File Type (video/images/audio/docs/archives), Age (green=new, red=old)
- Click to select, right-click for context menu, zoom into subdirectories

### Analysis Tools
All analysis runs on the selected directory and all its subdirectories.

| Tool | Description |
|------|-------------|
| **Top 50 Largest Files** | Finds the biggest space consumers |
| **Old Files** | Files not modified in 2+ or 5+ years (> 1 MB) |
| **Temp/Cache Finder** | Detects `node_modules`, `.git`, `bin/obj`, browser caches, `%TEMP%`, Docker, Windows Update cache |
| **Duplicate Finder** | Files with same size + partial SHA256 hash |
| **Delete Candidates** | `.tmp`, `.log`, `.bak`, `.old`, `.dmp`, Office temp files, `Thumbs.db` |

Analysis results support **batch selection and deletion** (Select All / Deselect All / Delete Selected).

### Additional Features
- **7 Languages**: English, Deutsch, Francais, Espanol, Japanese, Chinese, Russian (auto-detects system language)
- **OneDrive / Cloud support**: Scans cloud-placeholder folders correctly (distinguishes symlinks from cloud reparse points)
- **Network paths**: Scan UNC paths like `\\server\share` or browse for folders
- **Explorer integration**: Register "Scan with disk0" in Windows Explorer right-click menu
- **Drive info**: Total, used, and free space with percentages
- **File list panel**: Right-side panel showing files sorted by size, with optional date column
- **Auto-scan on start**: Optional setting to scan automatically when the app launches
- **Custom dark UI**: No Windows title bar, fully resizable, custom window controls
- **Recycle Bin**: Highlighted in red with dashed border, option to empty from context menu
- **Delete directories**: Right-click any directory to delete it (with confirmation)
- **Standalone EXE**: Single-file self-contained executable, no .NET installation required

## Screenshots

*Sunburst view scanning C: drive with file type coloring*

## Getting Started

### Run from source
```bash
cd DriveScan
dotnet run
```

### Build standalone EXE
```bash
cd DriveScan
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```
Output: `bin/Release/net9.0-windows/win-x64/publish/DriveScan.exe` (~129 MB)

### Command line
```bash
DriveScan.exe --scan "C:\Users\MyFolder"
```

## Explorer Context Menu Integration

1. Run disk0 **as Administrator**
2. Menu: View > "Add to Explorer context menu"
3. Now right-click any folder in Explorer > "Scan with disk0"

To remove: View > "Remove from Explorer context menu"

## Requirements

- **Run from source**: .NET 9.0 SDK
- **Standalone EXE**: Windows 10/11 64-bit (no dependencies)

## Project Structure

```
DriveScan/
  Controls/          Sunburst and Treemap chart controls
  Models/            DirectoryNode data model
  Services/          Scanner, Analysis, Localization, Settings, Shell integration
  Views/             Analysis window, Input dialog
  MainWindow.xaml    Main application window
```

## License

**Freeware** - (c) CSBG.BIZ - All rights reserved.

This software is provided "as is" without warranty of any kind. Use at your own risk. Source code available on request.

Contact: disk0@csbg.biz
