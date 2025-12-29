#pragma once

#include <QWidget>
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QGridLayout>
#include <QLabel>
#include <QProgressBar>
#include <QFrame>
#include <QComboBox>
#include <QPushButton>
#include <QTimer>
#include <QPropertyAnimation>
#include <vector>
#include <memory>

#include "../monitors/processimpactmonitor.h"

class SparklineGraph;
class QPropertyAnimation;

/**
 * @brief Compact bar showing single process impact (used in lists)
 */
class ImpactBar : public QFrame
{
    Q_OBJECT
    Q_PROPERTY(double barValue READ barValue WRITE setBarValue)

public:
    explicit ImpactBar(QWidget* parent = nullptr);
    
    void setProcessInfo(const ProcessImpact& impact, ImpactCategory category);
    void updateLabels();
    void clear();
    
    double barValue() const { return m_barValue; }
    void setBarValue(double value);
    void setOpacity(double opacity);
    void animateTo(double targetValue, int durationMs = 300);
    
    QString getValueText() const;
    QColor getBarColor() const;
    
    DWORD pid() const { return m_pid; }

signals:
    void clicked(DWORD pid);
    void detailsRequested(DWORD pid);

protected:
    void enterEvent(QEnterEvent* event) override;
    void leaveEvent(QEvent* event) override;
    void mousePressEvent(QMouseEvent* event) override;
    void mouseDoubleClickEvent(QMouseEvent* event) override;
    void paintEvent(QPaintEvent* event) override;

private:
    QLabel* m_iconLabel{nullptr};
    QLabel* m_nameLabel{nullptr};
    QLabel* m_detailLabel{nullptr};
    QLabel* m_valueLabel{nullptr};
    
    ProcessImpact m_impact;
    DWORD m_pid{0};
    QString m_name;
    QString m_displayName;
    QIcon m_icon;
    ImpactCategory m_category{ImpactCategory::OverallImpact};
    double m_displayValue{0.0};
    double m_barValue{0.0};
    double m_opacity{1.0};
    bool m_hovered{false};
    
    std::unique_ptr<QPropertyAnimation> m_animation;
};

/**
 * @brief Impact card showing top processes for a category
 */
class ImpactCard : public QFrame
{
    Q_OBJECT

public:
    static constexpr int MAX_BARS = 5;
    
    explicit ImpactCard(const QString& title, ImpactCategory category,
                        const QString& icon, QWidget* parent = nullptr);
    
    void updateData(const std::vector<ProcessImpact>& impacts);
    void setCategory(ImpactCategory category);
    void clear();
    
    ImpactCategory category() const { return m_category; }

signals:
    void processClicked(DWORD pid);
    void processDetailsRequested(DWORD pid);
    void viewAllClicked(ImpactCategory category);

private:
    void setupUi();
    
    QString m_title;
    QString m_iconText;
    ImpactCategory m_category;
    
    QLabel* m_iconLabel{nullptr};
    QLabel* m_titleLabel{nullptr};
    QPushButton* m_viewAllButton{nullptr};
    QVBoxLayout* m_barsLayout{nullptr};
    QLabel* m_emptyLabel{nullptr};
    std::vector<ImpactBar*> m_bars;
};

/**
 * @brief Single process impact card showing resource usage
 */
class ProcessImpactCard : public QFrame
{
    Q_OBJECT
    Q_PROPERTY(double highlightOpacity READ highlightOpacity WRITE setHighlightOpacity)

public:
    explicit ProcessImpactCard(QWidget* parent = nullptr);
    
    void setProcessData(const ProcessImpact& impact, ImpactCategory category);
    void clear();
    
    double highlightOpacity() const { return m_highlightOpacity; }
    void setHighlightOpacity(double opacity);

signals:
    void clicked(DWORD pid);
    void contextMenuRequested(DWORD pid, const QPoint& pos);

protected:
    void enterEvent(QEnterEvent* event) override;
    void leaveEvent(QEvent* event) override;
    void mousePressEvent(QMouseEvent* event) override;
    void mouseReleaseEvent(QMouseEvent* event) override;
    void paintEvent(QPaintEvent* event) override;
    void contextMenuEvent(QContextMenuEvent* event) override;

private:
    void setupUi();
    void updateColors();
    QString formatBytes(qint64 bytes) const;
    QString formatBytesPerSec(qint64 bytesPerSec) const;
    QColor getImpactColor(double score) const;
    
    QLabel* m_rankLabel{nullptr};
    QLabel* m_iconLabel{nullptr};
    QLabel* m_nameLabel{nullptr};
    QLabel* m_descLabel{nullptr};
    QLabel* m_valueLabel{nullptr};
    QLabel* m_detailLabel{nullptr};
    QProgressBar* m_impactBar{nullptr};
    SparklineGraph* m_sparkline{nullptr};
    
