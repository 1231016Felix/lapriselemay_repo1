#include "storagehealthdialog.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QGridLayout>
#include <QGroupBox>
#include <QScrollArea>
#include <QHeaderView>
#include <QCheckBox>
#include <QFileDialog>
#include <QFile>
#include <QMessageBox>
#include <QTextStream>
#include <QApplication>
#include <QStyle>
#include <QDateTime>
#include <QSplitter>
#include <QDebug>

// ============================================================================
// DiskHealthCard Implementation
// ============================================================================

DiskHealthCard::DiskHealthCard(QWidget *parent)
    : QFrame(parent)
{
    setupUi();
}

void DiskHealthCard::setupUi()
{
    setFrameStyle(QFrame::StyledPanel | QFrame::Raised);
    setStyleSheet(R"(
        DiskHealthCard {
            background: palette(base);
            border-radius: 8px;
            padding: 10px;
        }
        DiskHealthCard:hover {
            background: palette(alternate-base);
        }
    )");
    
    auto layout = new QHBoxLayout(this);
    layout->setSpacing(15);
    
    // Icon
    m_iconLabel = new QLabel();
    m_iconLabel->setFixedSize(48, 48);
    m_iconLabel->setAlignment(Qt::AlignCenter);
    layout->addWidget(m_iconLabel);
    
    // Info column
    auto infoLayout = new QVBoxLayout();
    infoLayout->setSpacing(2);
    
    m_modelLabel = new QLabel();
    m_modelLabel->setStyleSheet("font-weight: bold; font-size: 12px;");
    infoLayout->addWidget(m_modelLabel);
    
    auto typeCapLayout = new QHBoxLayout();
    m_typeLabel = new QLabel();
    m_typeLabel->setStyleSheet("color: gray;");
    typeCapLayout->addWidget(m_typeLabel);
    m_capacityLabel = new QLabel();
    m_capacityLabel->setStyleSheet("color: gray;");
    typeCapLayout->addWidget(m_capacityLabel);
    typeCapLayout->addStretch();
    infoLayout->addLayout(typeCapLayout);
    
    layout->addLayout(infoLayout, 1);
    
    // Health column
    auto healthLayout = new QVBoxLayout();
    healthLayout->setAlignment(Qt::AlignCenter);
    
    m_healthLabel = new QLabel();
    m_healthLabel->setStyleSheet("font-weight: bold; font-size: 14px;");
    m_healthLabel->setAlignment(Qt::AlignCenter);
    healthLayout->addWidget(m_healthLabel);
    
    m_healthBar = new QProgressBar();
    m_healthBar->setRange(0, 100);
    m_healthBar->setFixedWidth(100);
    m_healthBar->setFixedHeight(10);
    m_healthBar->setTextVisible(false);
    healthLayout->addWidget(m_healthBar);
    
    layout->addLayout(healthLayout);
    
    // Temperature
    m_temperatureLabel = new QLabel();
    m_temperatureLabel->setStyleSheet("font-size: 14px;");
    m_temperatureLabel->setAlignment(Qt::AlignCenter);
    m_temperatureLabel->setFixedWidth(60);
    layout->addWidget(m_temperatureLabel);
    
    // Details button
    m_detailsButton = new QPushButton(tr("Details"));
    m_detailsButton->setFixedWidth(70);
    connect(m_detailsButton, &QPushButton::clicked, this, [this]() {
        emit detailsRequested(m_devicePath);
    });
    layout->addWidget(m_detailsButton);
}

void DiskHealthCard::setDiskInfo(const DiskHealthInfo& info)
{
    m_devicePath = info.devicePath;
    
    // Set icon based on type
    QString iconText = info.isSsd ? "💾" : "💿";
    if (info.isNvme) iconText = "⚡";
    m_iconLabel->setText(iconText);
    m_iconLabel->setStyleSheet("font-size: 32px;");
    
    // Model name
    m_modelLabel->setText(info.model.isEmpty() ? tr("Unknown Disk") : info.model);
    
    // Type
    QString type = info.isSsd ? (info.isNvme ? "NVMe SSD" : "SATA SSD") : "HDD";
    m_typeLabel->setText(type);
    
    // Capacity
    m_capacityLabel->setText(QString(" | %1").arg(info.totalFormatted));
    
    // Health
    updateHealthIndicator(info.healthStatus, info.healthPercent);
    
    // Temperature
    if (info.temperatureCelsius > 0) {
        m_temperatureLabel->setText(QString("🌡 %1°C").arg(info.temperatureCelsius));
        if (info.temperatureCelsius > 60) {
            m_temperatureLabel->setStyleSheet("font-size: 14px; color: #ff6600;");
        } else if (info.temperatureCelsius > 50) {
            m_temperatureLabel->setStyleSheet("font-size: 14px; color: #ffaa00;");
        } else {
            m_temperatureLabel->setStyleSheet("font-size: 14px; color: #00aa00;");
        }
    } else {
        m_temperatureLabel->setText("🌡 N/A");
        m_temperatureLabel->setStyleSheet("font-size: 14px; color: gray;");
    }
}

