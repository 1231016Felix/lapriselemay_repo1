#include "processimpactwidget.h"

#include <QPainter>
#include <QPainterPath>
#include <QMouseEvent>
#include <QToolTip>
#include <QGraphicsOpacityEffect>
#include <QScrollArea>
#include <algorithm>

// ============================================================================
// ImpactBar Implementation
// ============================================================================

ImpactBar::ImpactBar(QWidget* parent)
    : QFrame(parent)
{
    setFixedHeight(52);
    setMinimumWidth(200);
    setCursor(Qt::PointingHandCursor);
    setMouseTracking(true);
    
    auto* layout = new QHBoxLayout(this);
    layout->setContentsMargins(8, 6, 12, 6);
    layout->setSpacing(10);
    
    // Icon
    m_iconLabel = new QLabel(this);
    m_iconLabel->setFixedSize(28, 28);
    m_iconLabel->setAlignment(Qt::AlignCenter);
    layout->addWidget(m_iconLabel);
    
    // Name and detail
    auto* textLayout = new QVBoxLayout();
    textLayout->setContentsMargins(0, 0, 0, 0);
    textLayout->setSpacing(0);
    
    m_nameLabel = new QLabel(this);
    m_nameLabel->setStyleSheet("font-weight: 500; color: #e0e0e0;");
    textLayout->addWidget(m_nameLabel);
    
    m_detailLabel = new QLabel(this);
    m_detailLabel->setStyleSheet("font-size: 11px; color: #888;");
    textLayout->addWidget(m_detailLabel);
    
    layout->addLayout(textLayout, 1);
    
    // Value
    m_valueLabel = new QLabel(this);
    m_valueLabel->setStyleSheet("font-weight: 600; color: #fff; font-size: 13px;");
    m_valueLabel->setAlignment(Qt::AlignRight | Qt::AlignVCenter);
    m_valueLabel->setMinimumWidth(70);
    layout->addWidget(m_valueLabel);
    
    // Initial style
    setStyleSheet(R"(
        ImpactBar {
            background-color: #2a2a2a;
            border-radius: 8px;
            border: 1px solid #3a3a3a;
        }
        ImpactBar:hover {
            background-color: #333;
            border-color: #4a4a4a;
        }
    )");
}

void ImpactBar::setProcessInfo(const ProcessImpact& impact, ImpactCategory category)
{
    m_impact = impact;
    m_pid = impact.pid;
    m_name = impact.name;
    m_displayName = impact.displayName;
    m_icon = impact.icon;
    m_category = category;
    
    // Set icon
    if (!m_icon.isNull()) {
        m_iconLabel->setPixmap(m_icon.pixmap(24, 24));
    } else {
        m_iconLabel->setText("📦");
        m_iconLabel->setStyleSheet("font-size: 18px;");
    }
    
    // Set name (truncate if needed)
    QString displayName = m_displayName.isEmpty() ? m_name : m_displayName;
    QFontMetrics fm(m_nameLabel->font());
    QString elidedName = fm.elidedText(displayName, Qt::ElideRight, 180);
    m_nameLabel->setText(elidedName);
    m_nameLabel->setToolTip(displayName);
    
    updateLabels();
}

