#include "monitorworker.h"
#include <QTimer>
#include <QDateTime>
#include <QDebug>

// Register metatype for queued connections
static bool registerMetaTypes() {
    qRegisterMetaType<MonitorData>("MonitorData");
    return true;
}
static bool s_metaTypesRegistered = registerMetaTypes();

MonitorWorker::MonitorWorker(QObject* parent)
    : QObject(parent)
{
}

MonitorWorker::~MonitorWorker()
{
    stop();
}

void MonitorWorker::start(int intervalMs)
{
    if (m_running.load()) {
        return;
    }
    
    m_intervalMs.store(intervalMs);
    m_running.store(true);
    
    // Create thread
    m_thread = std::make_unique<QThread>();
    
    // Move worker to thread
    this->moveToThread(m_thread.get());
    
    // Connect thread started to work function
    connect(m_thread.get(), &QThread::started, this, &MonitorWorker::doWork);
    
    // Start thread
    m_thread->start();
}

void MonitorWorker::stop()
{
    if (!m_running.load()) {
        return;
    }
    
    m_running.store(false);
    
    // Wake up the worker if it's waiting
    {
        QMutexLocker locker(&m_mutex);
        m_updateRequested = true;
        m_condition.wakeOne();
    }
    
    // Wait for thread to finish
    if (m_thread && m_thread->isRunning()) {
        m_thread->quit();
        m_thread->wait(3000);
    }
    
    m_thread.reset();
}

void MonitorWorker::setInterval(int intervalMs)
{
    m_intervalMs.store(intervalMs);
}

void MonitorWorker::requestUpdate()
{
    QMutexLocker locker(&m_mutex);
    m_updateRequested = true;
    m_condition.wakeOne();
}

void MonitorWorker::initializeMonitors()
{
    // Create monitors in the worker thread
    m_cpuMonitor = std::make_unique<CpuMonitor>();
    m_memoryMonitor = std::make_unique<MemoryMonitor>();
    m_gpuMonitor = std::make_unique<GpuMonitor>();
    m_diskMonitor = std::make_unique<DiskMonitor>();
    m_networkMonitor = std::make_unique<NetworkMonitor>();
    m_batteryMonitor = std::make_unique<BatteryMonitor>();
    m_temperatureMonitor = std::make_unique<TemperatureMonitor>();
}

void MonitorWorker::doWork()
{
    // Initialize monitors in worker thread
    initializeMonitors();
    
    while (m_running.load()) {
        // Collect all monitor data
        MonitorData data;
        
        try {
            collectData(data);
            data.timestamp = QDateTime::currentMSecsSinceEpoch();
            
            // Emit data to UI thread (queued connection)
            emit dataReady(data);
        }
        catch (const std::exception& e) {
            emit errorOccurred(QString("Monitor error: %1").arg(e.what()));
        }
        
        // Wait for next interval or manual request
        {
            QMutexLocker locker(&m_mutex);
            if (!m_updateRequested && m_running.load()) {
                m_condition.wait(&m_mutex, m_intervalMs.load());
            }
            m_updateRequested = false;
        }
    }
    
    // Cleanup monitors
    m_cpuMonitor.reset();
    m_memoryMonitor.reset();
    m_gpuMonitor.reset();
    m_diskMonitor.reset();
    m_networkMonitor.reset();
    m_batteryMonitor.reset();
    m_temperatureMonitor.reset();
}

void MonitorWorker::collectData(MonitorData& data)
{
    // CPU - usually fast
    if (m_cpuMonitor) {
        m_cpuMonitor->update();
        data.cpu = m_cpuMonitor->info();
    }
    
    // Memory - fast
    if (m_memoryMonitor) {
        m_memoryMonitor->update();
        data.memory = m_memoryMonitor->info();
    }
    
    // Temperature - can be slow (WMI)
    if (m_temperatureMonitor) {
        m_temperatureMonitor->update();
        data.temperature = m_temperatureMonitor->info();
    }
    
    // GPU - can be slow
    if (m_gpuMonitor) {
        m_gpuMonitor->update();
        data.gpus = m_gpuMonitor->gpus();
        data.primaryGpu = m_gpuMonitor->primaryGpu();
    }
    
    // Disk - moderate
    if (m_diskMonitor) {
        m_diskMonitor->update();
        data.disks = m_diskMonitor->disks();
        data.diskActivity = m_diskMonitor->activity();
    }
    
    // Network - moderate
    if (m_networkMonitor) {
        m_networkMonitor->update();
        data.networkAdapters = m_networkMonitor->adapters();
        data.networkActivity = m_networkMonitor->activity();
    }
    
    // Battery - can be slow on some systems
    if (m_batteryMonitor) {
        m_batteryMonitor->update();
        data.battery = m_batteryMonitor->info();
    }
}
