#include "networkspeedtestdialog.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QGridLayout>
#include <QGroupBox>
#include <QHeaderView>
#include <QPainter>
#include <QPainterPath>
#include <QDateTime>
#include <QtMath>

// ============== SpeedGauge Implementation ==============

SpeedGauge::SpeedGauge(QWidget* parent)
    : QWidget(parent)
{
    setMinimumSize(200, 200);
    setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Expanding);
}

void SpeedGauge::setValue(double value)
{
    m_value = qBound(0.0, value, m_maxValue);
    update();
}

void SpeedGauge::reset()
{
    m_value = 0.0;
    update();
}

void SpeedGauge::paintEvent(QPaintEvent* event)
{
    Q_UNUSED(event)
    
    QPainter painter(this);
    painter.setRenderHint(QPainter::Antialiasing);
    
    // Reserve more space for title at bottom
    int titleHeight = 50;
    int availableHeight = height() - titleHeight;
    
    // Calculate responsive sizes - use smaller of width or available height
    int side = qMin(width(), availableHeight);
    int margin = qMax(15, side / 12);
    int arcThickness = qMax(10, side / 12);
    
    // Make gauge rect smaller to fit
    int gaugeSize = side - 2 * margin;
    QRect gaugeRect(0, 0, gaugeSize, gaugeSize);
    
    // Center the gauge horizontally and in the top area (above title)
    gaugeRect.moveCenter(QPoint(width() / 2, availableHeight / 2));
    
    // Draw background arc
    painter.setPen(QPen(QColor(60, 60, 60), arcThickness, Qt::SolidLine, Qt::RoundCap));
    painter.drawArc(gaugeRect, 225 * 16, -270 * 16);
    
    // Draw value arc
    double ratio = m_value / m_maxValue;
    int spanAngle = static_cast<int>(-270 * ratio * 16);
    
    QLinearGradient gradient(gaugeRect.topLeft(), gaugeRect.bottomRight());
    gradient.setColorAt(0, m_color.lighter(150));
    gradient.setColorAt(1, m_color);
    
    painter.setPen(QPen(QBrush(gradient), arcThickness, Qt::SolidLine, Qt::RoundCap));
    painter.drawArc(gaugeRect, 225 * 16, spanAngle);
    
    // Draw center circle
    int centerRadius = gaugeRect.width() / 4;
    QRect centerRect(
        gaugeRect.center().x() - centerRadius,
        gaugeRect.center().y() - centerRadius,
        centerRadius * 2,
        centerRadius * 2
    );
    
    painter.setPen(Qt::NoPen);
    painter.setBrush(QColor(40, 40, 45));
    painter.drawEllipse(centerRect);
    
    // Responsive font sizes
    int valueFontSize = qMax(14, gaugeSize / 6);
    int unitFontSize = qMax(9, gaugeSize / 14);
    
    // Draw value text
    painter.setPen(Qt::white);
    QFont valueFont = font();
    valueFont.setPixelSize(valueFontSize);
    valueFont.setBold(true);
    painter.setFont(valueFont);
    
    QString valueText;
    if (m_value >= 1000) {
        valueText = QString::number(m_value / 1000.0, 'f', 2);
    } else if (m_value >= 100) {
        valueText = QString::number(m_value, 'f', 0);
    } else if (m_value >= 10) {
        valueText = QString::number(m_value, 'f', 1);
    } else {
        valueText = QString::number(m_value, 'f', 2);
    }
    
    QRect valueTextRect = centerRect.adjusted(0, -valueFontSize / 3, 0, -valueFontSize / 3);
    painter.drawText(valueTextRect, Qt::AlignCenter, valueText);
    
    // Draw unit text
    QFont unitFont = font();
    unitFont.setPixelSize(unitFontSize);
    painter.setFont(unitFont);
    painter.setPen(QColor(150, 150, 150));
    
    QString unitText = (m_value >= 1000) ? "Gbps" : m_unit;
    QRect unitTextRect = centerRect.adjusted(0, valueFontSize / 2, 0, valueFontSize / 2);
    painter.drawText(unitTextRect, Qt::AlignCenter, unitText);
    
    // Draw title in the reserved bottom area
    painter.setPen(m_color.lighter(130));
    QFont titleFont = font();
    titleFont.setPixelSize(16);
    titleFont.setBold(true);
    titleFont.setLetterSpacing(QFont::AbsoluteSpacing, 3);
    painter.setFont(titleFont);
    
    // Title rect starts after the gauge area
    QRect titleRect(0, availableHeight, width(), titleHeight);
    painter.drawText(titleRect, Qt::AlignHCenter | Qt::AlignTop, m_title);
}

