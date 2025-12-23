#include "temperaturemonitor.h"

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <Windows.h>
#include <comdef.h>
#include <Wbemidl.h>
#pragma comment(lib, "wbemuuid.lib")
#endif

#include <QDebug>
#include <QRegularExpression>
#include <QColor>

// ============================================================================
// ThermalZoneTableModel Implementation
// ============================================================================

ThermalZoneTableModel::ThermalZoneTableModel(QObject *parent)
    : QAbstractTableModel(parent)
{
}

void ThermalZoneTableModel::setZones(const std::vector<ThermalZoneInfo>& zones)
{
    beginResetModel();
    m_zones = zones;
    endResetModel();
}

int ThermalZoneTableModel::rowCount(const QModelIndex &parent) const
{
    if (parent.isValid()) return 0;
    return static_cast<int>(m_zones.size());
}

int ThermalZoneTableModel::columnCount(const QModelIndex &parent) const
{
    if (parent.isValid()) return 0;
    return 3; // Name, Friendly Name, Temperature
}

QVariant ThermalZoneTableModel::data(const QModelIndex &index, int role) const
{
    if (!index.isValid() || index.row() >= static_cast<int>(m_zones.size()))
        return {};

    const auto& zone = m_zones[index.row()];

    if (role == Qt::DisplayRole) {
        switch (index.column()) {
            case 0: return zone.friendlyName;
            case 1: return zone.name;
            case 2: 
                if (zone.isValid)
                    return QString("%1 °C").arg(zone.temperatureC, 0, 'f', 1);
                return "N/A";
        }
    }
    else if (role == Qt::TextAlignmentRole) {
        if (index.column() == 2)
            return static_cast<int>(Qt::AlignRight | Qt::AlignVCenter);
    }
    else if (role == Qt::ForegroundRole && index.column() == 2 && zone.isValid) {
        // Color based on temperature
        if (zone.temperatureC >= 80)
            return QColor(255, 0, 0);      // Red - Hot
        else if (zone.temperatureC >= 60)
            return QColor(255, 165, 0);    // Orange - Warm
        else
            return QColor(0, 170, 0);      // Green - Normal
    }

    return {};
}

QVariant ThermalZoneTableModel::headerData(int section, Qt::Orientation orientation, int role) const
{
    if (orientation == Qt::Horizontal && role == Qt::DisplayRole) {
        switch (section) {
            case 0: return tr("Zone");
            case 1: return tr("System Name");
            case 2: return tr("Temperature");
        }
    }
    return {};
}

// ============================================================================
// TemperatureMonitor Implementation
// ============================================================================

TemperatureMonitor::TemperatureMonitor(QObject *parent)
    : QObject(parent)
    , m_model(std::make_unique<ThermalZoneTableModel>(this))
{
    initializeWmi();
    
    // Do initial query to check availability
    if (m_wmiInitialized) {
        update();
        m_isAvailable = !m_zones.empty() && m_info.hasTemperature;
    }
}

TemperatureMonitor::~TemperatureMonitor()
{
#ifdef _WIN32
    if (m_wbemServices) {
        reinterpret_cast<IWbemServices*>(m_wbemServices)->Release();
    }
    if (m_wbemLocator) {
        reinterpret_cast<IWbemLocator*>(m_wbemLocator)->Release();
    }
#endif
}

void TemperatureMonitor::initializeWmi()
{
#ifdef _WIN32
    HRESULT hr;
    
    // Initialize COM (might already be initialized)
    hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (FAILED(hr) && hr != RPC_E_CHANGED_MODE) {
        qWarning() << "TemperatureMonitor: COM initialization failed";
        return;
    }
    
    // Create WMI locator
    IWbemLocator* locator = nullptr;
    hr = CoCreateInstance(
        CLSID_WbemLocator, nullptr, CLSCTX_INPROC_SERVER,
        IID_IWbemLocator, reinterpret_cast<void**>(&locator)
    );
    
    if (FAILED(hr)) {
        qWarning() << "TemperatureMonitor: Failed to create WMI locator";
        return;
    }
    m_wbemLocator = locator;
    
    // Connect to WMI namespace
    IWbemServices* services = nullptr;
    hr = locator->ConnectServer(
        _bstr_t(L"ROOT\\CIMV2"),
        nullptr, nullptr, nullptr, 0, nullptr, nullptr,
        &services
    );
    
    if (FAILED(hr)) {
        qWarning() << "TemperatureMonitor: Failed to connect to WMI";
        return;
    }
    m_wbemServices = services;

    // Set security levels
    hr = CoSetProxyBlanket(
        services,
        RPC_C_AUTHN_WINNT,
        RPC_C_AUTHZ_NONE,
        nullptr,
        RPC_C_AUTHN_LEVEL_CALL,
        RPC_C_IMP_LEVEL_IMPERSONATE,
        nullptr,
        EOAC_NONE
    );
    
    if (FAILED(hr)) {
        qWarning() << "TemperatureMonitor: Failed to set proxy blanket";
        return;
    }
    
    m_wmiInitialized = true;
    qDebug() << "TemperatureMonitor: WMI initialized successfully";
#endif
}

void TemperatureMonitor::update()
{
    queryTemperatures();
    calculateAggregates();
    m_model->setZones(m_zones);
}