void ImpactBar::updateLabels()
{
    // Set value based on category
    QString valueText;
    QString detailText;
    
    switch (m_category) {
        case ImpactCategory::BatteryDrain:
            m_displayValue = m_impact.batteryImpactScore;
            valueText = QString::number(m_displayValue, 'f', 0) + "%";
            detailText = QString("CPU: %1% | Disk: %2")
                .arg(m_impact.avgCpuPercent, 0, 'f', 1)
                .arg(ProcessImpactMonitor::formatBytes(m_impact.totalReadBytes + m_impact.totalWriteBytes));
            break;
            
        case ImpactCategory::CpuUsage:
            m_displayValue = m_impact.avgCpuPercent;
            valueText = QString::number(m_displayValue, 'f', 1) + "%";
            detailText = QString("Peak: %1% | Active: %2%")
                .arg(m_impact.peakCpuPercent, 0, 'f', 0)
                .arg(m_impact.activityPercent, 0, 'f', 0);
            break;
            
        case ImpactCategory::DiskIO:
        case ImpactCategory::DiskRead:
        case ImpactCategory::DiskWrite: {
            qint64 total = m_impact.totalReadBytes + m_impact.totalWriteBytes;
            m_displayValue = m_impact.diskImpactScore;
            valueText = ProcessImpactMonitor::formatBytes(total);
            detailText = QString("R: %1 | W: %2")
                .arg(ProcessImpactMonitor::formatBytes(m_impact.totalReadBytes))
                .arg(ProcessImpactMonitor::formatBytes(m_impact.totalWriteBytes));
            break;
        }
            
        case ImpactCategory::MemoryUsage:
            m_displayValue = static_cast<double>(m_impact.avgMemoryBytes) / (1024.0 * 1024.0 * 1024.0) * 25; // Normalize
            valueText = ProcessImpactMonitor::formatBytes(m_impact.avgMemoryBytes);
            detailText = QString("Peak: %1")
                .arg(ProcessImpactMonitor::formatBytes(m_impact.peakMemoryBytes));
            if (m_impact.memoryGrowth > 1024 * 1024) {
                detailText += QString(" | +%1").arg(ProcessImpactMonitor::formatBytes(m_impact.memoryGrowth));
            }
            break;
            
        case ImpactCategory::GpuUsage:
            m_displayValue = m_impact.avgGpuPercent;
            valueText = QString::number(m_displayValue, 'f', 1) + "%";
            detailText = QString("Peak: %1%")
                .arg(m_impact.peakGpuPercent, 0, 'f', 0);
            break;
            
        default:
            m_displayValue = m_impact.overallImpactScore;
            valueText = QString::number(m_displayValue, 'f', 0) + "%";
            detailText = "";
            break;
    }
    
    m_valueLabel->setText(valueText);
    m_detailLabel->setText(detailText);
}

QString ImpactBar::getValueText() const
{
    return m_valueLabel->text();
}

QColor ImpactBar::getBarColor() const
{
    // Color gradient based on impact value
    if (m_barValue < 20) return QColor(76, 175, 80);    // Green
    if (m_barValue < 40) return QColor(139, 195, 74);   // Light green
    if (m_barValue < 60) return QColor(255, 193, 7);    // Amber
    if (m_barValue < 80) return QColor(255, 152, 0);    // Orange
    return QColor(244, 67, 54);                          // Red
}

void ImpactBar::setBarValue(double value)
{
    m_barValue = std::clamp(value, 0.0, 100.0);
    update();
}

void ImpactBar::setOpacity(double opacity)
{
    m_opacity = std::clamp(opacity, 0.0, 1.0);
    update();
}

void ImpactBar::animateTo(double targetValue, int durationMs)
{
    if (!m_animation) {
        m_animation = std::make_unique<QPropertyAnimation>(this, "barValue");
    }
    
    m_animation->stop();
    m_animation->setDuration(durationMs);
    m_animation->setStartValue(m_barValue);
    m_animation->setEndValue(targetValue);
    m_animation->setEasingCurve(QEasingCurve::OutCubic);
    m_animation->start();
}

void ImpactBar::clear()
{
    m_pid = 0;
    m_name.clear();
    m_displayName.clear();
    m_icon = QIcon();
    m_barValue = 0;
    m_displayValue = 0;
    m_iconLabel->clear();
    m_nameLabel->clear();
    m_valueLabel->clear();
    m_detailLabel->clear();
    update();
}

void ImpactBar::paintEvent(QPaintEvent* event)
{
    QFrame::paintEvent(event);
    
    if (m_barValue <= 0) return;
    
    QPainter painter(this);
    painter.setRenderHint(QPainter::Antialiasing);
    painter.setOpacity(m_opacity);
    
    // Draw progress bar background
    QRectF barRect(6, height() - 6, width() - 12, 3);
    painter.setPen(Qt::NoPen);
    painter.setBrush(QColor(60, 60, 60));
    painter.drawRoundedRect(barRect, 1.5, 1.5);
    
    // Draw progress
    double progress = std::min(m_barValue / 100.0, 1.0);
    QRectF progressRect(6, height() - 6, (width() - 12) * progress, 3);
    
    QColor barColor = getBarColor();
    if (m_hovered) {
        barColor = barColor.lighter(115);
    }
    
    // Gradient
    QLinearGradient gradient(progressRect.topLeft(), progressRect.topRight());
    gradient.setColorAt(0, barColor.darker(110));
    gradient.setColorAt(1, barColor);
    painter.setBrush(gradient);
    painter.drawRoundedRect(progressRect, 1.5, 1.5);
}

