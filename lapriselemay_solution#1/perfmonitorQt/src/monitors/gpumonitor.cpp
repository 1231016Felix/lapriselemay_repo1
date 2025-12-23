#include "gpumonitor.h"

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <Windows.h>
#include <winternl.h>  // Pour NTSTATUS
#include <dxgi1_4.h>
#include <d3d11.h>
#include <pdh.h>
#include <vector>

#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "pdh.lib")

// D3DKMT structures for GPU usage
typedef UINT D3DKMT_HANDLE;

typedef struct _D3DKMT_OPENADAPTERFROMLUID {
    LUID AdapterLuid;
    D3DKMT_HANDLE hAdapter;
} D3DKMT_OPENADAPTERFROMLUID;

typedef struct _D3DKMT_CLOSEADAPTER {
    D3DKMT_HANDLE hAdapter;
} D3DKMT_CLOSEADAPTER;

typedef struct _D3DKMT_QUERYSTATISTICS_COUNTER {
    ULONGLONG Count;
    ULONGLONG Bytes;
} D3DKMT_QUERYSTATISTICS_COUNTER;

typedef enum _D3DKMT_QUERYSTATISTICS_TYPE {
    D3DKMT_QUERYSTATISTICS_ADAPTER = 0,
    D3DKMT_QUERYSTATISTICS_PROCESS = 1,
    D3DKMT_QUERYSTATISTICS_PROCESS_ADAPTER = 2,
    D3DKMT_QUERYSTATISTICS_SEGMENT = 3,
    D3DKMT_QUERYSTATISTICS_PROCESS_SEGMENT = 4,
    D3DKMT_QUERYSTATISTICS_NODE = 5,
    D3DKMT_QUERYSTATISTICS_PROCESS_NODE = 6,
    D3DKMT_QUERYSTATISTICS_VIDPNSOURCE = 7,
    D3DKMT_QUERYSTATISTICS_PROCESS_VIDPNSOURCE = 8
} D3DKMT_QUERYSTATISTICS_TYPE;

typedef struct _D3DKMT_QUERYSTATISTICS {
    D3DKMT_QUERYSTATISTICS_TYPE Type;
    LUID AdapterLuid;
    HANDLE hProcess;
    BYTE QueryResult[512];  // Union of various result types
} D3DKMT_QUERYSTATISTICS;

// Function pointers
typedef NTSTATUS(WINAPI* PFN_D3DKMTOpenAdapterFromLuid)(D3DKMT_OPENADAPTERFROMLUID*);
typedef NTSTATUS(WINAPI* PFN_D3DKMTCloseAdapter)(D3DKMT_CLOSEADAPTER*);
typedef NTSTATUS(WINAPI* PFN_D3DKMTQueryStatistics)(D3DKMT_QUERYSTATISTICS*);

static PFN_D3DKMTOpenAdapterFromLuid pfnD3DKMTOpenAdapterFromLuid = nullptr;
static PFN_D3DKMTCloseAdapter pfnD3DKMTCloseAdapter = nullptr;
static PFN_D3DKMTQueryStatistics pfnD3DKMTQueryStatistics = nullptr;
static bool g_d3dkmtLoaded = false;

static void LoadD3DKMT()
{
    if (g_d3dkmtLoaded) return;
    
    HMODULE hGdi32 = GetModuleHandleW(L"gdi32.dll");
    if (!hGdi32) {
        hGdi32 = LoadLibraryW(L"gdi32.dll");
    }
    
    if (hGdi32) {
        pfnD3DKMTOpenAdapterFromLuid = reinterpret_cast<PFN_D3DKMTOpenAdapterFromLuid>(
            GetProcAddress(hGdi32, "D3DKMTOpenAdapterFromLuid"));
        pfnD3DKMTCloseAdapter = reinterpret_cast<PFN_D3DKMTCloseAdapter>(
            GetProcAddress(hGdi32, "D3DKMTCloseAdapter"));
        pfnD3DKMTQueryStatistics = reinterpret_cast<PFN_D3DKMTQueryStatistics>(
            GetProcAddress(hGdi32, "D3DKMTQueryStatistics"));
    }
    
    g_d3dkmtLoaded = true;
}

