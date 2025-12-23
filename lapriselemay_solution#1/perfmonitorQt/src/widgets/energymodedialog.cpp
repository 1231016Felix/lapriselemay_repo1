#include "energymodedialog.h"
#include "../utils/energymode.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QHeaderView>
#include <QCheckBox>
#include <QMessageBox>
#include <QGroupBox>

EnergyModeDialog::EnergyModeDialog(EnergyModeManager* manager, QWidget *parent)
    : QDialog(parent)
    , m_manager(manager)
{
    setWindowTitle(tr("Mode Énergie"));
    setMinimumSize(600, 500);
    
    setupUi();
    populateServiceList();
    updateUI();
    
    // Connect signals
    connect(m_manager, &EnergyModeManager::statusMessageChanged, 
            this, &EnergyModeDialog::updateStatus);
    connect(m_manager, &EnergyModeManager::progressChanged, 
            this, &EnergyModeDialog::updateProgress);
    connect(m_manager, &EnergyModeManager::activationChanged, 
            this, [this]() { updateUI(); });
}

void EnergyModeDialog::setupUi()
{
    auto layout = new QVBoxLayout(this);
    layout->setSpacing(15);
    
    // Header with status
    auto headerGroup = new QGroupBox(tr("État du Mode Énergie"));
    auto headerLayout = new QVBoxLayout(headerGroup);
    
    auto statusLayout = new QHBoxLayout();
    m_statusLabel = new QLabel(tr("Inactif"));
    m_statusLabel->setStyleSheet("font-weight: bold; font-size: 14px;");
    statusLayout->addWidget(m_statusLabel);
    statusLayout->addStretch();
    
    m_toggleButton = new QPushButton(tr("Activer"));
    m_toggleButton->setMinimumWidth(120);
    m_toggleButton->setStyleSheet(R"(
        QPushButton {
            background-color: #00aa00;
            color: white;
            font-weight: bold;
            padding: 8px 16px;
            border-radius: 4px;
        }
        QPushButton:hover {
            background-color: #00cc00;
        }
    )");
    connect(m_toggleButton, &QPushButton::clicked, this, &EnergyModeDialog::onToggleClicked);
    statusLayout->addWidget(m_toggleButton);
    
    headerLayout->addLayout(statusLayout);
    
    m_estimateLabel = new QLabel();
    m_estimateLabel->setStyleSheet("color: #888;");
    headerLayout->addWidget(m_estimateLabel);
    
    m_progressBar = new QProgressBar();
    m_progressBar->setVisible(false);
    m_progressBar->setTextVisible(true);
    headerLayout->addWidget(m_progressBar);
    
    layout->addWidget(headerGroup);
    
    // Service list
    auto serviceGroup = new QGroupBox(tr("Services à désactiver"));
    auto serviceLayout = new QVBoxLayout(serviceGroup);
    
    auto infoLabel = new QLabel(tr(
        "Sélectionnez les services Windows à arrêter en Mode Énergie.\n"
        "Les services seront automatiquement restaurés à la désactivation."));
    infoLabel->setStyleSheet("color: #666; margin-bottom: 10px;");
    infoLabel->setWordWrap(true);
    serviceLayout->addWidget(infoLabel);
    
    m_serviceTable = new QTableWidget();
    m_serviceTable->setColumnCount(3);
    m_serviceTable->setHorizontalHeaderLabels({tr("Actif"), tr("Service"), tr("Description")});
    m_serviceTable->horizontalHeader()->setSectionResizeMode(0, QHeaderView::ResizeToContents);
    m_serviceTable->horizontalHeader()->setSectionResizeMode(1, QHeaderView::ResizeToContents);
    m_serviceTable->horizontalHeader()->setSectionResizeMode(2, QHeaderView::Stretch);
    m_serviceTable->verticalHeader()->setVisible(false);
    m_serviceTable->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_serviceTable->setAlternatingRowColors(true);
    
    connect(m_serviceTable, &QTableWidget::cellChanged, 
            this, &EnergyModeDialog::onServiceToggled);
    
    serviceLayout->addWidget(m_serviceTable);
    layout->addWidget(serviceGroup);
    
    // Warning
    auto warningLabel = new QLabel(tr(
        "⚠️ Requiert les droits administrateur. Certains services système "
        "peuvent affecter des fonctionnalités Windows si désactivés."));
    warningLabel->setStyleSheet("color: #ff8800; padding: 10px; background-color: #332200; border-radius: 4px;");
    warningLabel->setWordWrap(true);
    layout->addWidget(warningLabel);
    
    // Buttons
    auto buttonLayout = new QHBoxLayout();
    buttonLayout->addStretch();
    
    auto closeButton = new QPushButton(tr("Fermer"));
    connect(closeButton, &QPushButton::clicked, this, &QDialog::accept);
    buttonLayout->addWidget(closeButton);
    
    layout->addLayout(buttonLayout);
}

