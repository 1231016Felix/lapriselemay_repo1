#pragma once

#include <QDialog>
#include <QTableWidget>
#include <QLabel>
#include <QPushButton>
#include <QProgressBar>

class EnergyModeManager;

/**
 * @brief Configuration dialog for Energy Mode
 */
class EnergyModeDialog : public QDialog
{
    Q_OBJECT

public:
    explicit EnergyModeDialog(EnergyModeManager* manager, QWidget *parent = nullptr);

private slots:
    void onToggleClicked();
    void onServiceToggled(int row, int column);
    void updateStatus(const QString& message);
    void updateProgress(int current, int total);
    void updateUI();

private:
    void setupUi();
    void populateServiceList();
    QString formatBytes(qint64 bytes);

    EnergyModeManager* m_manager;
    
    QLabel* m_statusLabel{nullptr};
    QLabel* m_estimateLabel{nullptr};
    QPushButton* m_toggleButton{nullptr};
    QProgressBar* m_progressBar{nullptr};
    QTableWidget* m_serviceTable{nullptr};
};
