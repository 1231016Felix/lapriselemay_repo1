#include "BSODAnalyzer.h"
#include <filesystem>
#include <algorithm>
#include <map>
#include <dbghelp.h>

#pragma comment(lib, "dbghelp.lib")
#pragma comment(lib, "version.lib")

namespace DriverManager {

    // Structure du header minidump (MINIDUMP_HEADER)
    #pragma pack(push, 1)
    struct MINIDUMP_HEADER_CUSTOM {
        uint32_t Signature;           // "MDMP"
        uint32_t Version;
        uint32_t NumberOfStreams;
        uint32_t StreamDirectoryRva;
        uint32_t CheckSum;
        uint32_t TimeDateStamp;
        uint64_t Flags;
    };

    struct MINIDUMP_DIRECTORY_CUSTOM {
        uint32_t StreamType;
        uint32_t DataSize;
        uint32_t Rva;
    };

    struct MINIDUMP_EXCEPTION_STREAM_CUSTOM {
        uint32_t ThreadId;
        uint32_t __alignment;
        uint32_t ExceptionCode;
        uint32_t ExceptionFlags;
        uint64_t ExceptionRecord;
        uint64_t ExceptionAddress;
        uint32_t NumberParameters;
        uint32_t __unusedAlignment;
        uint64_t ExceptionInformation[15];
    };
    #pragma pack(pop)

    // Table des bug check codes
    static const std::map<uint32_t, std::pair<std::wstring, std::wstring>> g_bugCheckTable = {
        {0x0000001E, {L"KMODE_EXCEPTION_NOT_HANDLED", L"Exception non gérée en mode kernel"}},
        {0x00000024, {L"NTFS_FILE_SYSTEM", L"Problème avec le système de fichiers NTFS"}},
        {0x0000003B, {L"SYSTEM_SERVICE_EXCEPTION", L"Exception dans un service système"}},
        {0x0000007E, {L"SYSTEM_THREAD_EXCEPTION_NOT_HANDLED", L"Exception thread système non gérée"}},
        {0x0000007F, {L"UNEXPECTED_KERNEL_MODE_TRAP", L"Piège inattendu en mode kernel"}},
        {0x0000009F, {L"DRIVER_POWER_STATE_FAILURE", L"Échec d'état d'alimentation du pilote"}},
        {0x000000BE, {L"ATTEMPTED_WRITE_TO_READONLY_MEMORY", L"Tentative d'écriture en mémoire lecture seule"}},
        {0x000000C2, {L"BAD_POOL_CALLER", L"Appelant de pool incorrect"}},
        {0x000000D1, {L"DRIVER_IRQL_NOT_LESS_OR_EQUAL", L"IRQL pilote incorrect - pilote défaillant"}},
        {0x000000D8, {L"DRIVER_USED_EXCESSIVE_PTES", L"Pilote utilisant trop de PTEs"}},
        {0x000000EA, {L"THREAD_STUCK_IN_DEVICE_DRIVER", L"Thread bloqué dans un pilote de périphérique"}},
        {0x000000F4, {L"CRITICAL_OBJECT_TERMINATION", L"Terminaison d'objet critique"}},
        {0x000000FC, {L"ATTEMPTED_EXECUTE_OF_NOEXECUTE_MEMORY", L"Exécution de mémoire non exécutable"}},
        {0x000000FE, {L"BUGCODE_USB_DRIVER", L"Erreur pilote USB"}},
        {0x00000116, {L"VIDEO_TDR_FAILURE", L"Échec TDR vidéo - pilote graphique"}},
        {0x00000117, {L"VIDEO_TDR_TIMEOUT_DETECTED", L"Timeout TDR vidéo détecté"}},
        {0x00000119, {L"VIDEO_SCHEDULER_INTERNAL_ERROR", L"Erreur interne planificateur vidéo"}},
        {0x0000011D, {L"EVENT_TRACING_FATAL_ERROR", L"Erreur fatale traçage d'événements"}},
        {0x00000124, {L"WHEA_UNCORRECTABLE_ERROR", L"Erreur matérielle non corrigeable"}},
        {0x0000012B, {L"FAULTY_HARDWARE_CORRUPTED_PAGE", L"Page corrompue par matériel défaillant"}},
        {0x00000133, {L"DPC_WATCHDOG_VIOLATION", L"Violation watchdog DPC"}},
        {0x00000139, {L"KERNEL_SECURITY_CHECK_FAILURE", L"Échec vérification sécurité kernel"}},
        {0x0000013A, {L"KERNEL_MODE_HEAP_CORRUPTION", L"Corruption du tas en mode kernel"}},
        {0x0000015F, {L"CONNECTED_STANDBY_WATCHDOG_TIMEOUT", L"Timeout watchdog veille connectée"}},
        {0x00000154, {L"UNEXPECTED_STORE_EXCEPTION", L"Exception store inattendue"}},
        {0x0000019, {L"BAD_POOL_HEADER", L"En-tête de pool incorrect"}},
        {0x0000001A, {L"MEMORY_MANAGEMENT", L"Erreur de gestion mémoire"}},
        {0x00000050, {L"PAGE_FAULT_IN_NONPAGED_AREA", L"Défaut de page en zone non paginée"}},
        {0x0000007A, {L"KERNEL_DATA_INPAGE_ERROR", L"Erreur données kernel en page"}},
        {0x000000C4, {L"DRIVER_VERIFIER_DETECTED_VIOLATION", L"Violation détectée par Driver Verifier"}},
        {0x000000EF, {L"CRITICAL_PROCESS_DIED", L"Processus critique terminé"}},
        {0x00000113, {L"VIDEO_DXGKRNL_FATAL_ERROR", L"Erreur fatale DXGKRNL"}},
        {0x0000014F, {L"PDC_WATCHDOG_TIMEOUT", L"Timeout watchdog PDC"}},
        {0x000001CA, {L"SYNTHETIC_WATCHDOG_TIMEOUT", L"Timeout watchdog synthétique"}},
    };

