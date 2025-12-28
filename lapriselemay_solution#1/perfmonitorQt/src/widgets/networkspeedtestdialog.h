#pragma once

#include <QDialog>
#include <QLabel>
#include <QPushButton>
#include <QProgressBar>
#include <QComboBox>
#include <QTableWidget>
#include <QVBoxLayout>
#include <QTimer>
#include <memory>

#include "../monitors/networkspeedtest.h"

class SpeedGauge;

/**
 * @brief Custom gauge widget for displaying speed
 */
class SpeedGauge : public QWidget
{
    Q_OBJECT

public:
    explicit SpeedGauge(QWidget* parent = nullptr);
    
    void setValue(double value);
    void setMaxValue(double max) { m_maxValue = max; update(); }
    void setTitle(const QString& title) { m_title = title; update(); }
    void setUnit(const QString& unit) { m_unit = unit; update(); }
    void setColor(const QColor& color) { m_color = color; update(); }
    void reset();

protected:
    void paintEvent(QPaintEvent* event) override;

private:
    double m_value{0.0};
    double m_maxValue{1000.0};
    QString m_title;
    QString m_unit{"Mbps"};
    QColor m_color{0, 150, 255};
};

/**
 * @brief Dialog for running network speed tests
 */
class NetworkSpeedTestDialog : public QDialog
{
    Q_OBJECT

public:
    explicit NetworkSpeedTestDialog(QWidget* parent = nullptr);
    ~NetworkSpeedTestDialog() override;

private slots:
    void onStartTest();
    void onStopTest();
    void onStateChanged(SpeedTestState state);
    void onProgressChanged(int percent, const QString& status);
    void onPingUpdated(int pingMs);
    void onDownloadSpeedUpdated(double mbps);
    void onUploadSpeedUpdated(double mbps);
    void onTestCompleted(const SpeedTestResult& result);
    void onTestFailed(const QString& error);
    void onServerChanged(int index);
    void updateHistoryTable();

private:
    void setupUi();
    void createGaugesSection(QVBoxLayout* layout);
    void createControlsSection(QVBoxLayout* layout);
    void createResultsSection(QVBoxLayout* layout);
    void createHistorySection(QVBoxLayout* layout);
    void connectSignals();
    void resetUI();
    void addResultToHistory(const SpeedTestResult& result);
    QString formatSpeed(double mbps);

    // Speed test engine
    std::unique_ptr<NetworkSpeedTest> m_speedTest;
    
    // Gauges
    SpeedGauge* m_downloadGauge{nullptr};
    SpeedGauge* m_uploadGauge{nullptr};
    
    // Results display
    QLabel* m_pingLabel{nullptr};
    QLabel* m_jitterLabel{nullptr};
    QLabel* m_downloadLabel{nullptr};
    QLabel* m_uploadLabel{nullptr};
    QLabel* m_serverLabel{nullptr};
    QLabel* m_statusLabel{nullptr};
    
    // Progress
    QProgressBar* m_progressBar{nullptr};
    
    // Controls
    QPushButton* m_startButton{nullptr};
    QPushButton* m_stopButton{nullptr};
    QComboBox* m_serverCombo{nullptr};
    
    // History
    QTableWidget* m_historyTable{nullptr};
    
    // Animation timer for smooth gauge updates
    QTimer* m_animationTimer{nullptr};
    double m_targetDownloadSpeed{0.0};
    double m_targetUploadSpeed{0.0};
    double m_currentDownloadSpeed{0.0};
    double m_currentUploadSpeed{0.0};
};
