#pragma once

#include <QObject>
#include <QString>
#include <vector>
#include <memory>

#ifdef _WIN32
#include <Windows.h>
#include <pdh.h>
#endif

struct CpuInfo {
    QString name;
    double usage{0.0};
    double currentSpeed{0.0};
    double baseSpeed{0.0};
    int cores{0};
    int logicalProcessors{0};
    int processCount{0};
    int threadCount{0};
    QString uptime;
    std::vector<double> coreUsages;
};

class CpuMonitor : public QObject
{
    Q_OBJECT

public:
    explicit CpuMonitor(QObject *parent = nullptr);
    ~CpuMonitor() override;

    void update();
    [[nodiscard]] const CpuInfo& info() const { return m_info; }

private:
    void initializePdh();
    void queryProcessorName();
    void queryProcessorInfo();
    QString formatUptime(qint64 milliseconds);

    CpuInfo m_info;
    
#ifdef _WIN32
    PDH_HQUERY m_query{nullptr};
    PDH_HCOUNTER m_cpuCounter{nullptr};
    std::vector<PDH_HCOUNTER> m_coreCounters;
    FILETIME m_prevIdleTime{};
    FILETIME m_prevKernelTime{};
    FILETIME m_prevUserTime{};
    bool m_pdhInitialized{false};
#endif
};
