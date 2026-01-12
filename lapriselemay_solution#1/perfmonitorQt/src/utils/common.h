#pragma once

/**
 * @file common.h
 * @brief Common utilities and constants for PerfMonitorQt
 * 
 * This file centralizes shared functionality to avoid code duplication.
 * Use SystemInfo namespace for format helpers and system queries.
 */

#include "systeminfo.h"

namespace PerfMonitor {

// ============================================================================
// Constants
// ============================================================================

namespace Colors {
    // Status colors
    constexpr const char* Good = "#00aa00";
    constexpr const char* Warning = "#ffaa00";
    constexpr const char* Critical = "#ff0000";
    constexpr const char* Neutral = "#0078d7";
    
    // Chart colors
    constexpr const char* CpuColor = "#0078d7";
    constexpr const char* MemoryColor = "#8b008b";
    constexpr const char* GpuColor = "#76b900";
    constexpr const char* GpuMemoryColor = "#e535ab";
    constexpr const char* DiskReadColor = "#00aa00";
    constexpr const char* DiskWriteColor = "#cc6600";
    constexpr const char* NetworkSendColor = "#cc6600";
    constexpr const char* NetworkRecvColor = "#00aa00";
    constexpr const char* BatteryColor = "#00aa00";
}

namespace Thresholds {
    // CPU thresholds
    constexpr double CpuWarning = 70.0;
    constexpr double CpuCritical = 90.0;
    
    // Memory thresholds  
    constexpr double MemoryWarning = 70.0;
    constexpr double MemoryCritical = 85.0;
    
    // Temperature thresholds (Celsius)
    constexpr double TempWarning = 60.0;
    constexpr double TempCritical = 80.0;
    
    // Battery thresholds
    constexpr int BatteryWarning = 30;
    constexpr int BatteryCritical = 15;
    
    // Disk usage thresholds
    constexpr double DiskWarning = 75.0;
    constexpr double DiskCritical = 90.0;
}

namespace Intervals {
    constexpr int DefaultUpdateMs = 1000;
    constexpr int FastUpdateMs = 500;
    constexpr int SlowUpdateMs = 2000;
    constexpr int MetricsRecordMs = 5000;
}

// ============================================================================
// Utility Functions (delegates to SystemInfo)
// ============================================================================

/**
 * @brief Format bytes to human-readable string
 * @note Use SystemInfo::formatBytes() directly when possible
 */
inline QString formatBytes(qint64 bytes) {
    return SystemInfo::formatBytes(bytes);
}

/**
 * @brief Check if running as administrator
 * @note Use SystemInfo::isAdministrator() directly when possible
 */
inline bool isAdmin() {
    return SystemInfo::isAdministrator();
}

/**
 * @brief Get color based on percentage value and thresholds
 */
inline QString getStatusColor(double value, double warningThreshold, double criticalThreshold) {
    if (value >= criticalThreshold) return Colors::Critical;
    if (value >= warningThreshold) return Colors::Warning;
    return Colors::Good;
}

/**
 * @brief Get temperature color based on value
 */
inline QString getTempColor(double tempCelsius) {
    return getStatusColor(tempCelsius, Thresholds::TempWarning, Thresholds::TempCritical);
}

/**
 * @brief Get CPU usage color
 */
inline QString getCpuColor(double usage) {
    return getStatusColor(usage, Thresholds::CpuWarning, Thresholds::CpuCritical);
}

/**
 * @brief Get memory usage color
 */
inline QString getMemoryColor(double usage) {
    return getStatusColor(usage, Thresholds::MemoryWarning, Thresholds::MemoryCritical);
}

/**
 * @brief Get battery color based on percentage
 */
inline QString getBatteryColor(int percentage) {
    if (percentage <= Thresholds::BatteryCritical) return Colors::Critical;
    if (percentage <= Thresholds::BatteryWarning) return Colors::Warning;
    return Colors::Good;
}

} // namespace PerfMonitor