    BSODAnalyzer::BSODAnalyzer() {
        m_minidumpPath = L"C:\\Windows\\Minidump";
    }

    BSODAnalyzer::~BSODAnalyzer() {
    }

    std::wstring BSODAnalyzer::GetBugCheckName(uint32_t code) {
        auto it = g_bugCheckTable.find(code);
        if (it != g_bugCheckTable.end()) {
            return it->second.first;
        }
        wchar_t buf[32];
        swprintf_s(buf, L"BUGCHECK_0x%08X", code);
        return buf;
    }

    std::wstring BSODAnalyzer::GetBugCheckDescription(uint32_t code) {
        auto it = g_bugCheckTable.find(code);
        if (it != g_bugCheckTable.end()) {
            return it->second.second;
        }
        return L"Code d'erreur inconnu";
    }

    bool BSODAnalyzer::MinidumpFolderExists() const {
        return std::filesystem::exists(m_minidumpPath) && 
               std::filesystem::is_directory(m_minidumpPath);
    }

    std::wstring BSODAnalyzer::GetFileVersion(const std::wstring& filePath) {
        DWORD dummy;
        DWORD size = GetFileVersionInfoSizeW(filePath.c_str(), &dummy);
        if (size == 0) return L"";

        std::vector<BYTE> data(size);
        if (!GetFileVersionInfoW(filePath.c_str(), 0, size, data.data())) return L"";

        VS_FIXEDFILEINFO* fileInfo = nullptr;
        UINT len = 0;
        if (!VerQueryValueW(data.data(), L"\\", (LPVOID*)&fileInfo, &len)) return L"";

        wchar_t version[64];
        swprintf_s(version, L"%d.%d.%d.%d",
            HIWORD(fileInfo->dwFileVersionMS),
            LOWORD(fileInfo->dwFileVersionMS),
            HIWORD(fileInfo->dwFileVersionLS),
            LOWORD(fileInfo->dwFileVersionLS));
        return version;
    }

