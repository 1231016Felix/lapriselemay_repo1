#pragma once

#include <QObject>
#include <QString>
#include <QAbstractTableModel>
#include <vector>
#include <memory>

struct GpuInfo {
    QString name;
    QString vendor;
    QString driverVersion;
    
    // Memory
    qint64 dedicatedMemoryTotal{0};     // bytes
    qint64 dedicatedMemoryUsed{0};      // bytes
    qint64 sharedMemoryTotal{0};        // bytes
    qint64 sharedMemoryUsed{0};         // bytes
    double memoryUsagePercent{0.0};
    
    // Usage
    double usage{0.0};                  // GPU usage %
    double usage3D{0.0};                // 3D engine %
    double usageCopy{0.0};              // Copy engine %
    double usageVideoDecode{0.0};       // Video decode %
    double usageVideoEncode{0.0};       // Video encode %
    
    // Temperature (if available)
    double temperature{-999.0};         // Celsius, -999 = not available
    
    // Clock speeds (if available)
    int currentClockMHz{0};
    int maxClockMHz{0};
    
    // Power (if available)
    double powerWatts{0.0};
    
    // Identification
    int index{0};
    bool isDiscrete{false};             // true = dedicated GPU, false = integrated
    quint32 vendorId{0};
    quint32 deviceId{0};
};

class GpuTableModel : public QAbstractTableModel
{
    Q_OBJECT

public:
    explicit GpuTableModel(QObject *parent = nullptr);
    
    void setGpus(const std::vector<GpuInfo>& gpus);
    
    int rowCount(const QModelIndex &parent = QModelIndex()) const override;
    int columnCount(const QModelIndex &parent = QModelIndex()) const override;
    QVariant data(const QModelIndex &index, int role = Qt::DisplayRole) const override;
    QVariant headerData(int section, Qt::Orientation orientation, int role) const override;

private:
    std::vector<GpuInfo> m_gpus;
};

class GpuMonitor : public QObject
{
    Q_OBJECT

public:
    explicit GpuMonitor(QObject *parent = nullptr);
    ~GpuMonitor() override;

    void update();
    [[nodiscard]] const std::vector<GpuInfo>& gpus() const { return m_gpus; }
    [[nodiscard]] const GpuInfo& primaryGpu() const;
    [[nodiscard]] int gpuCount() const { return static_cast<int>(m_gpus.size()); }
    [[nodiscard]] QAbstractTableModel* model() { return m_model.get(); }
    
    static QString formatMemory(qint64 bytes);

private:
    void enumerateGpus();
    void queryGpuUsage();
    void queryGpuMemory();
    void queryNvidiaInfo();
    void queryAmdInfo();
    
    std::vector<GpuInfo> m_gpus;
    std::unique_ptr<GpuTableModel> m_model;
    int m_primaryGpuIndex{0};
    
#ifdef _WIN32
    // D3DKMT handles
    void* m_d3dkmt{nullptr};
    bool m_d3dkmtInitialized{false};
    
    // PDH for GPU usage
    void* m_pdhQuery{nullptr};
    std::vector<void*> m_pdhCounters;
    bool m_pdhInitialized{false};
#endif
};
