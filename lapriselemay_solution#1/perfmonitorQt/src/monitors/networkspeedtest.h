#pragma once

#include <QObject>
#include <QNetworkAccessManager>
#include <QNetworkReply>
#include <QElapsedTimer>
#include <QTimer>
#include <QDateTime>
#include <QUrl>
#include <QTcpSocket>
#include <vector>
#include <memory>

/**
 * @brief Result of a single speed test
 */
struct SpeedTestResult {
    QDateTime timestamp;
    
    // Download
    double downloadSpeedMbps{0.0};
    qint64 downloadedBytes{0};
    int downloadDurationMs{0};
    
    // Upload
    double uploadSpeedMbps{0.0};
    qint64 uploadedBytes{0};
    int uploadDurationMs{0};
    
    // Latency
    int pingMs{0};
    int jitterMs{0};
    QVector<int> pingHistory;
    
    // Server info
    QString serverName;
    QString serverLocation;
    QString serverUrl;
    
    // Status
    bool success{false};
    QString errorMessage;
    
    // Helper methods
    QString downloadSpeedFormatted() const;
    QString uploadSpeedFormatted() const;
    QString latencyFormatted() const;
};

/**
 * @brief Test server information
 */
struct SpeedTestServer {
    QString name;
    QString location;
    QString downloadUrl;
    QString uploadUrl;
    QString pingHost;
    int pingPort{80};
    int estimatedLatency{0};
};

/**
 * @brief Current state of the speed test
 */
enum class SpeedTestState {
    Idle,
    SelectingServer,
    TestingPing,
    TestingDownload,
    TestingUpload,
    Completed,
    Error,
    Cancelled
};

/**
 * @brief Network Speed Test engine
 * 
 * Performs download, upload, and latency tests using public test servers.
 * Uses multiple connections for accurate bandwidth measurement.
 */
class NetworkSpeedTest : public QObject
{
    Q_OBJECT

public:
    explicit NetworkSpeedTest(QObject *parent = nullptr);
    ~NetworkSpeedTest() override;

    /// Start the complete speed test (ping + download + upload)
    void startTest();
    
    /// Start only ping test
    void startPingTest();
    
    /// Start only download test
    void startDownloadTest();
    
    /// Start only upload test
    void startUploadTest();
    
    /// Cancel ongoing test
    void cancelTest();
    
    /// Get current state
    SpeedTestState state() const { return m_state; }
    
    /// Get last result
    const SpeedTestResult& lastResult() const { return m_result; }
    
    /// Get available servers
    const std::vector<SpeedTestServer>& servers() const { return m_servers; }
    
    /// Set preferred server (empty = auto-select)
    void setPreferredServer(const QString& serverName);
    
    /// Get test history
    const std::vector<SpeedTestResult>& history() const { return m_history; }
    
    /// Clear history
    void clearHistory();
    
    // Test configuration
    void setDownloadDuration(int seconds) { m_downloadDurationSec = seconds; }
    void setUploadDuration(int seconds) { m_uploadDurationSec = seconds; }
    void setPingCount(int count) { m_pingCount = count; }
    void setParallelConnections(int count) { m_parallelConnections = count; }

signals:
    void stateChanged(SpeedTestState state);
    void progressChanged(int percent, const QString& status);
    
    // Real-time updates during test
    void pingUpdated(int pingMs);
    void downloadSpeedUpdated(double mbps);
    void uploadSpeedUpdated(double mbps);
    void downloadProgressUpdated(qint64 bytesReceived, double currentSpeedMbps);
    void uploadProgressUpdated(qint64 bytesSent, double currentSpeedMbps);
    
    // Completion signals
    void pingTestCompleted(int pingMs, int jitterMs);
    void downloadTestCompleted(double speedMbps);
    void uploadTestCompleted(double speedMbps);
    void testCompleted(const SpeedTestResult& result);
    void testFailed(const QString& error);

private slots:
    void onDownloadProgress(qint64 bytesReceived, qint64 bytesTotal);
    void onDownloadFinished();
    void onUploadProgress(qint64 bytesSent, qint64 bytesTotal);
    void onUploadFinished();
    void onServerSelectFinished();
    void onDownloadTimeout();
    void onUploadTimeout();

private:
    void setState(SpeedTestState newState);
    void initializeServers();
    void selectBestServer();
    void performPingTest();
    void performDownloadTest();
    void performUploadTest();
    void finishTest();
    void handleError(const QString& error);
    int measurePing(const QString& host, int port);
    QByteArray generateUploadData(qint64 size);
    double calculateSpeed(qint64 bytes, qint64 milliseconds);
    
    // State
    SpeedTestState m_state{SpeedTestState::Idle};
    SpeedTestResult m_result;
    std::vector<SpeedTestResult> m_history;
    
    // Network
    std::unique_ptr<QNetworkAccessManager> m_networkManager;
    QNetworkReply* m_currentReply{nullptr};
    std::vector<QNetworkReply*> m_parallelReplies;
    
    // Servers
    std::vector<SpeedTestServer> m_servers;
    SpeedTestServer m_selectedServer;
    QString m_preferredServerName;
    
    // Timing
    QElapsedTimer m_testTimer;
    QElapsedTimer m_speedTimer;
    std::unique_ptr<QTimer> m_timeoutTimer;
    
    // Download tracking
    qint64 m_totalBytesReceived{0};
    qint64 m_lastBytesReceived{0};
    qint64 m_lastSpeedUpdateTime{0};
    
    // Upload tracking
    qint64 m_totalBytesSent{0};
    qint64 m_lastBytesSent{0};
    QByteArray m_uploadData;
    
    // Configuration
    int m_downloadDurationSec{10};
    int m_uploadDurationSec{10};
    int m_pingCount{5};
    int m_parallelConnections{4};
    
    // Flags
    bool m_downloadOnly{false};
    bool m_uploadOnly{false};
    bool m_pingOnly{false};
};
