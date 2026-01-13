#include "floatingwidget.h"
#include "sparklinegraph.h"

#include <QPainter>
#include <QPainterPath>
#include <QMouseEvent>
#include <QContextMenuEvent>
#include <QMenu>
#include <QAction>
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QGridLayout>
#include <QScreen>
#include <QGuiApplication>
#include <QGraphicsDropShadowEffect>

FloatingWidget::FloatingWidget(QWidget *parent)
    : QWidget(parent, Qt::Tool | Qt::FramelessWindowHint | Qt::WindowStaysOnTopHint)
{
    setAttribute(Qt::WA_TranslucentBackground);
    setAttribute(Qt::WA_ShowWithoutActivating);
    setMouseTracking(true);
    
    setupUi();
    loadSettings();
    
    // Apply initial opacity
    setWindowOpacity(m_opacity);
}

FloatingWidget::~FloatingWidget()
{
    saveSettings();
}

void FloatingWidget::setupUi()
{
    setFixedSize(180, 140);
    
    auto mainLayout = new QVBoxLayout(this);
    mainLayout->setContentsMargins(12, 10, 12, 10);
    mainLayout->setSpacing(6);

    // CPU Row
    auto cpuLayout = new QHBoxLayout();
    cpuLayout->setSpacing(8);
    
    m_cpuLabel = new QLabel("CPU");
    m_cpuLabel->setStyleSheet("color: #0078d7; font-weight: bold; font-size: 11px;");
    m_cpuLabel->setFixedWidth(35);
    cpuLayout->addWidget(m_cpuLabel);
    
    m_cpuGraph = new SparklineGraph(30, QColor(0, 120, 215));
    m_cpuGraph->setFixedSize(70, 20);
    m_cpuGraph->setShowGrid(false);
    m_cpuGraph->setShowLabels(false);
    m_cpuGraph->setBackgroundColor(QColor(40, 40, 40));
    cpuLayout->addWidget(m_cpuGraph);
    
    m_cpuValueLabel = new QLabel("0%");
    m_cpuValueLabel->setStyleSheet("color: white; font-weight: bold; font-size: 12px;");
    m_cpuValueLabel->setFixedWidth(40);
    m_cpuValueLabel->setAlignment(Qt::AlignRight | Qt::AlignVCenter);
    cpuLayout->addWidget(m_cpuValueLabel);
    
    mainLayout->addLayout(cpuLayout);

    // Memory Row
    auto memLayout = new QHBoxLayout();
    memLayout->setSpacing(8);
    
    m_memLabel = new QLabel("RAM");
    m_memLabel->setStyleSheet("color: #8b008b; font-weight: bold; font-size: 11px;");
    m_memLabel->setFixedWidth(35);
    memLayout->addWidget(m_memLabel);
    
    m_memGraph = new SparklineGraph(30, QColor(139, 0, 139));
    m_memGraph->setFixedSize(70, 20);
    m_memGraph->setShowGrid(false);
    m_memGraph->setShowLabels(false);
    m_memGraph->setBackgroundColor(QColor(40, 40, 40));
    memLayout->addWidget(m_memGraph);
    
    m_memValueLabel = new QLabel("0%");
    m_memValueLabel->setStyleSheet("color: white; font-weight: bold; font-size: 12px;");
    m_memValueLabel->setFixedWidth(40);
    m_memValueLabel->setAlignment(Qt::AlignRight | Qt::AlignVCenter);
    memLayout->addWidget(m_memValueLabel);
    
    mainLayout->addLayout(memLayout);

    // GPU Row (hidden by default)
    auto gpuLayout = new QHBoxLayout();
    gpuLayout->setSpacing(8);
    
    m_gpuLabel = new QLabel("GPU");
    m_gpuLabel->setStyleSheet("color: #00aa00; font-weight: bold; font-size: 11px;");
    m_gpuLabel->setFixedWidth(35);
    gpuLayout->addWidget(m_gpuLabel);
    
    m_gpuValueLabel = new QLabel("0%");
    m_gpuValueLabel->setStyleSheet("color: white; font-weight: bold; font-size: 12px;");
    gpuLayout->addWidget(m_gpuValueLabel);
    gpuLayout->addStretch();
    
    m_gpuTempLabel = new QLabel("");
    m_gpuTempLabel->setStyleSheet("color: #ffaa00; font-size: 10px;");
    gpuLayout->addWidget(m_gpuTempLabel);
    
    mainLayout->addLayout(gpuLayout);

    // Battery Row (hidden by default)
    auto batteryLayout = new QHBoxLayout();
    batteryLayout->setSpacing(8);
    
    m_batteryLabel = new QLabel("BAT");
    m_batteryLabel->setStyleSheet("color: #00aa00; font-weight: bold; font-size: 11px;");
    m_batteryLabel->setFixedWidth(35);
    batteryLayout->addWidget(m_batteryLabel);
    
    m_batteryValueLabel = new QLabel("0%");
    m_batteryValueLabel->setStyleSheet("color: white; font-weight: bold; font-size: 12px;");
    batteryLayout->addWidget(m_batteryValueLabel);
    batteryLayout->addStretch();
    
    mainLayout->addLayout(batteryLayout);

    mainLayout->addStretch();
    
    // Initial visibility
    m_gpuLabel->setVisible(m_showGpu);
    m_gpuValueLabel->setVisible(m_showGpu);
    m_gpuTempLabel->setVisible(m_showGpu);
    m_batteryLabel->setVisible(m_showBattery);
    m_batteryValueLabel->setVisible(m_showBattery);
    
    updateLayout();
}

