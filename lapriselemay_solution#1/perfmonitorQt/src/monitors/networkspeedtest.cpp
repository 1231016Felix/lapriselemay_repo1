#include "networkspeedtest.h"

#include <QNetworkRequest>
#include <QHttpMultiPart>
#include <QRandomGenerator>
#include <QThread>
#include <QtMath>
#include <algorithm>
#include <numeric>

// ============== SpeedTestResult Implementation ==============

QString SpeedTestResult::downloadSpeedFormatted() const
{
    if (downloadSpeedMbps >= 1000.0) {
        return QString("%1 Gbps").arg(downloadSpeedMbps / 1000.0, 0, 'f', 2);
    } else if (downloadSpeedMbps >= 1.0) {
        return QString("%1 Mbps").arg(downloadSpeedMbps, 0, 'f', 2);
    } else {
        return QString("%1 Kbps").arg(downloadSpeedMbps * 1000.0, 0, 'f', 0);
    }
}

QString SpeedTestResult::uploadSpeedFormatted() const
{
    if (uploadSpeedMbps >= 1000.0) {
        return QString("%1 Gbps").arg(uploadSpeedMbps / 1000.0, 0, 'f', 2);
    } else if (uploadSpeedMbps >= 1.0) {
        return QString("%1 Mbps").arg(uploadSpeedMbps, 0, 'f', 2);
    } else {
        return QString("%1 Kbps").arg(uploadSpeedMbps * 1000.0, 0, 'f', 0);
    }
}

QString SpeedTestResult::latencyFormatted() const
{
    return QString("%1 ms").arg(pingMs);
}

// ============== NetworkSpeedTest Implementation ==============

NetworkSpeedTest::NetworkSpeedTest(QObject *parent)
    : QObject(parent)
    , m_networkManager(std::make_unique<QNetworkAccessManager>())
    , m_timeoutTimer(std::make_unique<QTimer>())
{
    m_timeoutTimer->setSingleShot(true);
    initializeServers();
}

NetworkSpeedTest::~NetworkSpeedTest()
{
    cancelTest();
}

void NetworkSpeedTest::initializeServers()
{
    // Public test servers that allow bandwidth testing
    // Using Cloudflare and other CDN endpoints that are reliable
    
    m_servers.push_back({
        "Cloudflare",
        "Global CDN",
        "https://speed.cloudflare.com/__down?bytes=100000000",
        "https://speed.cloudflare.com/__up",
        "speed.cloudflare.com",
        443,
        0
    });
    
    m_servers.push_back({
        "Fast.com (Netflix)",
        "Global CDN",
        "https://api.fast.com/netflix/speedtest/v2?https=true&token=YXNkZmFzZGxmbnNkYWZoYXNkZmhrYWxm&urlCount=5",
        "",  // Fast.com doesn't support upload
        "api.fast.com",
        443,
        0
    });
    
    // Hetzner Speed Test (Europe)
    m_servers.push_back({
        "Hetzner",
        "Germany",
        "https://speed.hetzner.de/100MB.bin",
        "",
        "speed.hetzner.de",
        443,
        0
    });
    
    // OVH Speed Test
    m_servers.push_back({
        "OVH",
        "France", 
        "http://proof.ovh.net/files/100Mb.dat",
        "",
        "proof.ovh.net",
        80,
        0
    });
    
    // Tele2 Speed Test
    m_servers.push_back({
        "Tele2",
        "Sweden",
        "http://speedtest.tele2.net/100MB.zip",
        "http://speedtest.tele2.net/upload.php",
        "speedtest.tele2.net",
        80,
        0
    });
}

void NetworkSpeedTest::setState(SpeedTestState newState)
{
    if (m_state != newState) {
        m_state = newState;
        emit stateChanged(newState);
    }
}