void DiskHealthCard::updateHealthIndicator(DriveHealthStatus status, int percent)
{
    QString statusText = StorageHealthMonitor::healthStatusToString(status);
    QString color = StorageHealthMonitor::healthStatusColor(status);
    
    if (percent >= 0) {
        m_healthLabel->setText(QString("%1%").arg(percent));
    } else {
        m_healthLabel->setText("?");
    }
    m_healthLabel->setStyleSheet(QString("font-weight: bold; font-size: 14px; color: %1;").arg(color));
    
    m_healthBar->setValue(percent >= 0 ? percent : 0);
    m_healthBar->setStyleSheet(QString(R"(
        QProgressBar {
            border: 1px solid gray;
            border-radius: 3px;
            background: palette(base);
        }
        QProgressBar::chunk {
            background: %1;
            border-radius: 2px;
        }
    )").arg(color));
}

// ============================================================================
// DiskDetailWidget Implementation
// ============================================================================

DiskDetailWidget::DiskDetailWidget(QWidget *parent)
    : QWidget(parent)
{
    setupUi();
}

void DiskDetailWidget::setupUi()
{
    auto mainLayout = new QVBoxLayout(this);
    mainLayout->setSpacing(8);
    mainLayout->setContentsMargins(8, 8, 8, 8);
    
    // === Drive Information ===
    auto infoGroup = new QGroupBox(tr("Drive Information"));
    auto infoLayout = new QGridLayout(infoGroup);
    infoLayout->setSpacing(4);
    
    infoLayout->addWidget(new QLabel(tr("Model:")), 0, 0);
    m_modelLabel = new QLabel("-");
    m_modelLabel->setStyleSheet("font-weight: bold;");
    m_modelLabel->setTextInteractionFlags(Qt::TextSelectableByMouse);
    infoLayout->addWidget(m_modelLabel, 0, 1);
    
    infoLayout->addWidget(new QLabel(tr("Serial:")), 0, 2);
    m_serialLabel = new QLabel("-");
    m_serialLabel->setTextInteractionFlags(Qt::TextSelectableByMouse);
    infoLayout->addWidget(m_serialLabel, 0, 3);
    
    infoLayout->addWidget(new QLabel(tr("Firmware:")), 1, 0);
    m_firmwareLabel = new QLabel("-");
    infoLayout->addWidget(m_firmwareLabel, 1, 1);
    
    infoLayout->addWidget(new QLabel(tr("Interface:")), 1, 2);
    m_interfaceLabel = new QLabel("-");
    infoLayout->addWidget(m_interfaceLabel, 1, 3);
    
    infoLayout->addWidget(new QLabel(tr("Capacity:")), 2, 0);
    m_capacityLabel = new QLabel("-");
    infoLayout->addWidget(m_capacityLabel, 2, 1);
    
    mainLayout->addWidget(infoGroup);
    
    // === Health Status ===
    auto healthGroup = new QGroupBox(tr("Health Status"));
    auto healthLayout = new QVBoxLayout(healthGroup);
    
    auto healthTopLayout = new QHBoxLayout();
    
    m_healthPercentLabel = new QLabel("---%");
    m_healthPercentLabel->setStyleSheet("font-size: 36px; font-weight: bold;");
    healthTopLayout->addWidget(m_healthPercentLabel);
    
    auto healthInfoLayout = new QVBoxLayout();
    m_healthStatusLabel = new QLabel("-");
    m_healthStatusLabel->setStyleSheet("font-size: 16px; font-weight: bold;");
    healthInfoLayout->addWidget(m_healthStatusLabel);
    m_healthDescLabel = new QLabel("-");
    m_healthDescLabel->setWordWrap(true);
    healthInfoLayout->addWidget(m_healthDescLabel);
    healthTopLayout->addLayout(healthInfoLayout, 1);
    
    healthLayout->addLayout(healthTopLayout);
    
    m_healthBar = new QProgressBar();
    m_healthBar->setRange(0, 100);
    m_healthBar->setMinimumHeight(20);
    healthLayout->addWidget(m_healthBar);
    
    // Temperature and Power info
    auto statsLayout = new QHBoxLayout();
    
    auto tempBox = new QGroupBox(tr("Temperature"));
    auto tempLayout = new QVBoxLayout(tempBox);
    m_tempLabel = new QLabel("--°C");
    m_tempLabel->setStyleSheet("font-size: 24px; font-weight: bold;");
    m_tempLabel->setAlignment(Qt::AlignCenter);
    tempLayout->addWidget(m_tempLabel);
    m_tempStatusLabel = new QLabel("-");
    m_tempStatusLabel->setAlignment(Qt::AlignCenter);
    tempLayout->addWidget(m_tempStatusLabel);
    statsLayout->addWidget(tempBox);
    
    auto powerBox = new QGroupBox(tr("Power Statistics"));
    auto powerLayout = new QGridLayout(powerBox);
    powerLayout->addWidget(new QLabel(tr("Power-On Hours:")), 0, 0);
    m_powerOnHoursLabel = new QLabel("-");
    m_powerOnHoursLabel->setStyleSheet("font-weight: bold;");
    powerLayout->addWidget(m_powerOnHoursLabel, 0, 1);
    powerLayout->addWidget(new QLabel(tr("Power Cycles:")), 1, 0);
    m_powerCyclesLabel = new QLabel("-");
    m_powerCyclesLabel->setStyleSheet("font-weight: bold;");
    powerLayout->addWidget(m_powerCyclesLabel, 1, 1);
    powerLayout->addWidget(new QLabel(tr("Est. Life Remaining:")), 2, 0);
    m_lifeRemainingLabel = new QLabel("-");
    m_lifeRemainingLabel->setStyleSheet("font-weight: bold;");
    powerLayout->addWidget(m_lifeRemainingLabel, 2, 1);
    statsLayout->addWidget(powerBox);
    
    healthLayout->addLayout(statsLayout);
    mainLayout->addWidget(healthGroup);
    
    // === NVMe Specific ===
    m_nvmeGroup = new QGroupBox(tr("NVMe Health Info"));
    auto nvmeLayout = new QGridLayout(m_nvmeGroup);
    
    nvmeLayout->addWidget(new QLabel(tr("Available Spare:")), 0, 0);
    m_nvmeSpareLabel = new QLabel("-");
    nvmeLayout->addWidget(m_nvmeSpareLabel, 0, 1);
    
    nvmeLayout->addWidget(new QLabel(tr("Percentage Used:")), 0, 2);
    m_nvmeUsedLabel = new QLabel("-");
    nvmeLayout->addWidget(m_nvmeUsedLabel, 0, 3);
    
    nvmeLayout->addWidget(new QLabel(tr("Data Written:")), 1, 0);
    m_nvmeWrittenLabel = new QLabel("-");
    nvmeLayout->addWidget(m_nvmeWrittenLabel, 1, 1);
    
    nvmeLayout->addWidget(new QLabel(tr("Data Read:")), 1, 2);
    m_nvmeReadLabel = new QLabel("-");
    nvmeLayout->addWidget(m_nvmeReadLabel, 1, 3);
    
    nvmeLayout->addWidget(new QLabel(tr("Media Errors:")), 2, 0);
    m_nvmeErrorsLabel = new QLabel("-");
    nvmeLayout->addWidget(m_nvmeErrorsLabel, 2, 1);
    
    nvmeLayout->addWidget(new QLabel(tr("Unsafe Shutdowns:")), 2, 2);
    m_nvmeShutdownsLabel = new QLabel("-");
    nvmeLayout->addWidget(m_nvmeShutdownsLabel, 2, 3);
    
    m_nvmeGroup->setVisible(false);
    mainLayout->addWidget(m_nvmeGroup);
    
    // === SMART Attributes Table ===
    auto smartGroup = new QGroupBox(tr("S.M.A.R.T. Attributes"));
    auto smartLayout = new QVBoxLayout(smartGroup);
    
    m_smartModel = std::make_unique<SmartAttributeModel>(this);
    m_smartTable = new QTableView();
    m_smartTable->setModel(m_smartModel.get());
    m_smartTable->setAlternatingRowColors(true);
    m_smartTable->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_smartTable->horizontalHeader()->setStretchLastSection(true);
    m_smartTable->verticalHeader()->setVisible(false);
    m_smartTable->setMinimumHeight(100);
    m_smartTable->setSizePolicy(QSizePolicy::Expanding, QSizePolicy::MinimumExpanding);
    smartLayout->addWidget(m_smartTable);
    
    smartGroup->setSizePolicy(QSizePolicy::Expanding, QSizePolicy::MinimumExpanding);
    mainLayout->addWidget(smartGroup, 1);  // Give it stretch factor
    
    // === Alerts ===
    m_alertsLabel = new QLabel();
    m_alertsLabel->setWordWrap(true);
    m_alertsLabel->setVisible(false);
    m_alertsLabel->setSizePolicy(QSizePolicy::Preferred, QSizePolicy::Minimum);
    mainLayout->addWidget(m_alertsLabel);
    
    // Ensure the widget expands properly in the scroll area
    setSizePolicy(QSizePolicy::Preferred, QSizePolicy::MinimumExpanding);
    
    // Add a spacer at the end to prevent excessive stretching
    mainLayout->addStretch(0);
}

void DiskDetailWidget::setDiskInfo(const DiskHealthInfo& info)
{
    // Basic info
    m_modelLabel->setText(info.model.isEmpty() ? tr("Unknown") : info.model);
    m_serialLabel->setText(info.serialNumber.isEmpty() ? tr("N/A") : info.serialNumber);
    m_firmwareLabel->setText(info.firmwareVersion.isEmpty() ? tr("N/A") : info.firmwareVersion);
    m_interfaceLabel->setText(info.interfaceType);
    m_capacityLabel->setText(info.totalFormatted);
    
    // Health status
    QString healthColor = StorageHealthMonitor::healthStatusColor(info.healthStatus);
    
    if (info.healthPercent >= 0) {
        m_healthPercentLabel->setText(QString("%1%").arg(info.healthPercent));
    } else {
        m_healthPercentLabel->setText("N/A");
    }
    m_healthPercentLabel->setStyleSheet(QString("font-size: 36px; font-weight: bold; color: %1;").arg(healthColor));
    
    m_healthStatusLabel->setText(StorageHealthMonitor::healthStatusToString(info.healthStatus));
    m_healthStatusLabel->setStyleSheet(QString("font-size: 16px; font-weight: bold; color: %1;").arg(healthColor));
    
    m_healthDescLabel->setText(info.healthDescription);
    
    m_healthBar->setValue(info.healthPercent >= 0 ? info.healthPercent : 0);
    m_healthBar->setStyleSheet(QString(R"(
        QProgressBar {
            border: 1px solid gray;
            border-radius: 5px;
            text-align: center;
        }
        QProgressBar::chunk {
            background: %1;
            border-radius: 4px;
        }
    )").arg(healthColor));
    m_healthBar->setFormat(QString("%1% - %2").arg(info.healthPercent)
        .arg(StorageHealthMonitor::healthStatusToString(info.healthStatus)));
    
    // Temperature
    if (info.temperatureCelsius > 0) {
        m_tempLabel->setText(QString("%1°C").arg(info.temperatureCelsius));
        QString tempColor;
        QString tempStatus;
        if (info.temperatureCelsius > 70) {
            tempColor = "#ff0000";
            tempStatus = tr("Critical - Too hot!");
        } else if (info.temperatureCelsius > 60) {
            tempColor = "#ff6600";
            tempStatus = tr("Warning - High");
        } else if (info.temperatureCelsius > 50) {
            tempColor = "#ffaa00";
            tempStatus = tr("Elevated");
        } else {
            tempColor = "#00aa00";
            tempStatus = tr("Normal");
        }
        m_tempLabel->setStyleSheet(QString("font-size: 24px; font-weight: bold; color: %1;").arg(tempColor));
        m_tempStatusLabel->setText(tempStatus);
        m_tempStatusLabel->setStyleSheet(QString("color: %1;").arg(tempColor));
    } else {
        m_tempLabel->setText("N/A");
        m_tempLabel->setStyleSheet("font-size: 24px; font-weight: bold; color: gray;");
        m_tempStatusLabel->setText(tr("Not available"));
    }
    
    // Power stats
    if (info.powerOnHours > 0) {
        uint64_t days = info.powerOnHours / 24;
        uint64_t years = days / 365;
        if (years > 0) {
            m_powerOnHoursLabel->setText(QString("%1 hours (%2 years, %3 days)")
                .arg(info.powerOnHours).arg(years).arg(days % 365));
        } else {
            m_powerOnHoursLabel->setText(QString("%1 hours (%2 days)")
                .arg(info.powerOnHours).arg(days));
        }
    } else {
        m_powerOnHoursLabel->setText("N/A");
    }
    
    m_powerCyclesLabel->setText(info.powerCycles > 0 ? QString::number(info.powerCycles) : "N/A");
    m_lifeRemainingLabel->setText(info.estimatedLifeDescription);
    
    // NVMe specific section
    if (info.isNvme && info.nvmeHealth.isValid) {
        updateNvmeSection(info);
        m_nvmeGroup->setVisible(true);
    } else {
        m_nvmeGroup->setVisible(false);
    }
    
    // SMART attributes
    updateSmartTable(info);
    
    // Alerts
    if (!info.criticalAlerts.isEmpty() || !info.warnings.isEmpty()) {
        QString alertText;
        if (!info.criticalAlerts.isEmpty()) {
            alertText += "<p style='color: #ff0000; font-weight: bold;'>⚠️ " + 
                         tr("Critical Alerts:") + "</p><ul>";
            for (const auto& alert : info.criticalAlerts) {
                alertText += "<li style='color: #ff0000;'>" + alert + "</li>";
            }
            alertText += "</ul>";
        }
        if (!info.warnings.isEmpty()) {
            alertText += "<p style='color: #ff8c00; font-weight: bold;'>⚠️ " + 
                         tr("Warnings:") + "</p><ul>";
            for (const auto& warn : info.warnings) {
                alertText += "<li style='color: #ff8c00;'>" + warn + "</li>";
            }
            alertText += "</ul>";
        }
        m_alertsLabel->setText(alertText);
        m_alertsLabel->setVisible(true);
    } else {
        m_alertsLabel->setVisible(false);
    }
}

void DiskDetailWidget::updateNvmeSection(const DiskHealthInfo& info)
{
    const auto& nvme = info.nvmeHealth;
    
    m_nvmeSpareLabel->setText(QString("%1%").arg(nvme.availableSpare));
    if (nvme.availableSpare < nvme.availableSpareThreshold) {
        m_nvmeSpareLabel->setStyleSheet("font-weight: bold; color: #ff0000;");
    } else {
        m_nvmeSpareLabel->setStyleSheet("font-weight: bold; color: #00aa00;");
    }
    
    m_nvmeUsedLabel->setText(QString("%1%").arg(nvme.percentageUsed));
    if (nvme.percentageUsed > 90) {
        m_nvmeUsedLabel->setStyleSheet("font-weight: bold; color: #ff0000;");
    } else if (nvme.percentageUsed > 70) {
        m_nvmeUsedLabel->setStyleSheet("font-weight: bold; color: #ff8c00;");
    } else {
        m_nvmeUsedLabel->setStyleSheet("font-weight: bold;");
    }
    
    // Data units are in 512KB units according to NVMe spec
    m_nvmeWrittenLabel->setText(StorageHealthMonitor::formatBytes(nvme.dataUnitsWritten * 512000));
    m_nvmeReadLabel->setText(StorageHealthMonitor::formatBytes(nvme.dataUnitsRead * 512000));
    
    m_nvmeErrorsLabel->setText(QString::number(nvme.mediaErrors));
    if (nvme.mediaErrors > 0) {
        m_nvmeErrorsLabel->setStyleSheet("font-weight: bold; color: #ff0000;");
    }
    
    m_nvmeShutdownsLabel->setText(QString::number(nvme.unsafeShutdowns));
}

void DiskDetailWidget::updateSmartTable(const DiskHealthInfo& info)
{
    m_smartModel->setAttributes(info.smartAttributes);
    
    // Resize columns to content
    m_smartTable->resizeColumnsToContents();
    
    // Ensure the table is properly updated
    m_smartTable->update();
    
    // If no attributes, show a message
    if (info.smartAttributes.empty()) {
        qDebug() << "No SMART attributes available for disk:" << info.model;
    }
}

void DiskDetailWidget::clear()
{
    m_modelLabel->setText("-");
    m_serialLabel->setText("-");
    m_firmwareLabel->setText("-");
    m_interfaceLabel->setText("-");
    m_capacityLabel->setText("-");
    m_healthPercentLabel->setText("---%");
    m_healthStatusLabel->setText("-");
    m_healthDescLabel->setText("-");
    m_healthBar->setValue(0);
    m_tempLabel->setText("--°C");
    m_tempStatusLabel->setText("-");
    m_powerOnHoursLabel->setText("-");
    m_powerCyclesLabel->setText("-");
    m_lifeRemainingLabel->setText("-");
    m_nvmeGroup->setVisible(false);
    m_smartModel->clear();
    m_alertsLabel->setVisible(false);
}

// ============================================================================
// StorageHealthDialog Implementation
// ============================================================================

StorageHealthDialog::StorageHealthDialog(QWidget *parent)
    : QDialog(parent)
{
    setWindowTitle(tr("Storage Health Monitor"));
    setMinimumSize(600, 350);
    resize(750, 480);
    
    m_monitor = std::make_unique<StorageHealthMonitor>(this);
    
    setupUi();
    
    // Connect signals
    connect(m_monitor.get(), &StorageHealthMonitor::updated, this, &StorageHealthDialog::updateDiskCards);
    connect(m_monitor.get(), &StorageHealthMonitor::diskHealthWarning, this, 
            [this](const QString& model, const QString& warning) {
        // Could show notification here
        qDebug() << "Warning for" << model << ":" << warning;
    });
    connect(m_monitor.get(), &StorageHealthMonitor::diskHealthCritical, this,
            [this](const QString& model, const QString& alert) {
        QMessageBox::critical(this, tr("Critical Disk Alert"),
            tr("Critical issue detected on %1:\n\n%2").arg(model, alert));
    });
    
    // Initial refresh
    refreshData();
    
    // Setup auto-refresh timer
    m_refreshTimer = new QTimer(this);
    connect(m_refreshTimer, &QTimer::timeout, this, &StorageHealthDialog::refreshData);
}

StorageHealthDialog::~StorageHealthDialog()
{
    if (m_refreshTimer->isActive()) {
        m_refreshTimer->stop();
    }
}

void StorageHealthDialog::setupUi()
{
    auto mainLayout = new QVBoxLayout(this);
    
    // Admin warning
    if (!StorageHealthMonitor::isAdmin()) {
        m_adminWarningLabel = new QLabel(tr("⚠️ Running without administrator privileges. "
                                            "Some SMART data may not be available."));
        m_adminWarningLabel->setStyleSheet("background: #fff3cd; color: #856404; padding: 8px; "
                                           "border-radius: 4px; border: 1px solid #ffeaa7;");
        m_adminWarningLabel->setWordWrap(true);
        mainLayout->addWidget(m_adminWarningLabel);
    }
    
    // Toolbar
    auto toolbar = new QHBoxLayout();
    
    m_refreshButton = new QPushButton(tr("🔄 Refresh"));
    connect(m_refreshButton, &QPushButton::clicked, this, &StorageHealthDialog::refreshData);
    toolbar->addWidget(m_refreshButton);
    
    m_autoRefreshCheck = new QCheckBox(tr("Auto-refresh (30s)"));
    connect(m_autoRefreshCheck, &QCheckBox::toggled, this, &StorageHealthDialog::onAutoRefreshToggled);
    toolbar->addWidget(m_autoRefreshCheck);
    
    toolbar->addStretch();
    
    m_lastUpdateLabel = new QLabel();
    m_lastUpdateLabel->setStyleSheet("color: gray;");
    toolbar->addWidget(m_lastUpdateLabel);
    
    toolbar->addSpacing(20);
    
    m_exportButton = new QPushButton(tr("📄 Export Report"));
    connect(m_exportButton, &QPushButton::clicked, this, &StorageHealthDialog::exportReport);
    toolbar->addWidget(m_exportButton);
    
    mainLayout->addLayout(toolbar);
    
    // Main content with splitter
    auto splitter = new QSplitter(Qt::Horizontal);
    
    // Left side - Disk cards
    auto leftWidget = new QWidget();
    auto leftLayout = new QVBoxLayout(leftWidget);
    leftLayout->setContentsMargins(0, 0, 0, 0);
    
    auto cardsScroll = new QScrollArea();
    cardsScroll->setWidgetResizable(true);
    cardsScroll->setHorizontalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
    cardsScroll->setMinimumWidth(220);
    cardsScroll->setMaximumWidth(280);
    
    m_cardsContainer = new QWidget();
    m_cardsLayout = new QVBoxLayout(m_cardsContainer);
    m_cardsLayout->setSpacing(10);
    m_cardsLayout->addStretch();
    
    cardsScroll->setWidget(m_cardsContainer);
    leftLayout->addWidget(cardsScroll);
    
    splitter->addWidget(leftWidget);
    
    // Right side - Detail view
    auto detailScroll = new QScrollArea();
    detailScroll->setWidgetResizable(true);
    detailScroll->setHorizontalScrollBarPolicy(Qt::ScrollBarAsNeeded);
    detailScroll->setVerticalScrollBarPolicy(Qt::ScrollBarAsNeeded);
    detailScroll->setMinimumWidth(300);
    
    m_detailWidget = new DiskDetailWidget();
    m_detailWidget->setMinimumSize(280, 350);
    detailScroll->setWidget(m_detailWidget);
    
    splitter->addWidget(detailScroll);
    splitter->setStretchFactor(0, 0);
    splitter->setStretchFactor(1, 1);
    
    mainLayout->addWidget(splitter, 1);
    
    // Close button
    auto buttonLayout = new QHBoxLayout();
    buttonLayout->addStretch();
    auto closeButton = new QPushButton(tr("Close"));
    connect(closeButton, &QPushButton::clicked, this, &QDialog::accept);
    buttonLayout->addWidget(closeButton);
    mainLayout->addLayout(buttonLayout);
}

void StorageHealthDialog::refreshData()
{
    m_refreshButton->setEnabled(false);
    m_refreshButton->setText(tr("🔄 Scanning..."));
    QApplication::processEvents();
    
    m_monitor->update();
    
    m_refreshButton->setEnabled(true);
    m_refreshButton->setText(tr("🔄 Refresh"));
    m_lastUpdateLabel->setText(tr("Last updated: %1")
        .arg(QDateTime::currentDateTime().toString("hh:mm:ss")));
}

void StorageHealthDialog::updateDiskCards()
{
    // Clear existing cards
    for (auto* card : m_diskCards) {
        m_cardsLayout->removeWidget(card);
        delete card;
    }
    m_diskCards.clear();
    
    // Create new cards
    const auto& disks = m_monitor->disks();
    qDebug() << "StorageHealthDialog: Found" << disks.size() << "disks";
    
    for (const auto& disk : disks) {
        qDebug() << "  - Disk:" << disk.model << "Path:" << disk.devicePath;
        auto* card = new DiskHealthCard();
        card->setDiskInfo(disk);
        connect(card, &DiskHealthCard::detailsRequested, this, &StorageHealthDialog::showDiskDetails);
        
        // Insert before the stretch
        m_cardsLayout->insertWidget(m_cardsLayout->count() - 1, card);
        m_diskCards.push_back(card);
    }
    
    // Show first disk details by default
    if (!disks.empty()) {
        showDiskDetails(disks[0].devicePath);
    } else {
        m_detailWidget->clear();
    }
}

void StorageHealthDialog::showDiskDetails(const QString& devicePath)
{
    const auto* disk = m_monitor->getDiskInfo(devicePath);
    if (disk) {
        m_detailWidget->setDiskInfo(*disk);
    }
    
    // Update card selection visual
    for (auto* card : m_diskCards) {
        if (card->devicePath() == devicePath) {
            card->setStyleSheet(R"(
                DiskHealthCard {
                    background: palette(highlight);
                    border-radius: 8px;
                    padding: 10px;
                }
            )");
        } else {
            card->setStyleSheet(R"(
                DiskHealthCard {
                    background: palette(base);
                    border-radius: 8px;
                    padding: 10px;
                }
                DiskHealthCard:hover {
                    background: palette(alternate-base);
                }
            )");
        }
    }
}

void StorageHealthDialog::onAutoRefreshToggled(bool enabled)
{
    if (enabled) {
        m_refreshTimer->start(30000); // 30 seconds
    } else {
        m_refreshTimer->stop();
    }
}

void StorageHealthDialog::onDiskSelected(int index)
{
    Q_UNUSED(index);
}

void StorageHealthDialog::exportReport()
{
    QString filename = QFileDialog::getSaveFileName(this,
        tr("Export Health Report"),
        QString("disk_health_report_%1.txt")
            .arg(QDateTime::currentDateTime().toString("yyyyMMdd_HHmmss")),
        tr("Text Files (*.txt);;HTML Files (*.html)"));
    
    if (filename.isEmpty()) return;
    
    QFile file(filename);
    if (!file.open(QIODevice::WriteOnly | QIODevice::Text)) {
        QMessageBox::warning(this, tr("Error"),
            tr("Could not save file: %1").arg(file.errorString()));
        return;
    }
    
    QTextStream out(&file);
    
    bool isHtml = filename.endsWith(".html", Qt::CaseInsensitive);
    
    if (isHtml) {
        out << "<!DOCTYPE html><html><head><meta charset='utf-8'>"
            << "<title>Disk Health Report</title>"
            << "<style>body{font-family:sans-serif;margin:20px;}"
            << "h1{color:#333;}h2{color:#666;border-bottom:1px solid #ccc;}"
            << "table{border-collapse:collapse;width:100%;margin:10px 0;}"
            << "th,td{border:1px solid #ddd;padding:8px;text-align:left;}"
            << "th{background:#f5f5f5;}.good{color:green;}.warning{color:orange;}.critical{color:red;}"
            << "</style></head><body>";
        out << "<h1>Storage Health Report</h1>";
        out << "<p>Generated: " << QDateTime::currentDateTime().toString() << "</p>";
    } else {
        out << "=== STORAGE HEALTH REPORT ===\n";
        out << "Generated: " << QDateTime::currentDateTime().toString() << "\n\n";
    }
    
    for (const auto& disk : m_monitor->disks()) {
        if (isHtml) {
            out << "<h2>" << disk.model << "</h2>";
            out << "<table>";
            out << "<tr><th>Property</th><th>Value</th></tr>";
            out << "<tr><td>Device Path</td><td>" << disk.devicePath << "</td></tr>";
            out << "<tr><td>Serial Number</td><td>" << disk.serialNumber << "</td></tr>";
            out << "<tr><td>Firmware</td><td>" << disk.firmwareVersion << "</td></tr>";
            out << "<tr><td>Interface</td><td>" << disk.interfaceType << "</td></tr>";
            out << "<tr><td>Capacity</td><td>" << disk.totalFormatted << "</td></tr>";
            out << "<tr><td>Type</td><td>" << (disk.isSsd ? (disk.isNvme ? "NVMe SSD" : "SATA SSD") : "HDD") << "</td></tr>";
            
            QString healthClass = disk.healthPercent >= 70 ? "good" : (disk.healthPercent >= 50 ? "warning" : "critical");
            out << "<tr><td>Health</td><td class='" << healthClass << "'>" 
                << disk.healthPercent << "% - " 
                << StorageHealthMonitor::healthStatusToString(disk.healthStatus) << "</td></tr>";
            
            if (disk.temperatureCelsius > 0) {
                QString tempClass = disk.temperatureCelsius > 60 ? "critical" : (disk.temperatureCelsius > 50 ? "warning" : "good");
                out << "<tr><td>Temperature</td><td class='" << tempClass << "'>" 
                    << disk.temperatureCelsius << "°C</td></tr>";
            }
            
            out << "<tr><td>Power-On Hours</td><td>" << disk.powerOnHours << "</td></tr>";
            out << "<tr><td>Power Cycles</td><td>" << disk.powerCycles << "</td></tr>";
            out << "</table>";
            
            // SMART attributes
            if (!disk.smartAttributes.empty()) {
                out << "<h3>S.M.A.R.T. Attributes</h3><table>";
                out << "<tr><th>ID</th><th>Attribute</th><th>Current</th><th>Worst</th><th>Threshold</th><th>Raw</th></tr>";
                for (const auto& attr : disk.smartAttributes) {
                    QString rowClass = !attr.isOk ? "critical" : (attr.isCritical && attr.rawValue > 0 ? "warning" : "");
                    out << "<tr class='" << rowClass << "'>";
                    out << "<td>0x" << QString::number(attr.id, 16).toUpper() << "</td>";
                    out << "<td>" << attr.name << "</td>";
                    out << "<td>" << attr.currentValue << "</td>";
                    out << "<td>" << attr.worstValue << "</td>";
                    out << "<td>" << attr.threshold << "</td>";
                    out << "<td>" << attr.rawValueString << "</td>";
                    out << "</tr>";
                }
                out << "</table>";
            }
        } else {
            out << "--- " << disk.model << " ---\n";
            out << "Device: " << disk.devicePath << "\n";
            out << "Serial: " << disk.serialNumber << "\n";
            out << "Firmware: " << disk.firmwareVersion << "\n";
            out << "Interface: " << disk.interfaceType << "\n";
            out << "Capacity: " << disk.totalFormatted << "\n";
            out << "Type: " << (disk.isSsd ? (disk.isNvme ? "NVMe SSD" : "SATA SSD") : "HDD") << "\n";
            out << "Health: " << disk.healthPercent << "% - " 
                << StorageHealthMonitor::healthStatusToString(disk.healthStatus) << "\n";
            if (disk.temperatureCelsius > 0) {
                out << "Temperature: " << disk.temperatureCelsius << "°C\n";
            }
            out << "Power-On Hours: " << disk.powerOnHours << "\n";
            out << "Power Cycles: " << disk.powerCycles << "\n";
            out << "\nS.M.A.R.T. Attributes:\n";
            for (const auto& attr : disk.smartAttributes) {
                out << QString("  [%1] %2: %3 (worst: %4, threshold: %5) Raw: %6\n")
                    .arg(attr.id, 3, 16, QChar('0')).toUpper()
                    .arg(attr.name, -30)
                    .arg(attr.currentValue, 3)
                    .arg(attr.worstValue, 3)
                    .arg(attr.threshold, 3)
                    .arg(attr.rawValueString);
            }
            out << "\n";
        }
    }
    
    if (isHtml) {
        out << "</body></html>";
    }
    
    file.close();
    
    QMessageBox::information(this, tr("Report Exported"),
        tr("Health report saved to:\n%1").arg(filename));
}