// ============== NetworkSpeedTestDialog Implementation ==============

NetworkSpeedTestDialog::NetworkSpeedTestDialog(QWidget* parent)
    : QDialog(parent)
    , m_speedTest(std::make_unique<NetworkSpeedTest>())
    , m_animationTimer(new QTimer(this))
{
    setWindowTitle(tr("Network Speed Test"));
    setMinimumSize(700, 600);
    resize(800, 700);
    setWindowFlags(windowFlags() | Qt::WindowMaximizeButtonHint);
    
    setupUi();
    connectSignals();
    
    // Setup animation timer for smooth gauge updates
    connect(m_animationTimer, &QTimer::timeout, this, [this]() {
        // Smooth interpolation
        double alpha = 0.3;
        m_currentDownloadSpeed += (m_targetDownloadSpeed - m_currentDownloadSpeed) * alpha;
        m_currentUploadSpeed += (m_targetUploadSpeed - m_currentUploadSpeed) * alpha;
        
        m_downloadGauge->setValue(m_currentDownloadSpeed);
        m_uploadGauge->setValue(m_currentUploadSpeed);
    });
    m_animationTimer->setInterval(50);
    
    // Update history table
    updateHistoryTable();
}

NetworkSpeedTestDialog::~NetworkSpeedTestDialog()
{
    m_speedTest->cancelTest();
}

void NetworkSpeedTestDialog::setupUi()
{
    auto mainLayout = new QVBoxLayout(this);
    mainLayout->setSpacing(15);
    
    createGaugesSection(mainLayout);
    createControlsSection(mainLayout);
    createResultsSection(mainLayout);
    createHistorySection(mainLayout);
}

void NetworkSpeedTestDialog::createGaugesSection(QVBoxLayout* layout)
{
    auto gaugesWidget = new QWidget();
    gaugesWidget->setStyleSheet("background-color: #1a1a1f; border-radius: 10px;");
    auto gaugesLayout = new QHBoxLayout(gaugesWidget);
    gaugesLayout->setContentsMargins(20, 20, 20, 20);
    
    // Download gauge
    m_downloadGauge = new SpeedGauge();
    m_downloadGauge->setTitle(tr("DOWNLOAD"));
    m_downloadGauge->setColor(QColor(0, 200, 100));
    m_downloadGauge->setMaxValue(500);
    gaugesLayout->addWidget(m_downloadGauge);
    
    // Center info panel
    auto centerWidget = new QWidget();
    auto centerLayout = new QVBoxLayout(centerWidget);
    centerLayout->setAlignment(Qt::AlignCenter);
    
    m_pingLabel = new QLabel("-- ms");
    m_pingLabel->setStyleSheet("font-size: 24px; font-weight: bold; color: white;");
    m_pingLabel->setAlignment(Qt::AlignCenter);
    
    auto pingTitle = new QLabel(tr("PING"));
    pingTitle->setStyleSheet("font-size: 12px; color: #888;");
    pingTitle->setAlignment(Qt::AlignCenter);
    
    m_jitterLabel = new QLabel("-- ms");
    m_jitterLabel->setStyleSheet("font-size: 18px; color: #aaa;");
    m_jitterLabel->setAlignment(Qt::AlignCenter);
    
    auto jitterTitle = new QLabel(tr("JITTER"));
    jitterTitle->setStyleSheet("font-size: 10px; color: #666;");
    jitterTitle->setAlignment(Qt::AlignCenter);
    
    centerLayout->addStretch();
    centerLayout->addWidget(pingTitle);
    centerLayout->addWidget(m_pingLabel);
    centerLayout->addSpacing(10);
    centerLayout->addWidget(jitterTitle);
    centerLayout->addWidget(m_jitterLabel);
    centerLayout->addStretch();
    
    gaugesLayout->addWidget(centerWidget);
    
    // Upload gauge
    m_uploadGauge = new SpeedGauge();
    m_uploadGauge->setTitle(tr("UPLOAD"));
    m_uploadGauge->setColor(QColor(100, 100, 255));
    m_uploadGauge->setMaxValue(500);
    gaugesLayout->addWidget(m_uploadGauge);
    
    layout->addWidget(gaugesWidget);
}