void NetworkSpeedTest::startTest()
{
    if (m_state != SpeedTestState::Idle && m_state != SpeedTestState::Completed && 
        m_state != SpeedTestState::Error && m_state != SpeedTestState::Cancelled) {
        return;  // Test already in progress
    }
    
    // Reset result
    m_result = SpeedTestResult();
    m_result.timestamp = QDateTime::currentDateTime();
    m_downloadOnly = false;
    m_uploadOnly = false;
    m_pingOnly = false;
    
    emit progressChanged(0, tr("Selecting best server..."));
    selectBestServer();
}

void NetworkSpeedTest::startPingTest()
{
    if (m_state != SpeedTestState::Idle && m_state != SpeedTestState::Completed &&
        m_state != SpeedTestState::Error && m_state != SpeedTestState::Cancelled) {
        return;
    }
    
    m_result = SpeedTestResult();
    m_result.timestamp = QDateTime::currentDateTime();
    m_pingOnly = true;
    m_downloadOnly = false;
    m_uploadOnly = false;
    
    selectBestServer();
}

void NetworkSpeedTest::startDownloadTest()
{
    if (m_state != SpeedTestState::Idle && m_state != SpeedTestState::Completed &&
        m_state != SpeedTestState::Error && m_state != SpeedTestState::Cancelled) {
        return;
    }
    
    m_result = SpeedTestResult();
    m_result.timestamp = QDateTime::currentDateTime();
    m_downloadOnly = true;
    m_uploadOnly = false;
    m_pingOnly = false;
    
    selectBestServer();
}

void NetworkSpeedTest::startUploadTest()
{
    if (m_state != SpeedTestState::Idle && m_state != SpeedTestState::Completed &&
        m_state != SpeedTestState::Error && m_state != SpeedTestState::Cancelled) {
        return;
    }
    
    m_result = SpeedTestResult();
    m_result.timestamp = QDateTime::currentDateTime();
    m_uploadOnly = true;
    m_downloadOnly = false;
    m_pingOnly = false;
    
    selectBestServer();
}

void NetworkSpeedTest::cancelTest()
{
    if (m_currentReply) {
        m_currentReply->abort();
        m_currentReply->deleteLater();
        m_currentReply = nullptr;
    }
    
    for (auto* reply : m_parallelReplies) {
        if (reply) {
            reply->abort();
            reply->deleteLater();
        }
    }
    m_parallelReplies.clear();
    
    m_timeoutTimer->stop();
    setState(SpeedTestState::Cancelled);
}

void NetworkSpeedTest::selectBestServer()
{
    setState(SpeedTestState::SelectingServer);
    
    // If preferred server is set, use it
    if (!m_preferredServerName.isEmpty()) {
        for (const auto& server : m_servers) {
            if (server.name == m_preferredServerName) {
                m_selectedServer = server;
                performPingTest();
                return;
            }
        }
    }
    
    // Test ping to all servers and select the best one
    int bestLatency = std::numeric_limits<int>::max();
    int bestIndex = 0;
    
    emit progressChanged(5, tr("Testing server latency..."));
    
    for (size_t i = 0; i < m_servers.size(); ++i) {
        int latency = measurePing(m_servers[i].pingHost, m_servers[i].pingPort);
        m_servers[i].estimatedLatency = latency;
        
        if (latency > 0 && latency < bestLatency) {
            bestLatency = latency;
            bestIndex = static_cast<int>(i);
        }
    }
    
    if (bestLatency == std::numeric_limits<int>::max()) {
        // Use first server as fallback
        m_selectedServer = m_servers[0];
    } else {
        m_selectedServer = m_servers[bestIndex];
    }
    
    m_result.serverName = m_selectedServer.name;
    m_result.serverLocation = m_selectedServer.location;
    m_result.serverUrl = m_selectedServer.downloadUrl;
    
    emit progressChanged(10, tr("Server selected: %1").arg(m_selectedServer.name));
    
    performPingTest();
}

int NetworkSpeedTest::measurePing(const QString& host, int port)
{
    QTcpSocket socket;
    QElapsedTimer timer;
    
    timer.start();
    socket.connectToHost(host, port);
    
    if (socket.waitForConnected(3000)) {
        int elapsed = static_cast<int>(timer.elapsed());
        socket.disconnectFromHost();
        return elapsed;
    }
    
    return -1;  // Connection failed
}