void FloatingWidget::updateLayout()
{
    int height = 60; // Base height
    if (m_showCpu) height += 26;
    if (m_showMemory) height += 26;
    if (m_showGpu) height += 26;
    if (m_showBattery) height += 26;
    
    setFixedHeight(height);
}

void FloatingWidget::paintEvent(QPaintEvent*)
{
    QPainter painter(this);
    painter.setRenderHint(QPainter::Antialiasing);

    // Background with rounded corners
    QPainterPath path;
    path.addRoundedRect(rect().adjusted(2, 2, -2, -2), 10, 10);
    
    // Shadow effect simulation
    painter.fillPath(path.translated(2, 2), QColor(0, 0, 0, 50));
    
    // Main background
    QColor bgColor = m_isHovered ? QColor(45, 45, 48) : QColor(30, 30, 32);
    painter.fillPath(path, bgColor);
    
    // Border
    painter.setPen(QPen(QColor(60, 60, 65), 1));
    painter.drawPath(path);
    
    // Accent line at top
    painter.setPen(QPen(QColor(0, 120, 215), 2));
    painter.drawLine(12, 4, width() - 12, 4);
}

void FloatingWidget::updateMetrics(double cpuUsage, double memoryUsage,
                                   double gpuUsage, int batteryPercent,
                                   [[maybe_unused]] double cpuTemp, double gpuTemp)
{
    // CPU
    m_cpuValueLabel->setText(formatValue(cpuUsage));
    m_cpuGraph->addValue(cpuUsage);
    
    // Color based on usage
    QString cpuColor = cpuUsage > 80 ? "#ff4444" : (cpuUsage > 50 ? "#ffaa00" : "#00cc66");
    m_cpuValueLabel->setStyleSheet(QString("color: %1; font-weight: bold; font-size: 12px;").arg(cpuColor));
    
    // Memory
    m_memValueLabel->setText(formatValue(memoryUsage));
    m_memGraph->addValue(memoryUsage);
    
    QString memColor = memoryUsage > 85 ? "#ff4444" : (memoryUsage > 70 ? "#ffaa00" : "#00cc66");
    m_memValueLabel->setStyleSheet(QString("color: %1; font-weight: bold; font-size: 12px;").arg(memColor));
    
    // GPU (if available)
    if (gpuUsage >= 0 && m_showGpu) {
        m_gpuValueLabel->setText(formatValue(gpuUsage));
        if (gpuTemp > 0) {
            m_gpuTempLabel->setText(QString("%1°C").arg(gpuTemp, 0, 'f', 0));
        }
    }
    
    // Battery (if available)
    if (batteryPercent >= 0 && m_showBattery) {
        m_batteryValueLabel->setText(QString("%1%").arg(batteryPercent));
        QString batColor = batteryPercent < 20 ? "#ff4444" : (batteryPercent < 50 ? "#ffaa00" : "#00cc66");
        m_batteryValueLabel->setStyleSheet(QString("color: %1; font-weight: bold; font-size: 12px;").arg(batColor));
    }
}

QString FloatingWidget::formatValue(double value, const QString& suffix)
{
    return QString("%1%2").arg(value, 0, 'f', 1).arg(suffix);
}

// Mouse events for dragging
void FloatingWidget::mousePressEvent(QMouseEvent *event)
{
    if (event->button() == Qt::LeftButton) {
        m_isDragging = true;
        m_dragPosition = event->globalPosition().toPoint() - frameGeometry().topLeft();
        event->accept();
    }
}

void FloatingWidget::mouseMoveEvent(QMouseEvent *event)
{
    if (m_isDragging && (event->buttons() & Qt::LeftButton)) {
        move(event->globalPosition().toPoint() - m_dragPosition);
        event->accept();
    }
}

void FloatingWidget::mouseReleaseEvent(QMouseEvent *event)
{
    if (event->button() == Qt::LeftButton) {
        m_isDragging = false;
        saveSettings(); // Save position
        event->accept();
    }
}

void FloatingWidget::mouseDoubleClickEvent(QMouseEvent *event)
{
    if (event->button() == Qt::LeftButton) {
        emit mainWindowRequested();
        event->accept();
    }
}

void FloatingWidget::enterEvent(QEnterEvent*)
{
    m_isHovered = true;
    update();
}

void FloatingWidget::leaveEvent(QEvent*)
{
    m_isHovered = false;
    update();
}