void NetworkSpeedTestDialog::createControlsSection(QVBoxLayout* layout)
{
    auto controlsWidget = new QWidget();
    auto controlsLayout = new QHBoxLayout(controlsWidget);
    
    // Server selection
    auto serverLabel = new QLabel(tr("Server:"));
    m_serverCombo = new QComboBox();
    m_serverCombo->addItem(tr("Auto (Best Server)"), "");
    for (const auto& server : m_speedTest->servers()) {
        m_serverCombo->addItem(QString("%1 - %2").arg(server.name, server.location), server.name);
    }
    m_serverCombo->setMinimumWidth(200);
    
    controlsLayout->addWidget(serverLabel);
    controlsLayout->addWidget(m_serverCombo);
    controlsLayout->addStretch();
    
    // Start/Stop buttons
    m_startButton = new QPushButton(tr("▶ Start Test"));
    m_startButton->setStyleSheet(
        "QPushButton { background-color: #00c864; color: white; font-weight: bold; "
        "padding: 12px 30px; border-radius: 5px; font-size: 14px; }"
        "QPushButton:hover { background-color: #00e070; }"
        "QPushButton:pressed { background-color: #00a050; }"
    );
    
    m_stopButton = new QPushButton(tr("⬛ Stop"));
    m_stopButton->setStyleSheet(
        "QPushButton { background-color: #c83232; color: white; font-weight: bold; "
        "padding: 12px 30px; border-radius: 5px; font-size: 14px; }"
        "QPushButton:hover { background-color: #e03838; }"
        "QPushButton:pressed { background-color: #a02828; }"
    );
    m_stopButton->setEnabled(false);
    
    controlsLayout->addWidget(m_startButton);
    controlsLayout->addWidget(m_stopButton);
    
    layout->addWidget(controlsWidget);
    
    // Progress bar
    m_progressBar = new QProgressBar();
    m_progressBar->setRange(0, 100);
    m_progressBar->setValue(0);
    m_progressBar->setTextVisible(true);
    m_progressBar->setStyleSheet(
        "QProgressBar { border: none; background-color: #2a2a2f; border-radius: 5px; height: 25px; }"
        "QProgressBar::chunk { background-color: #00c864; border-radius: 5px; }"
    );
    layout->addWidget(m_progressBar);
    
    // Status label
    m_statusLabel = new QLabel(tr("Ready to test"));
    m_statusLabel->setStyleSheet("color: #888; font-size: 12px;");
    m_statusLabel->setAlignment(Qt::AlignCenter);
    layout->addWidget(m_statusLabel);
}

