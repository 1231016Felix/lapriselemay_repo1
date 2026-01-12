#pragma once

#include <string>
#include <vector>
#include <cstdint>
#include <functional>
#include <windows.h>

namespace DriverManager {

    // Bug check codes communs
    struct BugCheckInfo {
        uint32_t code;
        std::wstring name;
        std::wstring description;
    };

    // Information sur un crash BSOD
    struct BSODCrashInfo {
        std::wstring dumpFilePath;
        std::wstring dumpFileName;
        SYSTEMTIME crashTime;
        uint32_t bugCheckCode;
        std::wstring bugCheckName;
        std::wstring bugCheckDescription;
        uint64_t bugCheckParams[4];
        std::wstring faultingModule;         // Pilote/module responsable
        std::wstring faultingModulePath;
        std::wstring faultingModuleVersion;
        uint64_t faultingAddress;
        std::wstring osVersion;
        uint32_t processorCount;
        uint64_t dumpFileSize;
        bool isAnalyzed = false;
        std::wstring analysisError;
    };

    // Statistiques sur les pilotes problématiques
    struct ProblematicDriverStats {
        std::wstring driverName;
        std::wstring driverPath;
        std::wstring currentVersion;
        int crashCount = 0;
        std::vector<uint32_t> bugCheckCodes;
        SYSTEMTIME lastCrash;
        SYSTEMTIME firstCrash;
    };

    class BSODAnalyzer {
    public:
        BSODAnalyzer();
        ~BSODAnalyzer();

        // Scanner les minidumps
        bool ScanMinidumps();

        // Obtenir les crashes analysés
        const std::vector<BSODCrashInfo>& GetCrashes() const { return m_crashes; }

        // Obtenir les statistiques par pilote
        std::vector<ProblematicDriverStats> GetProblematicDrivers() const;

        // Obtenir le nombre de dumps trouvés
        size_t GetDumpCount() const { return m_crashes.size(); }

        // Vérifier si le dossier Minidump existe
        bool MinidumpFolderExists() const;

        // Obtenir le chemin du dossier Minidump
        std::wstring GetMinidumpPath() const { return m_minidumpPath; }

        // Dernière erreur
        std::wstring GetLastError() const { return m_lastError; }

        // Callback de progression
        using ProgressCallback = std::function<void(int current, int total, const std::wstring& item)>;
        void SetProgressCallback(ProgressCallback callback) { m_progressCallback = callback; }

        // Est en cours de scan
        bool IsScanning() const { return m_isScanning; }

        // Obtenir le nom du bug check
        static std::wstring GetBugCheckName(uint32_t code);
        static std::wstring GetBugCheckDescription(uint32_t code);

    private:
        // Analyser un fichier minidump
        bool AnalyzeMinidump(const std::wstring& filePath, BSODCrashInfo& info);

        // Parser le header du minidump
        bool ParseMinidumpHeader(HANDLE hFile, BSODCrashInfo& info);

        // Extraire le module fautif des streams
        bool ExtractFaultingModule(HANDLE hFile, BSODCrashInfo& info);

        // Obtenir la version d'un fichier
        std::wstring GetFileVersion(const std::wstring& filePath);

        std::vector<BSODCrashInfo> m_crashes;
        std::wstring m_minidumpPath;
        std::wstring m_lastError;
        bool m_isScanning = false;
        ProgressCallback m_progressCallback;
    };

} // namespace DriverManager