void ImpactBar::enterEvent(QEnterEvent* event)
{
    m_hovered = true;
    update();
    QFrame::enterEvent(event);
}

void ImpactBar::leaveEvent(QEvent* event)
{
    m_hovered = false;
    update();
    QFrame::leaveEvent(event);
}

void ImpactBar::mousePressEvent(QMouseEvent* event)
{
    if (event->button() == Qt::LeftButton && m_pid != 0) {
        emit clicked(m_pid);
    }
    QFrame::mousePressEvent(event);
}

void ImpactBar::mouseDoubleClickEvent(QMouseEvent* event)
{
    if (event->button() == Qt::LeftButton && m_pid != 0) {
        emit detailsRequested(m_pid);
    }
    QFrame::mouseDoubleClickEvent(event);
}

// ============================================================================
// ImpactCard Implementation
// ============================================================================

ImpactCard::ImpactCard(const QString& title, ImpactCategory category,
                       const QString& icon, QWidget* parent)
    : QFrame(parent)
    , m_title(title)
    , m_iconText(icon)
    , m_category(category)
{
    setupUi();
}

void ImpactCard::setupUi()
{
    setFrameShape(QFrame::StyledPanel);
    setStyleSheet(R"(
        ImpactCard {
            background-color: #1e1e1e;
            border-radius: 12px;
            border: 1px solid #333;
        }
    )");
    
    auto* mainLayout = new QVBoxLayout(this);
    mainLayout->setContentsMargins(16, 12, 16, 16);
    mainLayout->setSpacing(12);
    
    // Header
    auto* headerLayout = new QHBoxLayout();
    headerLayout->setSpacing(8);
    
    m_iconLabel = new QLabel(m_iconText, this);
    m_iconLabel->setStyleSheet("font-size: 20px;");
    headerLayout->addWidget(m_iconLabel);
    
    m_titleLabel = new QLabel(m_title, this);
    m_titleLabel->setStyleSheet("font-size: 14px; font-weight: 600; color: #fff;");
    headerLayout->addWidget(m_titleLabel);
    
    headerLayout->addStretch();
    
    m_viewAllButton = new QPushButton("View All", this);
    m_viewAllButton->setStyleSheet(R"(
        QPushButton {
            background: transparent;
            border: none;
            color: #64b5f6;
            font-size: 12px;
            padding: 4px 8px;
        }
        QPushButton:hover {
            color: #90caf9;
            text-decoration: underline;
        }
    )");
    connect(m_viewAllButton, &QPushButton::clicked, this, [this]() {
        emit viewAllClicked(m_category);
    });
    headerLayout->addWidget(m_viewAllButton);
    
    mainLayout->addLayout(headerLayout);
    
    // Separator
    auto* separator = new QFrame(this);
    separator->setFrameShape(QFrame::HLine);
    separator->setStyleSheet("background-color: #333;");
    separator->setFixedHeight(1);
    mainLayout->addWidget(separator);
    
    // Bars container
    m_barsLayout = new QVBoxLayout();
    m_barsLayout->setSpacing(6);
    mainLayout->addLayout(m_barsLayout);
    
    // Create bars
    for (int i = 0; i < MAX_BARS; ++i) {
        auto* bar = new ImpactBar(this);
        bar->setVisible(false);
        m_barsLayout->addWidget(bar);
        m_bars.push_back(bar);
        
        connect(bar, &ImpactBar::clicked, this, &ImpactCard::processClicked);
        connect(bar, &ImpactBar::detailsRequested, this, &ImpactCard::processDetailsRequested);
    }
    
    // Empty state
    m_emptyLabel = new QLabel("Collecting data...", this);
    m_emptyLabel->setStyleSheet("color: #666; font-style: italic; padding: 20px;");
    m_emptyLabel->setAlignment(Qt::AlignCenter);
    m_barsLayout->addWidget(m_emptyLabel);
    
    mainLayout->addStretch();
}