void NetworkSpeedTestDialog::createResultsSection(QVBoxLayout* layout)
{
    auto resultsGroup = new QGroupBox(tr("Results"));
    auto resultsLayout = new QGridLayout(resultsGroup);
    
    // Download result
    auto dlIcon = new QLabel("⬇");
    dlIcon->setStyleSheet("font-size: 20px; color: #00c864;");
    resultsLayout->addWidget(dlIcon, 0, 0);
    
    auto dlTitle = new QLabel(tr("Download:"));
    resultsLayout->addWidget(dlTitle, 0, 1);
    
    m_downloadLabel = new QLabel("-- Mbps");
    m_downloadLabel->setStyleSheet("font-weight: bold; font-size: 14px;");
    resultsLayout->addWidget(m_downloadLabel, 0, 2);
    
    // Upload result
    auto ulIcon = new QLabel("⬆");
    ulIcon->setStyleSheet("font-size: 20px; color: #6464ff;");
    resultsLayout->addWidget(ulIcon, 0, 3);
    
    auto ulTitle = new QLabel(tr("Upload:"));
    resultsLayout->addWidget(ulTitle, 0, 4);
    
    m_uploadLabel = new QLabel("-- Mbps");
    m_uploadLabel->setStyleSheet("font-weight: bold; font-size: 14px;");
    resultsLayout->addWidget(m_uploadLabel, 0, 5);
    
    // Server info
    auto serverIcon = new QLabel("🌐");
    serverIcon->setStyleSheet("font-size: 16px;");
    resultsLayout->addWidget(serverIcon, 1, 0);
    
    auto serverTitle = new QLabel(tr("Server:"));
    resultsLayout->addWidget(serverTitle, 1, 1);
    
    m_serverLabel = new QLabel("--");
    m_serverLabel->setStyleSheet("color: #888;");
    resultsLayout->addWidget(m_serverLabel, 1, 2, 1, 4);
    
    resultsLayout->setColumnStretch(2, 1);
    resultsLayout->setColumnStretch(5, 1);
    
    layout->addWidget(resultsGroup);
}

void NetworkSpeedTestDialog::createHistorySection(QVBoxLayout* layout)
{
    auto historyGroup = new QGroupBox(tr("Test History"));
    auto historyLayout = new QVBoxLayout(historyGroup);
    
    m_historyTable = new QTableWidget();
    m_historyTable->setColumnCount(6);
    m_historyTable->setHorizontalHeaderLabels({
        tr("Time"), tr("Server"), tr("Ping"), tr("Download"), tr("Upload"), tr("Status")
    });
    m_historyTable->horizontalHeader()->setStretchLastSection(true);
    m_historyTable->horizontalHeader()->setSectionResizeMode(QHeaderView::ResizeToContents);
    m_historyTable->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_historyTable->setAlternatingRowColors(true);
    m_historyTable->setMaximumHeight(150);
    m_historyTable->verticalHeader()->setVisible(false);
    
    historyLayout->addWidget(m_historyTable);
    layout->addWidget(historyGroup);
}

void NetworkSpeedTestDialog::connectSignals()
{
    connect(m_startButton, &QPushButton::clicked, this, &NetworkSpeedTestDialog::onStartTest);
    connect(m_stopButton, &QPushButton::clicked, this, &NetworkSpeedTestDialog::onStopTest);
    connect(m_serverCombo, QOverload<int>::of(&QComboBox::currentIndexChanged),
            this, &NetworkSpeedTestDialog::onServerChanged);
    
    connect(m_speedTest.get(), &NetworkSpeedTest::stateChanged,
            this, &NetworkSpeedTestDialog::onStateChanged);
    connect(m_speedTest.get(), &NetworkSpeedTest::progressChanged,
            this, &NetworkSpeedTestDialog::onProgressChanged);
    connect(m_speedTest.get(), &NetworkSpeedTest::pingUpdated,
            this, &NetworkSpeedTestDialog::onPingUpdated);
    connect(m_speedTest.get(), &NetworkSpeedTest::downloadSpeedUpdated,
            this, &NetworkSpeedTestDialog::onDownloadSpeedUpdated);
    connect(m_speedTest.get(), &NetworkSpeedTest::uploadSpeedUpdated,
            this, &NetworkSpeedTestDialog::onUploadSpeedUpdated);
    connect(m_speedTest.get(), &NetworkSpeedTest::testCompleted,
            this, &NetworkSpeedTestDialog::onTestCompleted);
    connect(m_speedTest.get(), &NetworkSpeedTest::testFailed,
            this, &NetworkSpeedTestDialog::onTestFailed);
}

