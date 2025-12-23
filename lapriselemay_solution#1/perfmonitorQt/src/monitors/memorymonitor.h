#pragma once

#include <QObject>

struct MemoryInfo {
    double totalGB{0.0};
    double usedGB{0.0};
    double availableGB{0.0};
    double usagePercent{0.0};
    double committedGB{0.0};
    double commitLimitGB{0.0};
    double cachedGB{0.0};
    double pagedPoolMB{0.0};
    double nonPagedPoolMB{0.0};
};

class MemoryMonitor : public QObject
{
    Q_OBJECT

public:
    explicit MemoryMonitor(QObject *parent = nullptr);
    ~MemoryMonitor() override = default;

    void update();
    [[nodiscard]] const MemoryInfo& info() const { return m_info; }
    
    // Memory purge functions (require admin privileges)
    static bool purgeStandbyList();
    static bool purgeWorkingSets();
    static bool purgeAllMemory();
    static bool isAdministrator();

private:
    MemoryInfo m_info;
};
