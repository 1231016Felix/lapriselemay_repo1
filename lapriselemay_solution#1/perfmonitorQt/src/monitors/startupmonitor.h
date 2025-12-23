#pragma once

#include <QObject>
#include <QAbstractTableModel>
#include <QString>
#include <QIcon>
#include <QDateTime>
#include <vector>
#include <memory>

/**
 * @brief Source location of startup entry
 */
enum class StartupSource {
    RegistryCurrentUser,        // HKCU\Software\Microsoft\Windows\CurrentVersion\Run
    RegistryLocalMachine,       // HKLM\Software\Microsoft\Windows\CurrentVersion\Run
    RegistryCurrentUserOnce,    // HKCU\...\RunOnce
    RegistryLocalMachineOnce,   // HKLM\...\RunOnce
    StartupFolderUser,          // Shell:startup (user)
    StartupFolderCommon,        // Shell:common startup (all users)
    TaskScheduler,              // Task Scheduler
    Services,                   // Windows Services (Auto-start)
    Unknown
};

/**
 * @brief Impact level on startup time
 */
enum class StartupImpact {
    None,       // Disabled
    Low,        // < 300ms
    Medium,     // 300ms - 1000ms
    High,       // > 1000ms
    NotMeasured
};

/**
 * @brief Information about a startup entry
 */
struct StartupEntry {
    QString name;               // Display name
    QString publisher;          // Company/Publisher
    QString command;            // Full command line
    QString executablePath;     // Path to executable
    QString arguments;          // Command line arguments
    QString description;        // File description
    QString version;            // File version
    QIcon icon;                 // Application icon
    
    StartupSource source;       // Where it's registered
    QString sourceLocation;     // Exact registry key or folder path
    
    bool isEnabled{true};       // Currently enabled
    bool isValid{true};         // Executable exists
    bool isElevated{false};     // Requires admin
    bool isMicrosoft{false};    // Microsoft signed
    
    StartupImpact impact{StartupImpact::NotMeasured};
    int impactMs{0};            // Measured impact in milliseconds
    
    QDateTime lastDisabled;     // When it was last disabled
    QDateTime dateAdded;        // When it was added to startup
    
    // For services
    QString serviceName;        // Internal service name
    QString serviceStartType;   // Auto, Manual, Disabled
};

/**
 * @brief Table model for startup entries
 */
class StartupTableModel : public QAbstractTableModel
{
    Q_OBJECT

public:
    enum Column {
        ColEnabled = 0,
        ColName,
        ColPublisher,
        ColStatus,
        ColImpact,
        ColSource,
        ColCommand,
        ColCount
    };

    explicit StartupTableModel(QObject *parent = nullptr);
    
    void setEntries(const std::vector<StartupEntry>& entries);
    StartupEntry* getEntry(int row);
    const StartupEntry* getEntry(int row) const;
    
    int rowCount(const QModelIndex &parent = QModelIndex()) const override;
    int columnCount(const QModelIndex &parent = QModelIndex()) const override;
    QVariant data(const QModelIndex &index, int role = Qt::DisplayRole) const override;
    QVariant headerData(int section, Qt::Orientation orientation, int role) const override;
    Qt::ItemFlags flags(const QModelIndex &index) const override;
    bool setData(const QModelIndex &index, const QVariant &value, int role) override;

signals:
    void entryToggled(int row, bool enabled);

private:
    std::vector<StartupEntry> m_entries;
};

/**
 * @brief Monitors and manages Windows startup programs
 */
class StartupMonitor : public QObject
{
    Q_OBJECT

public:
    explicit StartupMonitor(QObject *parent = nullptr);
    ~StartupMonitor() override;

    /// Refresh the list of startup entries
    void refresh();
    
    /// Get the table model
    [[nodiscard]] StartupTableModel* model() { return m_model.get(); }
    
    /// Get all entries
    [[nodiscard]] const std::vector<StartupEntry>& entries() const { return m_entries; }
    
    /// Enable/disable a startup entry
    bool setEnabled(int index, bool enabled);
    bool setEnabled(const QString& name, StartupSource source, bool enabled);
    
    /// Delete a startup entry permanently
    bool deleteEntry(int index);
    
    /// Add a new startup entry
    bool addEntry(const QString& name, const QString& command, StartupSource source);
    
    /// Open the location of an entry (registry or folder)
    bool openLocation(int index);
    
    /// Open file location in Explorer
    bool openFileLocation(int index);
    
    /// Get startup statistics
    int totalCount() const { return static_cast<int>(m_entries.size()); }
    int enabledCount() const;
    int disabledCount() const;
    int highImpactCount() const;
    
    /// Check if running as admin (needed for HKLM entries)
    static bool isAdmin();
    
    /// Get source display name
    static QString sourceToString(StartupSource source);
    static QString impactToString(StartupImpact impact);

signals:
    void refreshed();
    void entryChanged(int index);
    void errorOccurred(const QString& error);

private:
    // Scan functions
    void scanRegistry(StartupSource source);
    void scanStartupFolders();
    void scanTaskScheduler();
    void scanServices();
    
    // Helper functions
    QString getRegistryPath(StartupSource source) const;
    QString getStartupFolderPath(bool allUsers) const;
    void extractExecutableInfo(StartupEntry& entry);
    StartupImpact estimateImpact(const StartupEntry& entry);
    QIcon getFileIcon(const QString& path);
    QString getFileDescription(const QString& path);
    QString getFileVersion(const QString& path);
    QString getFilePublisher(const QString& path);
    bool isMicrosoftSigned(const QString& path);
    
    // Modification functions
    bool enableRegistryEntry(const StartupEntry& entry, bool enable);
    bool enableStartupFolderEntry(const StartupEntry& entry, bool enable);
    bool enableTaskSchedulerEntry(const StartupEntry& entry, bool enable);
    bool enableServiceEntry(const StartupEntry& entry, bool enable);

    std::vector<StartupEntry> m_entries;
    std::unique_ptr<StartupTableModel> m_model;
    
    // Cache for disabled entries backup
    QString m_disabledBackupPath;
};