    ProcessImpact m_impact;
    ImpactCategory m_category{ImpactCategory::OverallImpact};
    int m_rank{0};
    
    double m_highlightOpacity{0.0};
    bool m_hovered{false};
    bool m_pressed{false};
};

/**
 * @brief Category panel showing top 5 processes for a specific metric
 */
class ImpactCategoryPanel : public QFrame
{
    Q_OBJECT

public:
    explicit ImpactCategoryPanel(ImpactCategory category, QWidget* parent = nullptr);
    
    void setData(const std::vector<ProcessImpact>& processes);
    void setTitle(const QString& title);
    void setIcon(const QString& iconPath);
    ImpactCategory category() const { return m_category; }

signals:
    void processClicked(DWORD pid);
    void viewAllClicked(ImpactCategory category);

private:
    void setupUi();
    QString getCategoryTitle() const;
    QString getCategoryIcon() const;
    QString getCategoryColor() const;
    
    ImpactCategory m_category;
    QLabel* m_titleLabel{nullptr};
    QLabel* m_iconLabel{nullptr};
    QPushButton* m_viewAllButton{nullptr};
    std::vector<ProcessImpactCard*> m_cards;
};

/**
 * @brief Main widget showing process impact analysis
 */
class ProcessImpactWidget : public QWidget
{
    Q_OBJECT

public:
    explicit ProcessImpactWidget(QWidget* parent = nullptr);
    ~ProcessImpactWidget() override;
    
    /// Start monitoring
    void startMonitoring();
    
    /// Stop monitoring
    void stopMonitoring();
    
    /// Check if monitoring
    bool isMonitoring() const;
    
    /// Set an external monitor (widget won't own it)
    void setMonitor(ProcessImpactMonitor* monitor);
    
    /// Get the monitor instance
    ProcessImpactMonitor* monitor() { return m_monitor; }
    
    /// Set refresh interval in ms
    void setRefreshInterval(int ms);
    
    /// Set whether to show system processes
    void setShowSystemProcesses(bool show);
    
    /// Set analysis window in seconds
    void setAnalysisWindow(int seconds);
    
    /// Get analysis window
    int analysisWindow() const;

signals:
    void processDetailsRequested(DWORD pid);
    void processSelected(quint32 pid);
    void viewAllRequested(ImpactCategory category);

public slots:
    void refresh();

private slots:
    void onDataUpdated();
    void onProcessClicked(DWORD pid);
    void onViewAllClicked(ImpactCategory category);
    void onCategoryChanged(int index);
    void onMonitorUpdated();
    void onRefreshTimer();
    void onWindowChanged(int index);

private:
    void setupUi();
    void createHeader();
    void createPanels();
    void updatePanels();
    void updateCards();
    
    ProcessImpactMonitor* m_monitor{nullptr};
    bool m_ownsMonitor{false};
    
    // UI Components
    QVBoxLayout* m_mainLayout{nullptr};
    QWidget* m_headerWidget{nullptr};
    QLabel* m_titleLabel{nullptr};
    QComboBox* m_viewModeCombo{nullptr};
    QComboBox* m_windowCombo{nullptr};
    QPushButton* m_refreshButton{nullptr};
    QPushButton* m_settingsButton{nullptr};
    QLabel* m_statusLabel{nullptr};
    
    // Cards container
    QWidget* m_cardsContainer{nullptr};
    ImpactCard* m_batteryCard{nullptr};
    ImpactCard* m_cpuCard{nullptr};
    ImpactCard* m_diskCard{nullptr};
    ImpactCard* m_memoryCard{nullptr};
    
    // Footer
    QWidget* m_footerWidget{nullptr};
    QLabel* m_legendLabel{nullptr};
    QLabel* m_coverageLabel{nullptr};
    
    // Panels for different categories (legacy)
    QWidget* m_panelsContainer{nullptr};
    QGridLayout* m_panelsLayout{nullptr};
    ImpactCategoryPanel* m_batteryPanel{nullptr};
    ImpactCategoryPanel* m_diskPanel{nullptr};
    ImpactCategoryPanel* m_memoryPanel{nullptr};
    ImpactCategoryPanel* m_cpuPanel{nullptr};
    
    // Settings
    int m_refreshIntervalMs{2000};
    bool m_showSystem{false};
    
    // Current view mode
    enum class ViewMode {
        Grid,       // 2x2 grid of panels
        Battery,    // Focus on battery drainers
        Disk,       // Focus on disk hogs
        Memory,     // Focus on memory hogs
        Cpu         // Focus on CPU hogs
    };
    ViewMode m_viewMode{ViewMode::Grid};
    
    QTimer* m_refreshTimer{nullptr};
};