#endif // _WIN32

// GpuTableModel implementation
GpuTableModel::GpuTableModel(QObject *parent)
    : QAbstractTableModel(parent)
{
}

void GpuTableModel::setGpus(const std::vector<GpuInfo>& gpus)
{
    beginResetModel();
    m_gpus = gpus;
    endResetModel();
}

int GpuTableModel::rowCount(const QModelIndex&) const
{
    return static_cast<int>(m_gpus.size());
}

int GpuTableModel::columnCount(const QModelIndex&) const
{
    return 5;
}

QVariant GpuTableModel::data(const QModelIndex &index, int role) const
{
    if (!index.isValid() || index.row() >= static_cast<int>(m_gpus.size()))
        return QVariant();

    const auto& gpu = m_gpus[index.row()];
    
    if (role == Qt::DisplayRole) {
        switch (index.column()) {
            case 0: return gpu.name;
            case 1: return QString("%1%").arg(gpu.usage, 0, 'f', 1);
            case 2: return GpuMonitor::formatMemory(gpu.dedicatedMemoryUsed);
            case 3: return GpuMonitor::formatMemory(gpu.dedicatedMemoryTotal);
            case 4: return QString("%1%").arg(gpu.memoryUsagePercent, 0, 'f', 1);
        }
    }
    else if (role == Qt::TextAlignmentRole) {
        if (index.column() >= 1)
            return Qt::AlignRight;
    }
    
    return QVariant();
}

QVariant GpuTableModel::headerData(int section, Qt::Orientation orientation, int role) const
{
    if (orientation != Qt::Horizontal || role != Qt::DisplayRole)
        return QVariant();

    switch (section) {
        case 0: return tr("GPU");
        case 1: return tr("Usage");
        case 2: return tr("Memory Used");
        case 3: return tr("Memory Total");
        case 4: return tr("Memory %");
    }
    return QVariant();
}

// GpuMonitor implementation
GpuMonitor::GpuMonitor(QObject *parent)
    : QObject(parent)
    , m_model(std::make_unique<GpuTableModel>())
{
#ifdef _WIN32
    LoadD3DKMT();
    
    // Initialize PDH for GPU usage
    PDH_STATUS status = PdhOpenQuery(nullptr, 0, reinterpret_cast<PDH_HQUERY*>(&m_pdhQuery));
    if (status == ERROR_SUCCESS) {
        m_pdhInitialized = true;
    }
#endif
    
    enumerateGpus();
    update();
}

GpuMonitor::~GpuMonitor()
{
#ifdef _WIN32
    if (m_pdhQuery) {
        PdhCloseQuery(reinterpret_cast<PDH_HQUERY>(m_pdhQuery));
    }
#endif
}