void FloatingWidget::contextMenuEvent(QContextMenuEvent *event)
{
    QMenu menu(this);
    menu.setStyleSheet(R"(
        QMenu {
            background-color: #2d2d30;
            color: white;
            border: 1px solid #3d3d3d;
            padding: 5px;
        }
        QMenu::item {
            padding: 6px 25px;
            border-radius: 3px;
        }
        QMenu::item:selected {
            background-color: #0078d7;
        }
        QMenu::separator {
            height: 1px;
            background: #3d3d3d;
            margin: 5px 10px;
        }
    )");

    // Show main window
    auto showMainAction = menu.addAction("Ouvrir PerfMonitor");
    connect(showMainAction, &QAction::triggered, this, &FloatingWidget::mainWindowRequested);
    
    menu.addSeparator();
    
    // Close
    auto closeAction = menu.addAction("Fermer le widget");
    connect(closeAction, &QAction::triggered, this, &FloatingWidget::closeRequested);

    menu.exec(event->globalPos());
}

// Setters
void FloatingWidget::setShowCpu(bool show)
{
    m_showCpu = show;
    m_cpuLabel->setVisible(show);
    m_cpuValueLabel->setVisible(show);
    m_cpuGraph->setVisible(show && m_showGraphs);
    updateLayout();
    saveSettings();
}

void FloatingWidget::setShowMemory(bool show)
{
    m_showMemory = show;
    m_memLabel->setVisible(show);
    m_memValueLabel->setVisible(show);
    m_memGraph->setVisible(show && m_showGraphs);
    updateLayout();
    saveSettings();
}

void FloatingWidget::setShowGpu(bool show)
{
    m_showGpu = show;
    m_gpuLabel->setVisible(show);
    m_gpuValueLabel->setVisible(show);
    m_gpuTempLabel->setVisible(show);
    updateLayout();
    saveSettings();
}

void FloatingWidget::setShowBattery(bool show)
{
    m_showBattery = show;
    m_batteryLabel->setVisible(show);
    m_batteryValueLabel->setVisible(show);
    updateLayout();
    saveSettings();
}

void FloatingWidget::setShowGraphs(bool show)
{
    m_showGraphs = show;
    m_cpuGraph->setVisible(show && m_showCpu);
    m_memGraph->setVisible(show && m_showMemory);
    saveSettings();
}

void FloatingWidget::setWidgetOpacity(double opacity)
{
    m_opacity = qBound(0.2, opacity, 1.0);
    setWindowOpacity(m_opacity);
    saveSettings();
}

// Settings persistence
void FloatingWidget::loadSettings()
{
    QSettings settings("Félix-Antoine", "PerfMonitorQt");
    settings.beginGroup("FloatingWidget");
    
    // Position
    QPoint defaultPos(100, 100);
    QPoint pos = settings.value("position", defaultPos).toPoint();
    
    // Validate position is on screen
    bool onScreen = false;
    for (QScreen* screen : QGuiApplication::screens()) {
        if (screen->geometry().contains(pos)) {
            onScreen = true;
            break;
        }
    }
    move(onScreen ? pos : defaultPos);
    
    // Display options
    m_showCpu = settings.value("showCpu", true).toBool();
    m_showMemory = settings.value("showMemory", true).toBool();
    m_showGpu = settings.value("showGpu", false).toBool();
    m_showBattery = settings.value("showBattery", false).toBool();
    m_showGraphs = settings.value("showGraphs", true).toBool();
    m_opacity = settings.value("opacity", 0.9).toDouble();
    
    settings.endGroup();
    
    // Apply visibility
    if (m_cpuLabel) {
        m_cpuLabel->setVisible(m_showCpu);
        m_cpuValueLabel->setVisible(m_showCpu);
        m_cpuGraph->setVisible(m_showCpu && m_showGraphs);
    }
    if (m_memLabel) {
        m_memLabel->setVisible(m_showMemory);
        m_memValueLabel->setVisible(m_showMemory);
        m_memGraph->setVisible(m_showMemory && m_showGraphs);
    }
    if (m_gpuLabel) {
        m_gpuLabel->setVisible(m_showGpu);
        m_gpuValueLabel->setVisible(m_showGpu);
        m_gpuTempLabel->setVisible(m_showGpu);
    }
    if (m_batteryLabel) {
        m_batteryLabel->setVisible(m_showBattery);
        m_batteryValueLabel->setVisible(m_showBattery);
    }
    
    updateLayout();
}

void FloatingWidget::saveSettings()
{
    QSettings settings("Félix-Antoine", "PerfMonitorQt");
    settings.beginGroup("FloatingWidget");
    
    settings.setValue("position", pos());
    settings.setValue("showCpu", m_showCpu);
    settings.setValue("showMemory", m_showMemory);
    settings.setValue("showGpu", m_showGpu);
    settings.setValue("showBattery", m_showBattery);
    settings.setValue("showGraphs", m_showGraphs);
    settings.setValue("opacity", m_opacity);
    
    settings.endGroup();
}