void NetworkSpeedTestDialog::onStartTest()
{
    resetUI();
    m_animationTimer->start();
    m_speedTest->startTest();
}

void NetworkSpeedTestDialog::onStopTest()
{
    m_speedTest->cancelTest();
    m_animationTimer->stop();
    m_statusLabel->setText(tr("Test cancelled"));
}

void NetworkSpeedTestDialog::onStateChanged(SpeedTestState state)
{
    switch (state) {
        case SpeedTestState::Idle:
            m_startButton->setEnabled(true);
            m_stopButton->setEnabled(false);
            m_serverCombo->setEnabled(true);
            break;
            
        case SpeedTestState::SelectingServer:
        case SpeedTestState::TestingPing:
        case SpeedTestState::TestingDownload:
        case SpeedTestState::TestingUpload:
            m_startButton->setEnabled(false);
            m_stopButton->setEnabled(true);
            m_serverCombo->setEnabled(false);
            break;
            
        case SpeedTestState::Completed:
        case SpeedTestState::Error:
        case SpeedTestState::Cancelled:
            m_startButton->setEnabled(true);
            m_stopButton->setEnabled(false);
            m_serverCombo->setEnabled(true);
            m_animationTimer->stop();
            break;
    }
}

void NetworkSpeedTestDialog::onProgressChanged(int percent, const QString& status)
{
    m_progressBar->setValue(percent);
    m_statusLabel->setText(status);
}

void NetworkSpeedTestDialog::onPingUpdated(int pingMs)
{
    m_pingLabel->setText(QString("%1 ms").arg(pingMs));
}

void NetworkSpeedTestDialog::onDownloadSpeedUpdated(double mbps)
{
    m_targetDownloadSpeed = mbps;
    m_downloadLabel->setText(formatSpeed(mbps));
    
    // Auto-adjust gauge max
    if (mbps > m_downloadGauge->property("maxValue").toDouble() * 0.8) {
        double newMax = mbps * 1.5;
        m_downloadGauge->setMaxValue(newMax);
    }
}

void NetworkSpeedTestDialog::onUploadSpeedUpdated(double mbps)
{
    m_targetUploadSpeed = mbps;
    m_uploadLabel->setText(formatSpeed(mbps));
    
    // Auto-adjust gauge max
    if (mbps > m_uploadGauge->property("maxValue").toDouble() * 0.8) {
        double newMax = mbps * 1.5;
        m_uploadGauge->setMaxValue(newMax);
    }
}

void NetworkSpeedTestDialog::onTestCompleted(const SpeedTestResult& result)
{
    // Final values
    m_pingLabel->setText(QString("%1 ms").arg(result.pingMs));
    m_jitterLabel->setText(QString("%1 ms").arg(result.jitterMs));
    m_downloadLabel->setText(result.downloadSpeedFormatted());
    m_uploadLabel->setText(result.uploadSpeedFormatted());
    m_serverLabel->setText(QString("%1 (%2)").arg(result.serverName, result.serverLocation));
    
    m_targetDownloadSpeed = result.downloadSpeedMbps;
    m_targetUploadSpeed = result.uploadSpeedMbps;
    
    m_statusLabel->setText(tr("✓ Test completed successfully"));
    m_statusLabel->setStyleSheet("color: #00c864; font-size: 12px; font-weight: bold;");
    
    // Add to history
    addResultToHistory(result);
}

void NetworkSpeedTestDialog::onTestFailed(const QString& error)
{
    m_statusLabel->setText(tr("✗ Error: %1").arg(error));
    m_statusLabel->setStyleSheet("color: #ff4444; font-size: 12px;");
}