void GpuMonitor::enumerateGpus()
{
    m_gpus.clear();
    
#ifdef _WIN32
    IDXGIFactory4* pFactory = nullptr;
    HRESULT hr = CreateDXGIFactory1(IID_PPV_ARGS(&pFactory));
    
    if (FAILED(hr)) {
        // Try older factory
        IDXGIFactory1* pFactory1 = nullptr;
        hr = CreateDXGIFactory1(IID_PPV_ARGS(&pFactory1));
        if (SUCCEEDED(hr)) {
            pFactory = reinterpret_cast<IDXGIFactory4*>(pFactory1);
        }
    }
    
    if (!pFactory) return;
    
    IDXGIAdapter1* pAdapter = nullptr;
    int gpuIndex = 0;
    
    while (pFactory->EnumAdapters1(gpuIndex, &pAdapter) != DXGI_ERROR_NOT_FOUND) {
        DXGI_ADAPTER_DESC1 desc;
        pAdapter->GetDesc1(&desc);
        
        // Skip software adapters
        if (desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) {
            pAdapter->Release();
            gpuIndex++;
            continue;
        }
        
        GpuInfo gpu;
        gpu.index = gpuIndex;
        gpu.name = QString::fromWCharArray(desc.Description);
        gpu.vendorId = desc.VendorId;
        gpu.deviceId = desc.DeviceId;
        gpu.dedicatedMemoryTotal = desc.DedicatedVideoMemory;
        gpu.sharedMemoryTotal = desc.SharedSystemMemory;
        
        // Determine vendor
        switch (desc.VendorId) {
            case 0x10DE: gpu.vendor = "NVIDIA"; break;
            case 0x1002: gpu.vendor = "AMD"; break;
            case 0x8086: gpu.vendor = "Intel"; break;
            default: gpu.vendor = "Unknown"; break;
        }
        
        // Discrete GPU detection (dedicated memory > 512MB usually means discrete)
        gpu.isDiscrete = (desc.DedicatedVideoMemory > 512 * 1024 * 1024);
        
        // Try to get more info via DXGI 1.4
        IDXGIAdapter3* pAdapter3 = nullptr;
        if (SUCCEEDED(pAdapter->QueryInterface(IID_PPV_ARGS(&pAdapter3)))) {
            DXGI_QUERY_VIDEO_MEMORY_INFO memInfo;
            if (SUCCEEDED(pAdapter3->QueryVideoMemoryInfo(0, DXGI_MEMORY_SEGMENT_GROUP_LOCAL, &memInfo))) {
                gpu.dedicatedMemoryUsed = memInfo.CurrentUsage;
            }
            if (SUCCEEDED(pAdapter3->QueryVideoMemoryInfo(0, DXGI_MEMORY_SEGMENT_GROUP_NON_LOCAL, &memInfo))) {
                gpu.sharedMemoryUsed = memInfo.CurrentUsage;
            }
            pAdapter3->Release();
        }
        
        // Calculate memory usage percent
        if (gpu.dedicatedMemoryTotal > 0) {
            gpu.memoryUsagePercent = (gpu.dedicatedMemoryUsed * 100.0) / gpu.dedicatedMemoryTotal;
        }
        
        // Add PDH counter for this GPU
        if (m_pdhInitialized) {
            PDH_HCOUNTER counter;
            QString counterPath = QString("\\GPU Engine(pid_*_luid_0x%1_0x%2_phys_0_eng_0_engtype_3D)\\Utilization Percentage")
                .arg(desc.AdapterLuid.LowPart, 8, 16, QChar('0'))
                .arg(desc.AdapterLuid.HighPart, 8, 16, QChar('0'));
            
            // Try simpler counter path
            counterPath = QString("\\GPU Engine(*)\\Utilization Percentage");
            
            PDH_STATUS status = PdhAddEnglishCounterW(
                reinterpret_cast<PDH_HQUERY>(m_pdhQuery),
                reinterpret_cast<LPCWSTR>(counterPath.utf16()),
                0,
                &counter
            );
            
            if (status == ERROR_SUCCESS) {
                m_pdhCounters.push_back(counter);
            }
        }
        
        m_gpus.push_back(gpu);
        
        pAdapter->Release();
        gpuIndex++;
    }
    
    pFactory->Release();
    
    // Set primary GPU (first discrete, or first GPU)
    m_primaryGpuIndex = 0;
    for (size_t i = 0; i < m_gpus.size(); ++i) {
        if (m_gpus[i].isDiscrete) {
            m_primaryGpuIndex = static_cast<int>(i);
            break;
        }
    }
    
    // Initial PDH collection
    if (m_pdhInitialized) {
        PdhCollectQueryData(reinterpret_cast<PDH_HQUERY>(m_pdhQuery));
    }
#endif
}

void GpuMonitor::update()
{
    queryGpuMemory();
    queryGpuUsage();
    m_model->setGpus(m_gpus);
}