void TemperatureMonitor::queryTemperatures()
{
#ifdef _WIN32
    if (!m_wmiInitialized || !m_wbemServices)
        return;
    
    m_zones.clear();
    
    IWbemServices* services = reinterpret_cast<IWbemServices*>(m_wbemServices);
    IEnumWbemClassObject* enumerator = nullptr;
    
    // Query thermal zone information (doesn't require admin)
    HRESULT hr = services->ExecQuery(
        _bstr_t(L"WQL"),
        _bstr_t(L"SELECT Name, Temperature, HighPrecisionTemperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation"),
        WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY,
        nullptr,
        &enumerator
    );
    
    if (FAILED(hr)) {
        qWarning() << "TemperatureMonitor: WMI query failed";
        return;
    }
    
    IWbemClassObject* obj = nullptr;
    ULONG returned = 0;
    
    while (enumerator) {
        hr = enumerator->Next(WBEM_INFINITE, 1, &obj, &returned);
        if (returned == 0) break;

        ThermalZoneInfo zone;
        VARIANT vtProp;
        
        // Get zone name
        VariantInit(&vtProp);
        hr = obj->Get(L"Name", 0, &vtProp, nullptr, nullptr);
        if (SUCCEEDED(hr) && vtProp.vt == VT_BSTR) {
            zone.name = QString::fromWCharArray(vtProp.bstrVal);
        }
        VariantClear(&vtProp);
        
        // Get high precision temperature (in tenths of Kelvin)
        VariantInit(&vtProp);
        hr = obj->Get(L"HighPrecisionTemperature", 0, &vtProp, nullptr, nullptr);
        if (SUCCEEDED(hr) && (vtProp.vt == VT_I4 || vtProp.vt == VT_UI4)) {
            double kelvinTenths = static_cast<double>(vtProp.intVal);
            if (kelvinTenths > 0) {
                zone.temperatureK = kelvinTenths / 10.0;
                zone.temperatureC = zone.temperatureK - 273.15;
                zone.isValid = (zone.temperatureC > -40 && zone.temperatureC < 150);
            }
        }
        VariantClear(&vtProp);
        
        // Set friendly name
        zone.friendlyName = mapZoneToFriendlyName(zone.name);
        
        // Only add valid zones
        if (zone.isValid) {
            m_zones.push_back(zone);
        }
        
        obj->Release();
    }
    
    if (enumerator) {
        enumerator->Release();
    }
#endif
}

QString TemperatureMonitor::mapZoneToFriendlyName(const QString& zoneName)
{
    // Common thermal zone naming patterns
    // Surface devices typically use \_SB.TZxx format
    
    QString upper = zoneName.toUpper();
    
    // CPU/Processor zones (usually the hottest)
    if (upper.contains("CPU") || upper.contains("TCPU") || 
        upper.contains("TZ00") || upper.contains("TZ09") ||
        upper.contains("THRM") || upper.contains("CPUZ")) {
        return tr("CPU");
    }
    
    // GPU zones
    if (upper.contains("GPU") || upper.contains("GFX") ||
        upper.contains("TGPU")) {
        return tr("GPU");
    }
    
    // Memory/DIMM zones
    if (upper.contains("DIMM") || upper.contains("MEM") ||
        upper.contains("RAM")) {
        return tr("Memory");
    }
    
    // Chassis/Skin zones
    if (upper.contains("SKIN") || upper.contains("CHAS") ||
        upper.contains("AMB") || upper.contains("TZ05")) {
        return tr("Chassis");
    }
    
    // Battery zones
    if (upper.contains("BAT") || upper.contains("TBAT")) {
        return tr("Battery");
    }
    
    // SSD/Storage zones
    if (upper.contains("SSD") || upper.contains("NVME") ||
        upper.contains("STOR")) {
        return tr("Storage");
    }
    
    // Power/VRM zones
    if (upper.contains("VRM") || upper.contains("PWR") ||
        upper.contains("POWER")) {
        return tr("VRM");
    }
    
    // Throttle policy zone (usually not a real temp)
    if (upper.contains("TPOL") || upper.contains("POL")) {
        return tr("Throttle Policy");
    }
    
    // Default: use a generic name with zone number if available
    QRegularExpression re("TZ(\\d+)");
    auto match = re.match(upper);
    if (match.hasMatch()) {
        return tr("Zone %1").arg(match.captured(1));
    }
    
    return tr("Thermal Zone");
}

void TemperatureMonitor::calculateAggregates()
{
    m_info = TemperatureInfo{};
    
    if (m_zones.empty()) {
        return;
    }
    
    double sum = 0.0;
    double maxTemp = -999.0;
    double secondMax = -999.0;
    int validCount = 0;
    
    for (const auto& zone : m_zones) {
        if (!zone.isValid) continue;
        
        validCount++;
        sum += zone.temperatureC;
        
        if (zone.temperatureC > maxTemp) {
            secondMax = maxTemp;
            maxTemp = zone.temperatureC;
        } else if (zone.temperatureC > secondMax) {
            secondMax = zone.temperatureC;
        }
    }
    
    if (validCount > 0) {
        m_info.hasTemperature = true;
        m_info.validZoneCount = validCount;
        m_info.maxTemperature = maxTemp;
        m_info.avgTemperature = sum / validCount;
        
        // CPU is typically the hottest zone
        m_info.cpuTemperature = maxTemp;
        
        // Chassis is typically the second hottest (or lower)
        if (secondMax > -999.0) {
            m_info.chassisTemperature = secondMax;
        }
    }
}

QString TemperatureMonitor::formatTemperature(double celsius)
{
    if (celsius < -900) {
        return "N/A";
    }
    return QString("%1 °C").arg(celsius, 0, 'f', 1);
}