void NetworkSpeedTestDialog::onServerChanged(int index)
{
    QString serverName = m_serverCombo->itemData(index).toString();
    m_speedTest->setPreferredServer(serverName);
}

void NetworkSpeedTestDialog::resetUI()
{
    m_pingLabel->setText("-- ms");
    m_jitterLabel->setText("-- ms");
    m_downloadLabel->setText("-- Mbps");
    m_uploadLabel->setText("-- Mbps");
    m_serverLabel->setText("--");
    m_statusLabel->setText(tr("Starting test..."));
    m_statusLabel->setStyleSheet("color: #888; font-size: 12px;");
    
    m_progressBar->setValue(0);
    
    m_downloadGauge->reset();
    m_uploadGauge->reset();
    m_downloadGauge->setMaxValue(500);
    m_uploadGauge->setMaxValue(500);
    
    m_targetDownloadSpeed = 0;
    m_targetUploadSpeed = 0;
    m_currentDownloadSpeed = 0;
    m_currentUploadSpeed = 0;
}

void NetworkSpeedTestDialog::updateHistoryTable()
{
    const auto& history = m_speedTest->history();
    
    m_historyTable->setRowCount(0);
    
    for (int i = static_cast<int>(history.size()) - 1; i >= 0; --i) {
        const auto& result = history[i];
        int row = m_historyTable->rowCount();
        m_historyTable->insertRow(row);
        
        m_historyTable->setItem(row, 0, new QTableWidgetItem(
            result.timestamp.toString("yyyy-MM-dd HH:mm")));
        m_historyTable->setItem(row, 1, new QTableWidgetItem(result.serverName));
        m_historyTable->setItem(row, 2, new QTableWidgetItem(
            QString("%1 ms").arg(result.pingMs)));
        m_historyTable->setItem(row, 3, new QTableWidgetItem(
            result.downloadSpeedFormatted()));
        m_historyTable->setItem(row, 4, new QTableWidgetItem(
            result.uploadSpeedFormatted()));
        m_historyTable->setItem(row, 5, new QTableWidgetItem(
            result.success ? tr("✓ OK") : tr("✗ Failed")));
    }
}

void NetworkSpeedTestDialog::addResultToHistory(const SpeedTestResult& result)
{
    int row = 0;
    m_historyTable->insertRow(row);
    
    m_historyTable->setItem(row, 0, new QTableWidgetItem(
        result.timestamp.toString("yyyy-MM-dd HH:mm")));
    m_historyTable->setItem(row, 1, new QTableWidgetItem(result.serverName));
    m_historyTable->setItem(row, 2, new QTableWidgetItem(
        QString("%1 ms").arg(result.pingMs)));
    m_historyTable->setItem(row, 3, new QTableWidgetItem(
        result.downloadSpeedFormatted()));
    m_historyTable->setItem(row, 4, new QTableWidgetItem(
        result.uploadSpeedFormatted()));
    
    auto statusItem = new QTableWidgetItem(result.success ? tr("✓ OK") : tr("✗ Failed"));
    statusItem->setForeground(result.success ? QColor(0, 200, 100) : QColor(255, 68, 68));
    m_historyTable->setItem(row, 5, statusItem);
    
    // Limit history display to 20 rows
    while (m_historyTable->rowCount() > 20) {
        m_historyTable->removeRow(m_historyTable->rowCount() - 1);
    }
}

QString NetworkSpeedTestDialog::formatSpeed(double mbps)
{
    if (mbps >= 1000.0) {
        return QString("%1 Gbps").arg(mbps / 1000.0, 0, 'f', 2);
    } else if (mbps >= 100.0) {
        return QString("%1 Mbps").arg(mbps, 0, 'f', 0);
    } else if (mbps >= 10.0) {
        return QString("%1 Mbps").arg(mbps, 0, 'f', 1);
    } else {
        return QString("%1 Mbps").arg(mbps, 0, 'f', 2);
    }
}
