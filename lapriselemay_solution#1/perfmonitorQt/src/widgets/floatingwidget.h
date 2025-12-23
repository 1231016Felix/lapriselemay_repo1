#pragma once

#include <QWidget>
#include <QLabel>
#include <QTimer>
#include <QPoint>
#include <QSettings>
#include <memory>

class SparklineGraph;

/**
 * @brief Compact floating widget displaying real-time system metrics
 * 
 * A small, always-on-top, draggable widget that shows CPU, RAM,
 * and optionally GPU/Battery information in a compact format.
 */
class FloatingWidget : public QWidget
{
    Q_OBJECT

public:
    explicit FloatingWidget(QWidget *parent = nullptr);
    ~FloatingWidget() override;

    /// Update displayed metrics
    void updateMetrics(double cpuUsage, double memoryUsage, 
                       double gpuUsage = -1, int batteryPercent = -1,
                       double cpuTemp = -1, double gpuTemp = -1);

    /// Show/hide individual sections
    void setShowCpu(bool show);
    void setShowMemory(bool show);
    void setShowGpu(bool show);
    void setShowBattery(bool show);
    void setShowGraphs(bool show);
    
    /// Widget opacity (0.0 - 1.0)
    void setWidgetOpacity(double opacity);
    double widgetOpacity() const { return m_opacity; }

signals:
    void closeRequested();
    void settingsRequested();
    void mainWindowRequested();

protected:
    void paintEvent(QPaintEvent *event) override;
    void mousePressEvent(QMouseEvent *event) override;
    void mouseMoveEvent(QMouseEvent *event) override;
    void mouseReleaseEvent(QMouseEvent *event) override;
    void mouseDoubleClickEvent(QMouseEvent *event) override;
    void contextMenuEvent(QContextMenuEvent *event) override;
    void enterEvent(QEnterEvent *event) override;
    void leaveEvent(QEvent *event) override;

private:
    void setupUi();
    void createContextMenu();
    void loadSettings();
    void saveSettings();
    void updateLayout();
    QString formatValue(double value, const QString& suffix = "%");

    // UI Elements
    QLabel* m_cpuLabel{nullptr};
    QLabel* m_cpuValueLabel{nullptr};
    QLabel* m_memLabel{nullptr};
    QLabel* m_memValueLabel{nullptr};
    QLabel* m_gpuLabel{nullptr};
    QLabel* m_gpuValueLabel{nullptr};
    QLabel* m_batteryLabel{nullptr};
    QLabel* m_batteryValueLabel{nullptr};
    QLabel* m_cpuTempLabel{nullptr};
    QLabel* m_gpuTempLabel{nullptr};

    // Mini graphs
    SparklineGraph* m_cpuGraph{nullptr};
    SparklineGraph* m_memGraph{nullptr};

    // Drag handling
    QPoint m_dragPosition;
    bool m_isDragging{false};

    // Display options
    bool m_showCpu{true};
    bool m_showMemory{true};
    bool m_showGpu{false};
    bool m_showBattery{false};
    bool m_showGraphs{true};
    bool m_showTemps{false};
    double m_opacity{0.9};
    
    // Hover state
    bool m_isHovered{false};
};
