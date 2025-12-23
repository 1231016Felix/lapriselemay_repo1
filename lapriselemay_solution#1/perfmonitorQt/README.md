# PerfMonitorQt

A modern Windows 11 Performance Monitor built with C++20 and Qt 6.

![License](https://img.shields.io/badge/license-MIT-blue.svg)
![Platform](https://img.shields.io/badge/platform-Windows%2011-blue.svg)
![C++](https://img.shields.io/badge/C%2B%2B-20-blue.svg)
![Qt](https://img.shields.io/badge/Qt-6.x-green.svg)

## Features

### System Monitoring
- **CPU**: Real-time usage, per-core monitoring, processor info, uptime
- **Memory**: RAM usage, committed memory, cached, paged pool
- **Disk**: Drive information, read/write activity with sparkline graphs
- **Network**: Adapter list, send/receive rates, connection status

### Battery Monitoring (Surface-optimized)
- Battery percentage and charging status
- Time remaining estimation
- Battery health percentage
- Cycle count
- Design vs current capacity
- Voltage and temperature (when available)

### Process Management
- Full process list with search/filter
- CPU and memory usage per process
- Thread and handle counts
- End task functionality

### User Interface
- Modern dark theme (Windows 11 style)
- Real-time sparkline graphs
- System tray integration with CPU indicator
- Minimize to tray option
- Always on top mode
- Tab-based navigation

## Requirements

- Windows 10/11 (64-bit)
- Qt 6.5 or later
- CMake 3.20+
- Visual Studio 2022 or compatible C++20 compiler

## Building

### Prerequisites

1. Install Qt 6.5+ with the following components:
   - Qt Core
   - Qt GUI
   - Qt Widgets
   - Qt Charts
   - Qt Network

2. Install CMake 3.20 or later

3. Install Visual Studio 2022 with C++ development tools

### Build Steps

```powershell
# Clone or download the project
cd perfmonitorQt

# Create build directory
mkdir build
cd build

# Configure with CMake
cmake .. -G "Visual Studio 17 2022" -A x64 -DCMAKE_PREFIX_PATH="C:/Qt/6.x.x/msvc2022_64"

# Build
cmake --build . --config Release

# Deploy Qt dependencies
windeployqt Release/PerfMonitorQt.exe
```

### Quick Build Script

```powershell
./build.ps1
```

## Installation

### Using the Installer

1. Build the project
2. Install [Inno Setup](https://jrsoftware.org/isinfo.php)
3. Run `installer/setup.iss` with Inno Setup Compiler
4. The installer will be created in `installer/output/`

### Portable Version

Simply copy the `build/Release` folder after running `windeployqt`.

## Usage

### Command Line Options

```
PerfMonitorQt.exe [options]

Options:
  --minimized    Start minimized to system tray
  --help         Show help message
```

### Keyboard Shortcuts

- `Ctrl+S` - Export report
- `Ctrl+Q` - Exit application
- `F5` - Refresh process list

## Project Structure

```
perfmonitorQt/
├── CMakeLists.txt
├── README.md
├── LICENSE.txt
├── src/
│   ├── main.cpp
│   ├── mainwindow.h/cpp
│   ├── monitors/
│   │   ├── cpumonitor.h/cpp
│   │   ├── memorymonitor.h/cpp
│   │   ├── diskmonitor.h/cpp
│   │   ├── networkmonitor.h/cpp
│   │   ├── batterymonitor.h/cpp
│   │   └── processmonitor.h/cpp
│   ├── widgets/
│   │   ├── sparklinegraph.h/cpp
│   │   ├── systemtray.h/cpp
│   │   └── processdialog.h/cpp
│   └── utils/
│       └── systeminfo.h/cpp
├── resources/
│   ├── resources.qrc
│   └── icons/
└── installer/
    └── setup.iss
```

## Technical Details

### Windows APIs Used

- `GetSystemTimes()` - CPU usage calculation
- `GlobalMemoryStatusEx()` - Memory information
- `GetLogicalDrives()` / `GetDiskFreeSpaceEx()` - Disk information
- `IOCTL_DISK_PERFORMANCE` - Disk activity
- `GetIfTable2()` / `GetAdaptersAddresses()` - Network information
- `IOCTL_BATTERY_*` - Battery detailed information
- `CreateToolhelp32Snapshot()` - Process enumeration
- PDH (Performance Data Helper) - Per-core CPU usage

### C++20 Features Used

- Concepts
- std::jthread (for background updates)
- Ranges
- Designated initializers
- [[nodiscard]] attribute

## License

MIT License - See [LICENSE.txt](LICENSE.txt)

## Author

**Félix-Antoine**

## Acknowledgments

- Qt Framework
- Microsoft Windows SDK
- Inno Setup