void ImpactCard::updateData(const std::vector<ProcessImpact>& impacts)
{
    bool hasData = !impacts.empty();
    m_emptyLabel->setVisible(!hasData);
    
    for (int i = 0; i < MAX_BARS; ++i) {
        if (i < static_cast<int>(impacts.size())) {
            const auto& impact = impacts[i];
            m_bars[i]->setProcessInfo(impact, m_category);
            m_bars[i]->setVisible(true);
            
            // Calculate bar value (normalize to 0-100)
            double barValue = 0;
            switch (m_category) {
                case ImpactCategory::BatteryDrain:
                    barValue = impact.batteryImpactScore;
                    break;
                case ImpactCategory::CpuUsage:
                    barValue = impact.avgCpuPercent;
                    break;
                case ImpactCategory::DiskIO:
                case ImpactCategory::DiskRead:
                case ImpactCategory::DiskWrite:
                    barValue = impact.diskImpactScore;
                    break;
                case ImpactCategory::MemoryUsage:
                    // Normalize: 4GB = 100%
                    barValue = std::min(100.0, static_cast<double>(impact.avgMemoryBytes) / (4.0 * 1024 * 1024 * 1024) * 100);
                    break;
                case ImpactCategory::GpuUsage:
                    barValue = impact.avgGpuPercent;
                    break;
                default:
                    barValue = impact.overallImpactScore;
                    break;
            }
            
            m_bars[i]->animateTo(barValue, 400);
        } else {
            m_bars[i]->setVisible(false);
            m_bars[i]->clear();
        }
    }
}

void ImpactCard::setCategory(ImpactCategory category)
{
    m_category = category;
}

void ImpactCard::clear()
{
    for (auto* bar : m_bars) {
        bar->clear();
        bar->setVisible(false);
    }
    m_emptyLabel->setVisible(true);
    m_emptyLabel->setText("No data available");
}

// ============================================================================
// ProcessImpactCard Implementation
// ============================================================================

ProcessImpactCard::ProcessImpactCard(QWidget* parent)
    : QFrame(parent)
{
    setupUi();
}

void ProcessImpactCard::setupUi()
{
    setFrameShape(QFrame::StyledPanel);
    setStyleSheet(R"(
        ProcessImpactCard {
            background-color: #2a2a2a;
            border-radius: 8px;
            border: 1px solid #3a3a3a;
        }
        ProcessImpactCard:hover {
            background-color: #333;
            border-color: #4a4a4a;
        }
    )");
    setMinimumHeight(80);
    setCursor(Qt::PointingHandCursor);
}

void ProcessImpactCard::setProcessData(const ProcessImpact& impact, ImpactCategory category)
{
    m_impact = impact;
    m_category = category;
    update();
}

void ProcessImpactCard::clear()
{
    m_impact = ProcessImpact();
    m_rank = 0;
    update();
}

void ProcessImpactCard::setHighlightOpacity(double opacity)
{
    m_highlightOpacity = std::clamp(opacity, 0.0, 1.0);
    update();
}

void ProcessImpactCard::enterEvent(QEnterEvent* event)
{
    m_hovered = true;
    update();
    QFrame::enterEvent(event);
}

void ProcessImpactCard::leaveEvent(QEvent* event)
{
    m_hovered = false;
    update();
    QFrame::leaveEvent(event);
}

void ProcessImpactCard::mousePressEvent(QMouseEvent* event)
{
    if (event->button() == Qt::LeftButton) {
        m_pressed = true;
        update();
    }
    QFrame::mousePressEvent(event);
}

void ProcessImpactCard::mouseReleaseEvent(QMouseEvent* event)
{
    if (event->button() == Qt::LeftButton && m_pressed) {
        m_pressed = false;
        if (rect().contains(event->pos()) && m_impact.pid != 0) {
            emit clicked(m_impact.pid);
        }
        update();
    }
    QFrame::mouseReleaseEvent(event);
}