void EnergyModeDialog::populateServiceList()
{
    m_serviceTable->blockSignals(true);
    m_serviceTable->setRowCount(0);
    
    const auto& services = m_manager->services();
    m_serviceTable->setRowCount(static_cast<int>(services.size()));
    
    for (int i = 0; i < static_cast<int>(services.size()); ++i) {
        const auto& service = services[i];
        
        // Checkbox
        auto checkItem = new QTableWidgetItem();
        checkItem->setCheckState(service.isSelected ? Qt::Checked : Qt::Unchecked);
        checkItem->setData(Qt::UserRole, service.name);
        m_serviceTable->setItem(i, 0, checkItem);
        
        // Service name
        auto nameItem = new QTableWidgetItem(service.displayName);
        nameItem->setFlags(nameItem->flags() & ~Qt::ItemIsEditable);
        m_serviceTable->setItem(i, 1, nameItem);
        
        // Description
        auto descItem = new QTableWidgetItem(service.description);
        descItem->setFlags(descItem->flags() & ~Qt::ItemIsEditable);
        m_serviceTable->setItem(i, 2, descItem);
    }
    
    m_serviceTable->blockSignals(false);
}

void EnergyModeDialog::onToggleClicked()
{
    if (!EnergyModeManager::isRunningAsAdmin()) {
        QMessageBox::warning(this, tr("Droits insuffisants"),
            tr("Le Mode Énergie nécessite les droits administrateur.\n\n"
               "Relancez PerfMonitorQt en tant qu'administrateur."));
        return;
    }
    
    m_toggleButton->setEnabled(false);
    m_progressBar->setVisible(true);
    
    bool success = m_manager->toggle();
    
    m_progressBar->setVisible(false);
    m_toggleButton->setEnabled(true);
    
    if (!success) {
        QMessageBox::warning(this, tr("Erreur"),
            tr("Impossible de changer l'état du Mode Énergie.\n\n%1")
            .arg(m_manager->statusMessage()));
    }
    
    updateUI();
}

void EnergyModeDialog::onServiceToggled(int row, int column)
{
    if (column != 0) return;
    
    auto item = m_serviceTable->item(row, 0);
    if (!item) return;
    
    QString serviceName = item->data(Qt::UserRole).toString();
    bool selected = item->checkState() == Qt::Checked;
    
    m_manager->setServiceEnabled(serviceName, selected);
    updateUI();
}

void EnergyModeDialog::updateStatus(const QString& message)
{
    m_statusLabel->setText(message);
}

void EnergyModeDialog::updateProgress(int current, int total)
{
    m_progressBar->setMaximum(total);
    m_progressBar->setValue(current);
}

void EnergyModeDialog::updateUI()
{
    bool isActive = m_manager->isActive();
    
    if (isActive) {
        m_statusLabel->setText(tr("🟢 Mode Énergie ACTIF"));
        m_statusLabel->setStyleSheet("font-weight: bold; font-size: 14px; color: #00cc00;");
        m_toggleButton->setText(tr("Désactiver"));
        m_toggleButton->setStyleSheet(R"(
            QPushButton {
                background-color: #cc0000;
                color: white;
                font-weight: bold;
                padding: 8px 16px;
                border-radius: 4px;
            }
            QPushButton:hover {
                background-color: #ff0000;
            }
        )");
    } else {
        m_statusLabel->setText(tr("⚪ Mode Énergie inactif"));
        m_statusLabel->setStyleSheet("font-weight: bold; font-size: 14px; color: #888;");
        m_toggleButton->setText(tr("Activer"));
        m_toggleButton->setStyleSheet(R"(
            QPushButton {
                background-color: #00aa00;
                color: white;
                font-weight: bold;
                padding: 8px 16px;
                border-radius: 4px;
            }
            QPushButton:hover {
                background-color: #00cc00;
            }
        )");
    }
    
    // Update estimate
    qint64 savings = m_manager->estimatedMemorySavings();
    int serviceCount = m_manager->servicesToStopCount();
    m_estimateLabel->setText(tr("%1 services sélectionnés • Économie estimée: ~%2")
        .arg(serviceCount)
        .arg(formatBytes(savings)));
}

QString EnergyModeDialog::formatBytes(qint64 bytes)
{
    if (bytes >= 1024 * 1024 * 1024)
        return QString("%1 GB").arg(bytes / (1024.0 * 1024.0 * 1024.0), 0, 'f', 1);
    if (bytes >= 1024 * 1024)
        return QString("%1 MB").arg(bytes / (1024.0 * 1024.0), 0, 'f', 0);
    if (bytes >= 1024)
        return QString("%1 KB").arg(bytes / 1024.0, 0, 'f', 0);
    return QString("%1 B").arg(bytes);
}