void NetworkSpeedTest::performPingTest()
{
    setState(SpeedTestState::TestingPing);
    emit progressChanged(15, tr("Testing latency..."));
    
    QVector<int> pings;
    
    for (int i = 0; i < m_pingCount; ++i) {
        int ping = measurePing(m_selectedServer.pingHost, m_selectedServer.pingPort);
        if (ping > 0) {
            pings.append(ping);
            emit pingUpdated(ping);
        }
        QThread::msleep(100);  // Small delay between pings
    }
    
    if (pings.isEmpty()) {
        m_result.pingMs = -1;
        m_result.jitterMs = 0;
    } else {
        // Calculate average ping
        int sum = std::accumulate(pings.begin(), pings.end(), 0);
        m_result.pingMs = sum / pings.size();
        m_result.pingHistory = pings;
        
        // Calculate jitter (average deviation from mean)
        if (pings.size() > 1) {
            double sumDeviation = 0.0;
            for (int ping : pings) {
                sumDeviation += qAbs(ping - m_result.pingMs);
            }
            m_result.jitterMs = static_cast<int>(sumDeviation / pings.size());
        }
    }
    
    emit pingTestCompleted(m_result.pingMs, m_result.jitterMs);
    emit progressChanged(25, tr("Latency: %1 ms").arg(m_result.pingMs));
    
    if (m_pingOnly) {
        finishTest();
    } else if (!m_uploadOnly) {
        performDownloadTest();
    } else {
        performUploadTest();
    }
}

void NetworkSpeedTest::performDownloadTest()
{
    setState(SpeedTestState::TestingDownload);
    emit progressChanged(30, tr("Testing download speed..."));
    
    m_totalBytesReceived = 0;
    m_lastBytesReceived = 0;
    m_lastSpeedUpdateTime = 0;
    
    // Start timing
    m_testTimer.start();
    m_speedTimer.start();
    
    // Create download request
    QNetworkRequest request(QUrl(m_selectedServer.downloadUrl));
    request.setAttribute(QNetworkRequest::RedirectPolicyAttribute, 
                        QNetworkRequest::NoLessSafeRedirectPolicy);
    request.setRawHeader("User-Agent", "PerfMonitorQt/1.0 SpeedTest");
    request.setRawHeader("Cache-Control", "no-cache");
    
    m_currentReply = m_networkManager->get(request);
    
    connect(m_currentReply, &QNetworkReply::downloadProgress,
            this, &NetworkSpeedTest::onDownloadProgress);
    connect(m_currentReply, &QNetworkReply::finished,
            this, &NetworkSpeedTest::onDownloadFinished);
    
    // Set timeout
    connect(m_timeoutTimer.get(), &QTimer::timeout,
            this, &NetworkSpeedTest::onDownloadTimeout);
    m_timeoutTimer->start(m_downloadDurationSec * 1000 + 5000);  // Extra 5s buffer
}

void NetworkSpeedTest::onDownloadProgress(qint64 bytesReceived, qint64 bytesTotal)
{
    Q_UNUSED(bytesTotal)
    
    m_totalBytesReceived = bytesReceived;
    
    // Update speed every 200ms
    qint64 currentTime = m_speedTimer.elapsed();
    if (currentTime - m_lastSpeedUpdateTime >= 200) {
        qint64 bytesInInterval = bytesReceived - m_lastBytesReceived;
        qint64 timeInInterval = currentTime - m_lastSpeedUpdateTime;
        
        if (timeInInterval > 0) {
            double currentSpeedMbps = calculateSpeed(bytesInInterval, timeInInterval);
            emit downloadSpeedUpdated(currentSpeedMbps);
            emit downloadProgressUpdated(bytesReceived, currentSpeedMbps);
        }
        
        m_lastBytesReceived = bytesReceived;
        m_lastSpeedUpdateTime = currentTime;
    }
    
    // Calculate progress (30% to 65% of total test)
    int elapsed = static_cast<int>(m_testTimer.elapsed());
    int progressPercent = 30 + (35 * elapsed / (m_downloadDurationSec * 1000));
    progressPercent = qMin(progressPercent, 65);
    
    double overallSpeed = calculateSpeed(bytesReceived, m_testTimer.elapsed());
    emit progressChanged(progressPercent, 
        tr("Download: %1 Mbps").arg(overallSpeed, 0, 'f', 2));
    
    // Check if we've downloaded for long enough
    if (elapsed >= m_downloadDurationSec * 1000) {
        m_currentReply->abort();
    }
}