void GpuMonitor::queryGpuMemory()
{
#ifdef _WIN32
    IDXGIFactory4* pFactory = nullptr;
    HRESULT hr = CreateDXGIFactory1(IID_PPV_ARGS(&pFactory));
    if (FAILED(hr)) return;
    
    for (auto& gpu : m_gpus) {
        IDXGIAdapter1* pAdapter = nullptr;
        if (SUCCEEDED(pFactory->EnumAdapters1(gpu.index, &pAdapter))) {
            IDXGIAdapter3* pAdapter3 = nullptr;
            if (SUCCEEDED(pAdapter->QueryInterface(IID_PPV_ARGS(&pAdapter3)))) {
                DXGI_QUERY_VIDEO_MEMORY_INFO memInfo;
                if (SUCCEEDED(pAdapter3->QueryVideoMemoryInfo(0, DXGI_MEMORY_SEGMENT_GROUP_LOCAL, &memInfo))) {
                    gpu.dedicatedMemoryUsed = memInfo.CurrentUsage;
                }
                if (SUCCEEDED(pAdapter3->QueryVideoMemoryInfo(0, DXGI_MEMORY_SEGMENT_GROUP_NON_LOCAL, &memInfo))) {
                    gpu.sharedMemoryUsed = memInfo.CurrentUsage;
                }
                pAdapter3->Release();
            }
            pAdapter->Release();
            
            // Recalculate percent
            if (gpu.dedicatedMemoryTotal > 0) {
                gpu.memoryUsagePercent = (gpu.dedicatedMemoryUsed * 100.0) / gpu.dedicatedMemoryTotal;
            }
        }
    }
    
    pFactory->Release();
#endif
}

void GpuMonitor::queryGpuUsage()
{
#ifdef _WIN32
    if (!m_pdhInitialized || m_pdhCounters.empty()) {
        // Try alternative method using Performance Counters
        PDH_HQUERY tempQuery;
        if (PdhOpenQuery(nullptr, 0, &tempQuery) == ERROR_SUCCESS) {
            PDH_HCOUNTER counter;
            
            // Try to add GPU counter
            if (PdhAddEnglishCounterW(tempQuery, 
                L"\\GPU Engine(*)\\Utilization Percentage", 
                0, &counter) == ERROR_SUCCESS) 
            {
                PdhCollectQueryData(tempQuery);
                Sleep(100);
                PdhCollectQueryData(tempQuery);
                
                PDH_FMT_COUNTERVALUE value;
                if (PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE, nullptr, &value) == ERROR_SUCCESS) {
                    if (!m_gpus.empty()) {
                        m_gpus[0].usage = value.doubleValue;
                    }
                }
            }
            PdhCloseQuery(tempQuery);
        }
        
        // Alternative: Use WMI or estimate based on memory activity
        // For now, just check if memory changed significantly
        return;
    }
    
    // Collect PDH data
    PdhCollectQueryData(reinterpret_cast<PDH_HQUERY>(m_pdhQuery));
    
    // Get values for each counter
    double totalUsage = 0.0;
    int count = 0;
    
    for (auto& counterPtr : m_pdhCounters) {
        PDH_HCOUNTER counter = reinterpret_cast<PDH_HCOUNTER>(counterPtr);
        PDH_FMT_COUNTERVALUE value;
        
        if (PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE, nullptr, &value) == ERROR_SUCCESS) {
            totalUsage += value.doubleValue;
            count++;
        }
    }
    
    // Apply average usage to primary GPU
    if (count > 0 && !m_gpus.empty()) {
        m_gpus[m_primaryGpuIndex].usage = totalUsage / count;
    }
#endif
}

const GpuInfo& GpuMonitor::primaryGpu() const
{
    static GpuInfo emptyGpu;
    if (m_gpus.empty()) return emptyGpu;
    return m_gpus[m_primaryGpuIndex];
}

QString GpuMonitor::formatMemory(qint64 bytes)
{
    const char* units[] = {"B", "KB", "MB", "GB", "TB"};
    int unitIndex = 0;
    double size = bytes;
    
    while (size >= 1024.0 && unitIndex < 4) {
        size /= 1024.0;
        unitIndex++;
    }
    
    return QString("%1 %2").arg(size, 0, 'f', unitIndex > 1 ? 1 : 0).arg(units[unitIndex]);
}