void ProcessImpactCard::paintEvent(QPaintEvent* event)
{
    QFrame::paintEvent(event);
    
    QPainter painter(this);
    painter.setRenderHint(QPainter::Antialiasing);
    
    if (m_highlightOpacity > 0) {
        painter.setOpacity(m_highlightOpacity * 0.3);
        painter.fillRect(rect(), QColor(100, 181, 246));
        painter.setOpacity(1.0);
    }
}

void ProcessImpactCard::contextMenuEvent(QContextMenuEvent* event)
{
    if (m_impact.pid != 0) {
        emit contextMenuRequested(m_impact.pid, event->globalPos());
    }
    QFrame::contextMenuEvent(event);
}

void ProcessImpactCard::updateColors() {}

QString ProcessImpactCard::formatBytes(qint64 bytes) const
{
    return ProcessImpactMonitor::formatBytes(bytes);
}

QString ProcessImpactCard::formatBytesPerSec(qint64 bytesPerSec) const
{
    return ProcessImpactMonitor::formatBytes(bytesPerSec) + "/s";
}

QColor ProcessImpactCard::getImpactColor(double score) const
{
    if (score < 20) return QColor(76, 175, 80);
    if (score < 40) return QColor(139, 195, 74);
    if (score < 60) return QColor(255, 193, 7);
    if (score < 80) return QColor(255, 152, 0);
    return QColor(244, 67, 54);
}

// ============================================================================
// ImpactCategoryPanel Implementation
// ============================================================================

ImpactCategoryPanel::ImpactCategoryPanel(ImpactCategory category, QWidget* parent)
    : QFrame(parent)
    , m_category(category)
{
    setupUi();
}