void NetworkSpeedTest::onDownloadFinished()
{
    m_timeoutTimer->stop();
    
    if (m_state != SpeedTestState::TestingDownload) {
        return;  // Test was cancelled
    }
    
    m_result.downloadDurationMs = static_cast<int>(m_testTimer.elapsed());
    m_result.downloadedBytes = m_totalBytesReceived;
    m_result.downloadSpeedMbps = calculateSpeed(m_totalBytesReceived, m_result.downloadDurationMs);
    
    if (m_currentReply) {
        m_currentReply->deleteLater();
        m_currentReply = nullptr;
    }
    
    emit downloadTestCompleted(m_result.downloadSpeedMbps);
    emit progressChanged(65, tr("Download: %1").arg(m_result.downloadSpeedFormatted()));
    
    if (m_downloadOnly) {
        finishTest();
    } else {
        performUploadTest();
    }
}

void NetworkSpeedTest::onDownloadTimeout()
{
    if (m_currentReply) {
        m_currentReply->abort();
    }
}

void NetworkSpeedTest::performUploadTest()
{
    // Check if server supports upload
    if (m_selectedServer.uploadUrl.isEmpty()) {
        // Skip upload test
        emit progressChanged(95, tr("Upload test not available for this server"));
        finishTest();
        return;
    }
    
    setState(SpeedTestState::TestingUpload);
    emit progressChanged(70, tr("Testing upload speed..."));
    
    m_totalBytesSent = 0;
    m_lastBytesSent = 0;
    m_lastSpeedUpdateTime = 0;
    
    // Generate random data for upload (25MB)
    const qint64 uploadSize = 25 * 1024 * 1024;
    m_uploadData = generateUploadData(uploadSize);
    
    // Start timing
    m_testTimer.restart();
    m_speedTimer.restart();
    
    // Create upload request
    QNetworkRequest request(QUrl(m_selectedServer.uploadUrl));
    request.setAttribute(QNetworkRequest::RedirectPolicyAttribute,
                        QNetworkRequest::NoLessSafeRedirectPolicy);
    request.setRawHeader("User-Agent", "PerfMonitorQt/1.0 SpeedTest");
    request.setRawHeader("Content-Type", "application/octet-stream");
    request.setHeader(QNetworkRequest::ContentLengthHeader, m_uploadData.size());
    
    m_currentReply = m_networkManager->post(request, m_uploadData);
    
    connect(m_currentReply, &QNetworkReply::uploadProgress,
            this, &NetworkSpeedTest::onUploadProgress);
    connect(m_currentReply, &QNetworkReply::finished,
            this, &NetworkSpeedTest::onUploadFinished);
    
    // Set timeout
    disconnect(m_timeoutTimer.get(), nullptr, nullptr, nullptr);
    connect(m_timeoutTimer.get(), &QTimer::timeout,
            this, &NetworkSpeedTest::onUploadTimeout);
    m_timeoutTimer->start(m_uploadDurationSec * 1000 + 10000);
}

