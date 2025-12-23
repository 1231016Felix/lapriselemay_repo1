#pragma once

#include <QString>

namespace SystemInfo {

// Format helpers
QString formatBytes(qint64 bytes);
QString formatBytesPerSecond(qint64 bytesPerSec);
QString formatDuration(qint64 milliseconds);
QString formatPercentage(double value, int decimals = 1);

// System queries
QString getOSVersion();
QString getComputerName();
QString getUserName();
bool isAdministrator();
bool is64BitOS();
bool is64BitProcess();

// Hardware info
QString getCpuName();
int getCpuCoreCount();
int getCpuThreadCount();
qint64 getTotalPhysicalMemory();
qint64 getAvailablePhysicalMemory();

// Power info
bool hasBattery();
int getBatteryPercentage();
bool isOnACPower();

} // namespace SystemInfo