    bool BSODAnalyzer::ScanMinidumps() {
        if (m_isScanning) return false;
        m_isScanning = true;
        m_crashes.clear();
        m_lastError.clear();

        if (!MinidumpFolderExists()) {
            m_lastError = L"Le dossier Minidump n'existe pas ou est inaccessible.";
            m_isScanning = false;
            return false;
        }

        // Compter les fichiers
        std::vector<std::filesystem::path> dumpFiles;
        try {
            for (const auto& entry : std::filesystem::directory_iterator(m_minidumpPath)) {
                if (entry.is_regular_file()) {
                    auto ext = entry.path().extension().wstring();
                    std::transform(ext.begin(), ext.end(), ext.begin(), ::towlower);
                    if (ext == L".dmp") {
                        dumpFiles.push_back(entry.path());
                    }
                }
            }
        } catch (const std::exception& e) {
            std::string errorMsg = e.what();
            m_lastError = L"Erreur d'accès au dossier Minidump: " + 
                          std::wstring(errorMsg.begin(), errorMsg.end());
            m_isScanning = false;
            return false;
        }

        if (dumpFiles.empty()) {
            m_lastError = L"Aucun fichier minidump trouvé. Bonne nouvelle!";
            m_isScanning = false;
            return true;
        }

        // Trier par date (plus récent en premier)
        std::sort(dumpFiles.begin(), dumpFiles.end(), [](const auto& a, const auto& b) {
            return std::filesystem::last_write_time(a) > std::filesystem::last_write_time(b);
        });

        int current = 0;
        int total = static_cast<int>(dumpFiles.size());

        for (const auto& dumpPath : dumpFiles) {
            current++;
            if (m_progressCallback) {
                m_progressCallback(current, total, dumpPath.filename().wstring());
            }

            BSODCrashInfo info;
            info.dumpFilePath = dumpPath.wstring();
            info.dumpFileName = dumpPath.filename().wstring();
            
            try {
                info.dumpFileSize = std::filesystem::file_size(dumpPath);
            } catch (...) {
                info.dumpFileSize = 0;
            }

            if (AnalyzeMinidump(dumpPath.wstring(), info)) {
                info.isAnalyzed = true;
            }

            m_crashes.push_back(info);
        }

        m_isScanning = false;
        return true;
    }