void NetworkSpeedTest::onUploadProgress(qint64 bytesSent, qint64 bytesTotal)
{
    Q_UNUSED(bytesTotal)
    
    m_totalBytesSent = bytesSent;
    
    // Update speed every 200ms
    qint64 currentTime = m_speedTimer.elapsed();
    if (currentTime - m_lastSpeedUpdateTime >= 200) {
        qint64 bytesInInterval = bytesSent - m_lastBytesSent;
        qint64 timeInInterval = currentTime - m_lastSpeedUpdateTime;
        
        if (timeInInterval > 0) {
            double currentSpeedMbps = calculateSpeed(bytesInInterval, timeInInterval);
            emit uploadSpeedUpdated(currentSpeedMbps);
            emit uploadProgressUpdated(bytesSent, currentSpeedMbps);
        }
        
        m_lastBytesSent = bytesSent;
        m_lastSpeedUpdateTime = currentTime;
    }
    
    // Calculate progress (70% to 95% of total test)
    int elapsed = static_cast<int>(m_testTimer.elapsed());
    int progressPercent = 70 + (25 * elapsed / (m_uploadDurationSec * 1000));
    progressPercent = qMin(progressPercent, 95);
    
    double overallSpeed = calculateSpeed(bytesSent, m_testTimer.elapsed());
    emit progressChanged(progressPercent,
        tr("Upload: %1 Mbps").arg(overallSpeed, 0, 'f', 2));
    
    // Check if we've uploaded for long enough
    if (elapsed >= m_uploadDurationSec * 1000) {
        m_currentReply->abort();
    }
}

void NetworkSpeedTest::onUploadFinished()
{
    m_timeoutTimer->stop();
    
    if (m_state != SpeedTestState::TestingUpload) {
        return;
    }
    
    m_result.uploadDurationMs = static_cast<int>(m_testTimer.elapsed());
    m_result.uploadedBytes = m_totalBytesSent;
    m_result.uploadSpeedMbps = calculateSpeed(m_totalBytesSent, m_result.uploadDurationMs);
    
    if (m_currentReply) {
        m_currentReply->deleteLater();
        m_currentReply = nullptr;
    }
    
    m_uploadData.clear();  // Free memory
    
    emit uploadTestCompleted(m_result.uploadSpeedMbps);
    emit progressChanged(95, tr("Upload: %1").arg(m_result.uploadSpeedFormatted()));
    
    finishTest();
}

void NetworkSpeedTest::onUploadTimeout()
{
    if (m_currentReply) {
        m_currentReply->abort();
    }
}

void NetworkSpeedTest::onServerSelectFinished()
{
    // Not used currently - server selection is synchronous
}

void NetworkSpeedTest::finishTest()
{
    m_result.success = true;
    m_history.push_back(m_result);
    
    // Limit history to last 100 results
    if (m_history.size() > 100) {
        m_history.erase(m_history.begin());
    }
    
    setState(SpeedTestState::Completed);
    emit progressChanged(100, tr("Test completed"));
    emit testCompleted(m_result);
}

void NetworkSpeedTest::handleError(const QString& error)
{
    m_result.success = false;
    m_result.errorMessage = error;
    
    setState(SpeedTestState::Error);
    emit testFailed(error);
}

QByteArray NetworkSpeedTest::generateUploadData(qint64 size)
{
    QByteArray data;
    data.reserve(size);
    
    // Generate pseudo-random data (faster than truly random)
    QRandomGenerator* rng = QRandomGenerator::global();
    
    // Generate in chunks for efficiency
    const int chunkSize = 4096;
    QByteArray chunk;
    chunk.resize(chunkSize);
    
    while (data.size() < size) {
        for (int i = 0; i < chunkSize; i += 4) {
            quint32 random = rng->generate();
            memcpy(chunk.data() + i, &random, 4);
        }
        data.append(chunk);
    }
    
    data.resize(size);
    return data;
}

double NetworkSpeedTest::calculateSpeed(qint64 bytes, qint64 milliseconds)
{
    if (milliseconds <= 0) return 0.0;
    
    // Convert to Mbps (megabits per second)
    // bytes * 8 (bits) / 1000000 (mega) / (ms / 1000) (seconds)
    double bits = bytes * 8.0;
    double seconds = milliseconds / 1000.0;
    double mbps = (bits / 1000000.0) / seconds;
    
    return mbps;
}

void NetworkSpeedTest::setPreferredServer(const QString& serverName)
{
    m_preferredServerName = serverName;
}

void NetworkSpeedTest::clearHistory()
{
    m_history.clear();
}