void ImpactCategoryPanel::setupUi()
{
    setFrameShape(QFrame::StyledPanel);
    setStyleSheet(R"(
        ImpactCategoryPanel {
            background-color: #1e1e1e;
            border-radius: 12px;
            border: 1px solid #333;
        }
    )");
    
    auto* layout = new QVBoxLayout(this);
    layout->setContentsMargins(16, 12, 16, 16);
    layout->setSpacing(12);
    
    // Header
    auto* headerLayout = new QHBoxLayout();
    
    m_iconLabel = new QLabel(getCategoryIcon(), this);
    m_iconLabel->setStyleSheet("font-size: 20px;");
    headerLayout->addWidget(m_iconLabel);
    
    m_titleLabel = new QLabel(getCategoryTitle(), this);
    m_titleLabel->setStyleSheet("font-size: 14px; font-weight: 600; color: #fff;");
    headerLayout->addWidget(m_titleLabel);
    
    headerLayout->addStretch();
    
    m_viewAllButton = new QPushButton("View All", this);
    m_viewAllButton->setStyleSheet(R"(
        QPushButton {
            background: transparent;
            border: none;
            color: #64b5f6;
            font-size: 12px;
        }
        QPushButton:hover { color: #90caf9; }
    )");
    connect(m_viewAllButton, &QPushButton::clicked, this, [this]() {
        emit viewAllClicked(m_category);
    });
    headerLayout->addWidget(m_viewAllButton);
    
    layout->addLayout(headerLayout);
    
    // Cards
    for (int i = 0; i < 5; ++i) {
        auto* card = new ProcessImpactCard(this);
        card->setVisible(false);
        layout->addWidget(card);
        m_cards.push_back(card);
        
        connect(card, &ProcessImpactCard::clicked, this, &ImpactCategoryPanel::processClicked);
    }
    
    layout->addStretch();
}

void ImpactCategoryPanel::setData(const std::vector<ProcessImpact>& processes)
{
    for (size_t i = 0; i < m_cards.size(); ++i) {
        if (i < processes.size()) {
            m_cards[i]->setProcessData(processes[i], m_category);
            m_cards[i]->setVisible(true);
        } else {
            m_cards[i]->clear();
            m_cards[i]->setVisible(false);
        }
    }
}

void ImpactCategoryPanel::setTitle(const QString& title)
{
    m_titleLabel->setText(title);
}

void ImpactCategoryPanel::setIcon(const QString& iconPath)
{
    m_iconLabel->setText(iconPath);
}

QString ImpactCategoryPanel::getCategoryTitle() const
{
    switch (m_category) {
        case ImpactCategory::BatteryDrain: return "Battery Drainers";
        case ImpactCategory::CpuUsage: return "CPU Hogs";
        case ImpactCategory::DiskIO: return "Disk Hogs";
        case ImpactCategory::MemoryUsage: return "Memory Hogs";
        case ImpactCategory::GpuUsage: return "GPU Usage";
        default: return "Overall Impact";
    }
}

QString ImpactCategoryPanel::getCategoryIcon() const
{
    switch (m_category) {
        case ImpactCategory::BatteryDrain: return "🔋";
        case ImpactCategory::CpuUsage: return "💻";
        case ImpactCategory::DiskIO: return "💾";
        case ImpactCategory::MemoryUsage: return "🧠";
        case ImpactCategory::GpuUsage: return "🎮";
        default: return "⚡";
    }
}

QString ImpactCategoryPanel::getCategoryColor() const
{
    switch (m_category) {
        case ImpactCategory::BatteryDrain: return "#4caf50";
        case ImpactCategory::CpuUsage: return "#2196f3";
        case ImpactCategory::DiskIO: return "#ff9800";
        case ImpactCategory::MemoryUsage: return "#9c27b0";
        case ImpactCategory::GpuUsage: return "#e91e63";
        default: return "#607d8b";
    }
}

// ============================================================================
// ProcessImpactWidget Implementation
// ============================================================================

ProcessImpactWidget::ProcessImpactWidget(QWidget* parent)
    : QWidget(parent)
    , m_refreshTimer(new QTimer(this))
{
    setupUi();
    
    connect(m_refreshTimer, &QTimer::timeout, this, &ProcessImpactWidget::onRefreshTimer);
}

ProcessImpactWidget::~ProcessImpactWidget()
{
    if (m_ownsMonitor && m_monitor) {
        delete m_monitor;
    }
}

void ProcessImpactWidget::setupUi()
{
    m_mainLayout = new QVBoxLayout(this);
    m_mainLayout->setContentsMargins(16, 16, 16, 16);
    m_mainLayout->setSpacing(16);
    
    // Header
    m_headerWidget = new QWidget(this);
    auto* headerLayout = new QHBoxLayout(m_headerWidget);
    headerLayout->setContentsMargins(0, 0, 0, 0);
    headerLayout->setSpacing(16);
    
    // Title
    m_titleLabel = new QLabel("⚡ Process Impact Analysis", this);
    m_titleLabel->setStyleSheet("font-size: 18px; font-weight: 600; color: #fff;");
    headerLayout->addWidget(m_titleLabel);
    
    headerLayout->addStretch();
    
    // Status
    m_statusLabel = new QLabel("", this);
    m_statusLabel->setStyleSheet("color: #888; font-size: 12px;");
    headerLayout->addWidget(m_statusLabel);
    
    // Time window selector
    auto* windowLabel = new QLabel("Window:", this);
    windowLabel->setStyleSheet("color: #aaa;");
    headerLayout->addWidget(windowLabel);
    
    m_windowCombo = new QComboBox(this);
    m_windowCombo->addItem("1 minute", 60);
    m_windowCombo->addItem("5 minutes", 300);
    m_windowCombo->addItem("15 minutes", 900);
    m_windowCombo->addItem("30 minutes", 1800);
    m_windowCombo->setCurrentIndex(1); // Default 5 minutes
    m_windowCombo->setStyleSheet(R"(
        QComboBox {
            background-color: #2a2a2a;
            border: 1px solid #444;
            border-radius: 4px;
            padding: 4px 24px 4px 8px;
            color: #fff;
            min-width: 100px;
        }
        QComboBox:hover { border-color: #555; }
        QComboBox::drop-down {
            border: none;
            width: 20px;
        }
        QComboBox QAbstractItemView {
            background-color: #2a2a2a;
            border: 1px solid #444;
            selection-background-color: #3a3a3a;
            color: #fff;
        }
    )");
    connect(m_windowCombo, QOverload<int>::of(&QComboBox::currentIndexChanged),
            this, &ProcessImpactWidget::onWindowChanged);
    headerLayout->addWidget(m_windowCombo);
    
    // Refresh button
    m_refreshButton = new QPushButton("🔄 Refresh", this);
    m_refreshButton->setStyleSheet(R"(
        QPushButton {
            background-color: #2a2a2a;
            border: 1px solid #444;
            border-radius: 4px;
            padding: 6px 12px;
            color: #fff;
        }
        QPushButton:hover {
            background-color: #333;
            border-color: #555;
        }
        QPushButton:pressed {
            background-color: #252525;
        }
    )");
    connect(m_refreshButton, &QPushButton::clicked, this, &ProcessImpactWidget::refresh);
    headerLayout->addWidget(m_refreshButton);
    
    m_mainLayout->addWidget(m_headerWidget);
    
    // Scroll area for cards
    auto* scrollArea = new QScrollArea(this);
    scrollArea->setWidgetResizable(true);
    scrollArea->setFrameShape(QFrame::NoFrame);
    scrollArea->setHorizontalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
    scrollArea->setStyleSheet("QScrollArea { background: transparent; }");
    
    // Cards container
    m_cardsContainer = new QWidget();
    m_cardsContainer->setStyleSheet("background: transparent;");
    
    // Use grid layout for 2x2 on wide screens, vertical on narrow
    auto* gridLayout = new QGridLayout(m_cardsContainer);
    gridLayout->setSpacing(16);
    gridLayout->setContentsMargins(0, 0, 0, 0);
    
    // Create impact cards
    m_batteryCard = new ImpactCard("Battery Drainers", ImpactCategory::BatteryDrain, "🔋", this);
    m_cpuCard = new ImpactCard("CPU Hogs", ImpactCategory::CpuUsage, "💻", this);
    m_diskCard = new ImpactCard("Disk Hogs", ImpactCategory::DiskIO, "💾", this);
    m_memoryCard = new ImpactCard("Memory Hogs", ImpactCategory::MemoryUsage, "🧠", this);
    
    // Add to grid (2 columns)
    gridLayout->addWidget(m_batteryCard, 0, 0);
    gridLayout->addWidget(m_cpuCard, 0, 1);
    gridLayout->addWidget(m_diskCard, 1, 0);
    gridLayout->addWidget(m_memoryCard, 1, 1);
    
    // Column stretch
    gridLayout->setColumnStretch(0, 1);
    gridLayout->setColumnStretch(1, 1);
    
    scrollArea->setWidget(m_cardsContainer);
    m_mainLayout->addWidget(scrollArea, 1);
    
    // Connect card signals
    for (auto* card : {m_batteryCard, m_cpuCard, m_diskCard, m_memoryCard}) {
        connect(card, &ImpactCard::processClicked, this, &ProcessImpactWidget::onProcessClicked);
        connect(card, &ImpactCard::processDetailsRequested, this, &ProcessImpactWidget::processDetailsRequested);
        connect(card, &ImpactCard::viewAllClicked, this, &ProcessImpactWidget::onViewAllClicked);
    }
    
    // Footer
    m_footerWidget = new QWidget(this);
    auto* footerLayout = new QHBoxLayout(m_footerWidget);
    footerLayout->setContentsMargins(0, 0, 0, 0);
    
    m_legendLabel = new QLabel("Double-click a process for details", this);
    m_legendLabel->setStyleSheet("color: #666; font-size: 11px;");
    footerLayout->addWidget(m_legendLabel);
    
    footerLayout->addStretch();
    
    m_coverageLabel = new QLabel("", this);
    m_coverageLabel->setStyleSheet("color: #666; font-size: 11px;");
    footerLayout->addWidget(m_coverageLabel);
    
    m_mainLayout->addWidget(m_footerWidget);
}

void ProcessImpactWidget::setMonitor(ProcessImpactMonitor* monitor)
{
    if (m_ownsMonitor && m_monitor) {
        delete m_monitor;
    }
    
    m_monitor = monitor;
    m_ownsMonitor = false;
    
    if (m_monitor) {
        connect(m_monitor, &ProcessImpactMonitor::impactsUpdated,
                this, &ProcessImpactWidget::onMonitorUpdated);
    }
}

void ProcessImpactWidget::startMonitoring()
{
    if (!m_monitor) {
        m_monitor = new ProcessImpactMonitor(this);
        m_ownsMonitor = true;
        
        connect(m_monitor, &ProcessImpactMonitor::impactsUpdated,
                this, &ProcessImpactWidget::onMonitorUpdated);
    }
    
    // Set window from combo
    int windowSecs = m_windowCombo->currentData().toInt();
    m_monitor->setAnalysisWindow(windowSecs);
    
    m_monitor->start(1000);
    m_refreshTimer->start(m_refreshIntervalMs);
    
    m_statusLabel->setText("Monitoring...");
}

void ProcessImpactWidget::stopMonitoring()
{
    if (m_monitor) {
        m_monitor->stop();
    }
    m_refreshTimer->stop();
    m_statusLabel->setText("Stopped");
}

bool ProcessImpactWidget::isMonitoring() const
{
    return m_monitor && m_monitor->isRunning();
}

void ProcessImpactWidget::setRefreshInterval(int ms)
{
    m_refreshIntervalMs = ms;
    if (m_refreshTimer->isActive()) {
        m_refreshTimer->setInterval(ms);
    }
}

void ProcessImpactWidget::setShowSystemProcesses(bool show)
{
    m_showSystem = show;
    updateCards();
}

void ProcessImpactWidget::setAnalysisWindow(int seconds)
{
    if (m_monitor) {
        m_monitor->setAnalysisWindow(seconds);
    }
    
    // Update combo to match
    for (int i = 0; i < m_windowCombo->count(); ++i) {
        if (m_windowCombo->itemData(i).toInt() == seconds) {
            m_windowCombo->setCurrentIndex(i);
            break;
        }
    }
}

int ProcessImpactWidget::analysisWindow() const
{
    return m_monitor ? m_monitor->analysisWindow() : 300;
}

void ProcessImpactWidget::refresh()
{
    if (m_monitor) {
        m_monitor->recalculateImpacts();
    }
    updateCards();
}

void ProcessImpactWidget::onMonitorUpdated()
{
    updateCards();
}

void ProcessImpactWidget::onRefreshTimer()
{
    updateCards();
    
    // Update coverage indicator
    if (m_monitor) {
        double coverage = m_monitor->windowCoverage() * 100;
        m_coverageLabel->setText(QString("Data coverage: %1%").arg(coverage, 0, 'f', 0));
        
        int samples = m_monitor->totalSamples();
        m_statusLabel->setText(QString("Monitoring (%1 samples)").arg(samples));
    }
}

void ProcessImpactWidget::onDataUpdated()
{
    updateCards();
}

void ProcessImpactWidget::onCategoryChanged(int index)
{
    Q_UNUSED(index)
    updateCards();
}

void ProcessImpactWidget::onWindowChanged(int index)
{
    int windowSecs = m_windowCombo->itemData(index).toInt();
    if (m_monitor) {
        m_monitor->setAnalysisWindow(windowSecs);
    }
}

void ProcessImpactWidget::onProcessClicked(DWORD pid)
{
    emit processSelected(pid);
}

void ProcessImpactWidget::onViewAllClicked(ImpactCategory category)
{
    emit viewAllRequested(category);
}

void ProcessImpactWidget::updateCards()
{
    if (!m_monitor) return;
    
    // Get top 5 for each category
    auto batteryTop = m_monitor->getTopProcesses(ImpactCategory::BatteryDrain, 5, m_showSystem);
    auto cpuTop = m_monitor->getTopProcesses(ImpactCategory::CpuUsage, 5, m_showSystem);
    auto diskTop = m_monitor->getTopProcesses(ImpactCategory::DiskIO, 5, m_showSystem);
    auto memoryTop = m_monitor->getTopProcesses(ImpactCategory::MemoryUsage, 5, m_showSystem);
    
    // Update cards
    m_batteryCard->updateData(batteryTop);
    m_cpuCard->updateData(cpuTop);
    m_diskCard->updateData(diskTop);
    m_memoryCard->updateData(memoryTop);
}