    bool BSODAnalyzer::AnalyzeMinidump(const std::wstring& filePath, BSODCrashInfo& info) {
        HANDLE hFile = CreateFileW(filePath.c_str(), GENERIC_READ, FILE_SHARE_READ,
            nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        
        if (hFile == INVALID_HANDLE_VALUE) {
            info.analysisError = L"Impossible d'ouvrir le fichier";
            return false;
        }

        // Obtenir la date du fichier
        FILETIME ftCreate, ftAccess, ftWrite;
        if (GetFileTime(hFile, &ftCreate, &ftAccess, &ftWrite)) {
            FileTimeToSystemTime(&ftWrite, &info.crashTime);
        }

        bool result = ParseMinidumpHeader(hFile, info);
        
        if (result) {
            ExtractFaultingModule(hFile, info);
        }

        CloseHandle(hFile);
        return result;
    }

    bool BSODAnalyzer::ParseMinidumpHeader(HANDLE hFile, BSODCrashInfo& info) {
        // Lire le header
        MINIDUMP_HEADER_CUSTOM header;
        DWORD bytesRead;
        
        SetFilePointer(hFile, 0, nullptr, FILE_BEGIN);
        if (!ReadFile(hFile, &header, sizeof(header), &bytesRead, nullptr) || 
            bytesRead != sizeof(header)) {
            info.analysisError = L"Impossible de lire le header";
            return false;
        }

        // Vérifier la signature "MDMP"
        if (header.Signature != 0x504D444D) { // "MDMP" en little-endian
            info.analysisError = L"Signature invalide";
            return false;
        }

        // Parcourir les streams pour trouver les informations
        for (uint32_t i = 0; i < header.NumberOfStreams; i++) {
            MINIDUMP_DIRECTORY_CUSTOM dir;
            DWORD dirOffset = header.StreamDirectoryRva + (i * sizeof(MINIDUMP_DIRECTORY_CUSTOM));
            
            SetFilePointer(hFile, dirOffset, nullptr, FILE_BEGIN);
            if (!ReadFile(hFile, &dir, sizeof(dir), &bytesRead, nullptr) || 
                bytesRead != sizeof(dir)) {
                continue;
            }

            // Stream type 6 = Exception stream
            if (dir.StreamType == 6 && dir.DataSize >= sizeof(MINIDUMP_EXCEPTION_STREAM_CUSTOM)) {
                MINIDUMP_EXCEPTION_STREAM_CUSTOM excStream;
                SetFilePointer(hFile, dir.Rva, nullptr, FILE_BEGIN);
                if (ReadFile(hFile, &excStream, sizeof(excStream), &bytesRead, nullptr)) {
                    info.bugCheckCode = excStream.ExceptionCode;
                    info.faultingAddress = excStream.ExceptionAddress;
                    
                    // Copier les paramètres
                    for (int j = 0; j < 4 && j < 15; j++) {
                        info.bugCheckParams[j] = excStream.ExceptionInformation[j];
                    }
                }
            }
            
            // Stream type 4 = System info
            if (dir.StreamType == 4 && dir.DataSize >= 48) {
                SetFilePointer(hFile, dir.Rva, nullptr, FILE_BEGIN);
                BYTE sysInfo[64];
                if (ReadFile(hFile, sysInfo, min(dir.DataSize, 64UL), &bytesRead, nullptr)) {
                    info.processorCount = *reinterpret_cast<uint8_t*>(sysInfo + 20);
                    uint32_t majorVer = *reinterpret_cast<uint32_t*>(sysInfo + 4);
                    uint32_t minorVer = *reinterpret_cast<uint32_t*>(sysInfo + 8);
                    uint32_t buildNum = *reinterpret_cast<uint32_t*>(sysInfo + 12);
                    
                    wchar_t ver[64];
                    swprintf_s(ver, L"Windows %u.%u.%u", majorVer, minorVer, buildNum);
                    info.osVersion = ver;
                }
            }
        }

        info.bugCheckName = GetBugCheckName(info.bugCheckCode);
        info.bugCheckDescription = GetBugCheckDescription(info.bugCheckCode);
        
        return true;
    }

    bool BSODAnalyzer::ExtractFaultingModule(HANDLE hFile, BSODCrashInfo& info) {
        // Lire le header pour obtenir le nombre de streams
        MINIDUMP_HEADER_CUSTOM header;
        DWORD bytesRead;
        
        SetFilePointer(hFile, 0, nullptr, FILE_BEGIN);
        if (!ReadFile(hFile, &header, sizeof(header), &bytesRead, nullptr)) {
            return false;
        }

        // Chercher le stream de modules (type 4 = ModuleListStream)
        for (uint32_t i = 0; i < header.NumberOfStreams; i++) {
            MINIDUMP_DIRECTORY_CUSTOM dir;
            DWORD dirOffset = header.StreamDirectoryRva + (i * sizeof(MINIDUMP_DIRECTORY_CUSTOM));
            
            SetFilePointer(hFile, dirOffset, nullptr, FILE_BEGIN);
            if (!ReadFile(hFile, &dir, sizeof(dir), &bytesRead, nullptr)) {
                continue;
            }

            // ModuleListStream = 4
            if (dir.StreamType == 4) {
                SetFilePointer(hFile, dir.Rva, nullptr, FILE_BEGIN);
                
                uint32_t numberOfModules;
                if (!ReadFile(hFile, &numberOfModules, sizeof(numberOfModules), &bytesRead, nullptr)) {
                    continue;
                }

                // Structure MINIDUMP_MODULE simplifiée
                struct ModuleEntry {
                    uint64_t BaseOfImage;
                    uint32_t SizeOfImage;
                    uint32_t CheckSum;
                    uint32_t TimeDateStamp;
                    uint32_t ModuleNameRva;
                    // VS_FIXEDFILEINFO suit mais on saute
                };

                uint64_t faultAddr = info.faultingAddress;
                
                for (uint32_t m = 0; m < numberOfModules && m < 500; m++) {
                    ModuleEntry mod;
                    if (!ReadFile(hFile, &mod, sizeof(mod), &bytesRead, nullptr)) {
                        break;
                    }
                    
                    // Sauter le reste de la structure (VS_FIXEDFILEINFO + padding)
                    SetFilePointer(hFile, 108 - sizeof(mod), nullptr, FILE_CURRENT);
                    
                    // Vérifier si l'adresse fautive est dans ce module
                    if (faultAddr >= mod.BaseOfImage && 
                        faultAddr < mod.BaseOfImage + mod.SizeOfImage) {
                        
                        // Lire le nom du module
                        DWORD savedPos = SetFilePointer(hFile, 0, nullptr, FILE_CURRENT);
                        SetFilePointer(hFile, mod.ModuleNameRva, nullptr, FILE_BEGIN);
                        
                        uint32_t nameLen;
                        if (ReadFile(hFile, &nameLen, sizeof(nameLen), &bytesRead, nullptr)) {
                            if (nameLen > 0 && nameLen < 1024) {
                                std::vector<wchar_t> nameBuf(nameLen / 2 + 1);
                                if (ReadFile(hFile, nameBuf.data(), nameLen, &bytesRead, nullptr)) {
                                    nameBuf[nameLen / 2] = L'\0';
                                    info.faultingModulePath = nameBuf.data();
                                    
                                    // Extraire juste le nom du fichier
                                    size_t lastSlash = info.faultingModulePath.find_last_of(L"\\/");
                                    if (lastSlash != std::wstring::npos) {
                                        info.faultingModule = info.faultingModulePath.substr(lastSlash + 1);
                                    } else {
                                        info.faultingModule = info.faultingModulePath;
                                    }
                                    
                                    // Essayer d'obtenir la version si le fichier existe
                                    if (std::filesystem::exists(info.faultingModulePath)) {
                                        info.faultingModuleVersion = GetFileVersion(info.faultingModulePath);
                                    }
                                    
                                    return true;
                                }
                            }
                        }
                        SetFilePointer(hFile, savedPos, nullptr, FILE_BEGIN);
                    }
                }
            }
        }

        // Si on n'a pas trouvé via l'adresse, essayer d'extraire depuis les paramètres bugcheck
        // Certains codes ont le nom du pilote dans les paramètres
        if (info.bugCheckCode == 0x000000D1 || // DRIVER_IRQL_NOT_LESS_OR_EQUAL
            info.bugCheckCode == 0x0000009F || // DRIVER_POWER_STATE_FAILURE
            info.bugCheckCode == 0x000000FE) { // BUGCODE_USB_DRIVER
            // Ces codes ont souvent l'adresse du pilote fautif dans param 4
            // On pourrait faire une recherche plus approfondie ici
        }

        return false;
    }

    std::vector<ProblematicDriverStats> BSODAnalyzer::GetProblematicDrivers() const {
        std::map<std::wstring, ProblematicDriverStats> driverMap;

        for (const auto& crash : m_crashes) {
            if (!crash.faultingModule.empty()) {
                std::wstring key = crash.faultingModule;
                std::transform(key.begin(), key.end(), key.begin(), ::towlower);
                
                auto& stats = driverMap[key];
                if (stats.driverName.empty()) {
                    stats.driverName = crash.faultingModule;
                    stats.driverPath = crash.faultingModulePath;
                    stats.currentVersion = crash.faultingModuleVersion;
                    stats.firstCrash = crash.crashTime;
                    stats.lastCrash = crash.crashTime;
                }
                
                stats.crashCount++;
                stats.bugCheckCodes.push_back(crash.bugCheckCode);
                
                // Mettre à jour les dates
                FILETIME ft1, ft2;
                SystemTimeToFileTime(&crash.crashTime, &ft1);
                SystemTimeToFileTime(&stats.lastCrash, &ft2);
                if (CompareFileTime(&ft1, &ft2) > 0) {
                    stats.lastCrash = crash.crashTime;
                }
                SystemTimeToFileTime(&stats.firstCrash, &ft2);
                if (CompareFileTime(&ft1, &ft2) < 0) {
                    stats.firstCrash = crash.crashTime;
                }
            }
        }

        // Convertir en vecteur et trier par nombre de crashes
        std::vector<ProblematicDriverStats> result;
        for (auto& [key, stats] : driverMap) {
            result.push_back(std::move(stats));
        }
        
        std::sort(result.begin(), result.end(), [](const auto& a, const auto& b) {
            return a.crashCount > b.crashCount;
        });

        return result;
    }

} // namespace DriverManager
