#include "toolswidget.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QScrollArea>
#include <QPainter>
#include <QMouseEvent>
#include <QGraphicsDropShadowEffect>
#include <QPropertyAnimation>
#include <QApplication>

// ============================================================================
// ToolCard Implementation
// ============================================================================

ToolCard::ToolCard(const QString& icon, const QString& title, 
                   const QString& description, const QString& shortcut,
                   QWidget* parent)
    : QFrame(parent)
{
    setFixedSize(280, 140);
    setCursor(Qt::PointingHandCursor);
    setFocusPolicy(Qt::StrongFocus);
    
    auto layout = new QVBoxLayout(this);
    layout->setContentsMargins(20, 16, 20, 16);
    layout->setSpacing(8);
    
    // Top row: icon + title + admin badge
    auto topLayout = new QHBoxLayout();
    topLayout->setSpacing(12);
    
    m_iconLabel = new QLabel(icon);
    m_iconLabel->setStyleSheet("font-size: 28px; font-family: 'Segoe UI Emoji'; background: transparent;");
    topLayout->addWidget(m_iconLabel);
    
    m_titleLabel = new QLabel(title);
    m_titleLabel->setStyleSheet("font-size: 15px; font-weight: 600; color: #e0e0e0; background: transparent;");
    topLayout->addWidget(m_titleLabel);
    
    topLayout->addStretch();
    
    // Admin badge (hidden by default)
    m_adminBadge = new QLabel("🛡");
    m_adminBadge->setToolTip(tr("Requires Administrator"));
    m_adminBadge->setStyleSheet("font-size: 14px; font-family: 'Segoe UI Emoji'; background: transparent;");
    m_adminBadge->setVisible(false);
    topLayout->addWidget(m_adminBadge);
    
    layout->addLayout(topLayout);
    
    // Description
    m_descLabel = new QLabel(description);
    m_descLabel->setWordWrap(true);
    m_descLabel->setStyleSheet("font-size: 12px; color: #a0a0a0; background: transparent;");
    layout->addWidget(m_descLabel);
    
    layout->addStretch();
    
    // Shortcut at bottom
    if (!shortcut.isEmpty()) {
        m_shortcutLabel = new QLabel(shortcut);
        m_shortcutLabel->setStyleSheet("font-size: 11px; color: #606060; font-family: 'Consolas', monospace; background: transparent;");
        m_shortcutLabel->setAlignment(Qt::AlignRight);
        layout->addWidget(m_shortcutLabel);
    }
    
    // Shadow effect
    auto shadow = new QGraphicsDropShadowEffect(this);
    shadow->setBlurRadius(15);
    shadow->setColor(QColor(0, 0, 0, 60));
    shadow->setOffset(0, 4);
    setGraphicsEffect(shadow);
    
    updateStyle();
}

void ToolCard::setEnabled(bool enabled)
{
    QFrame::setEnabled(enabled);
    setCursor(enabled ? Qt::PointingHandCursor : Qt::ForbiddenCursor);
    updateStyle();
}

void ToolCard::setNeedsAdmin(bool needs)
{
    m_requiresAdmin = needs;
    m_adminBadge->setVisible(needs);
}

void ToolCard::enterEvent(QEnterEvent* event)
{
    m_hovered = true;
    updateStyle();
    QFrame::enterEvent(event);
}

void ToolCard::leaveEvent(QEvent* event)
{
    m_hovered = false;
    m_pressed = false;
    updateStyle();
    QFrame::leaveEvent(event);
}

void ToolCard::mousePressEvent(QMouseEvent* event)
{
    if (event->button() == Qt::LeftButton && isEnabled()) {
        m_pressed = true;
        updateStyle();
    }
    QFrame::mousePressEvent(event);
}

void ToolCard::mouseReleaseEvent(QMouseEvent* event)
{
    if (event->button() == Qt::LeftButton && m_pressed && isEnabled()) {
        m_pressed = false;
        updateStyle();
        if (rect().contains(event->pos())) {
            emit clicked();
        }
    }
    QFrame::mouseReleaseEvent(event);
}

void ToolCard::paintEvent(QPaintEvent* event)
{
    Q_UNUSED(event);
    
    QPainter painter(this);
    painter.setRenderHint(QPainter::Antialiasing);
    
    // Background
    QColor bgColor;
    if (!isEnabled()) {
        bgColor = QColor(40, 40, 45);
    } else if (m_pressed) {
        bgColor = QColor(55, 55, 65);
    } else if (m_hovered) {
        bgColor = QColor(50, 50, 58);
    } else {
        bgColor = QColor(42, 42, 50);
    }
    
    // Border
    QColor borderColor = m_hovered ? QColor(80, 80, 95) : QColor(55, 55, 65);
    
    painter.setPen(QPen(borderColor, 1));
    painter.setBrush(bgColor);
    painter.drawRoundedRect(rect().adjusted(1, 1, -1, -1), 12, 12);
    
    // Accent line on hover
    if (m_hovered && isEnabled()) {
        painter.setPen(Qt::NoPen);
        painter.setBrush(QColor(0, 120, 215));
        painter.drawRoundedRect(QRect(0, 0, 4, height()), 2, 2);
    }
}

void ToolCard::updateStyle()
{
    // Update label colors based on state
    if (isEnabled()) {
        if (m_hovered) {
            m_titleLabel->setStyleSheet("font-size: 15px; font-weight: 600; color: #ffffff; background: transparent;");
            m_descLabel->setStyleSheet("font-size: 12px; color: #b0b0b0; background: transparent;");
        } else {
            m_titleLabel->setStyleSheet("font-size: 15px; font-weight: 600; color: #e0e0e0; background: transparent;");
            m_descLabel->setStyleSheet("font-size: 12px; color: #a0a0a0; background: transparent;");
        }
    } else {
        m_titleLabel->setStyleSheet("font-size: 15px; font-weight: 600; color: #707070; background: transparent;");
        m_descLabel->setStyleSheet("font-size: 12px; color: #606060; background: transparent;");
    }
    
    update();
}

// ============================================================================
// ToolsWidget Implementation
// ============================================================================

ToolsWidget::ToolsWidget(QWidget* parent)
    : QWidget(parent)
{
    setupUi();
    createToolCards();
}

void ToolsWidget::setupUi()
{
    // Set background for the entire widget
    setAutoFillBackground(true);
    setStyleSheet("QWidget#ToolsWidget { background-color: #1e1e24; }");
    setObjectName("ToolsWidget");
    
    auto mainLayout = new QVBoxLayout(this);
    mainLayout->setContentsMargins(30, 30, 30, 30);
    mainLayout->setSpacing(20);
    
    // Header
    auto headerLayout = new QVBoxLayout();
    headerLayout->setSpacing(8);
    
    auto titleLabel = new QLabel(tr("🧰 System Tools"));
    titleLabel->setStyleSheet(
        "font-size: 24px; font-weight: bold; color: #ffffff; font-family: 'Segoe UI Emoji', 'Segoe UI'; background: transparent;"
    );
    headerLayout->addWidget(titleLabel);
    
    auto subtitleLabel = new QLabel(tr("Optimize, clean, and monitor your system"));
    subtitleLabel->setStyleSheet("font-size: 14px; color: #888888; background: transparent;");
    headerLayout->addWidget(subtitleLabel);
    
    mainLayout->addLayout(headerLayout);
    
    // Separator line
    auto separator = new QFrame();
    separator->setFrameShape(QFrame::HLine);
    separator->setStyleSheet("background-color: #3a3a45; max-height: 1px;");
    mainLayout->addWidget(separator);
    
    // Scroll area for cards
    auto scrollArea = new QScrollArea();
    scrollArea->setWidgetResizable(true);
    scrollArea->setFrameShape(QFrame::NoFrame);
    scrollArea->setStyleSheet(
        "QScrollArea { background-color: #1e1e24; border: none; }"
        "QScrollBar:vertical { background: #2a2a32; width: 8px; border-radius: 4px; }"
        "QScrollBar::handle:vertical { background: #4a4a55; border-radius: 4px; min-height: 30px; }"
        "QScrollBar::handle:vertical:hover { background: #5a5a65; }"
        "QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical { height: 0; }"
    );
    
    auto scrollContent = new QWidget();
    scrollContent->setStyleSheet("background-color: #1e1e24;");
    
    m_gridLayout = new QGridLayout(scrollContent);
    m_gridLayout->setContentsMargins(0, 10, 10, 10);
    m_gridLayout->setSpacing(20);
    m_gridLayout->setAlignment(Qt::AlignTop | Qt::AlignLeft);
    
    scrollArea->setWidget(scrollContent);
    mainLayout->addWidget(scrollArea);
}

ToolCard* ToolsWidget::createCard(const QString& icon, const QString& title,
                                   const QString& description, const QString& shortcut,
                                   bool needsAdmin)
{
    auto card = new ToolCard(icon, title, description, shortcut, this);
    card->setNeedsAdmin(needsAdmin);
    m_cards.push_back(card);
    return card;
}

void ToolsWidget::createToolCards()
{
    int row = 0, col = 0;
    const int maxCols = 3;
    
    auto addCard = [&](ToolCard* card) {
        m_gridLayout->addWidget(card, row, col);
        col++;
        if (col >= maxCols) {
            col = 0;
            row++;
        }
    };
    
    // === System Optimization Section ===
    auto sectionLabel1 = new QLabel(tr("⚡ System Optimization"));
    sectionLabel1->setStyleSheet("font-size: 13px; font-weight: 600; color: #0078d7; margin-top: 10px; font-family: 'Segoe UI Emoji', 'Segoe UI'; background: transparent;");
    m_gridLayout->addWidget(sectionLabel1, row, 0, 1, maxCols);
    row++; col = 0;
    
    // Energy Mode
    auto energyCard = createCard(
        "⚡", tr("Energy Mode"),
        tr("Stop non-essential Windows services to free resources and improve performance."),
        "Ctrl+E", true
    );
    connect(energyCard, &ToolCard::clicked, this, &ToolsWidget::energyModeRequested);
    addCard(energyCard);
    
    // Purge Memory
    auto purgeCard = createCard(
        "🧹", tr("Purge Memory"),
        tr("Free up system RAM by clearing standby list and emptying working sets."),
        "", true
    );
    connect(purgeCard, &ToolCard::clicked, this, &ToolsWidget::purgeMemoryRequested);
    addCard(purgeCard);
    
    // Startup Manager
    auto startupCard = createCard(
        "🚀", tr("Startup Manager"),
        tr("Control which programs run at Windows startup to speed up boot time."),
        "Ctrl+S", false
    );
    connect(startupCard, &ToolCard::clicked, this, &ToolsWidget::startupManagerRequested);
    addCard(startupCard);
    
    // === Cleaning Section ===
    row++; col = 0;
    auto sectionLabel2 = new QLabel(tr("🗑️ Cleaning & Maintenance"));
    sectionLabel2->setStyleSheet("font-size: 13px; font-weight: 600; color: #0078d7; margin-top: 20px; font-family: 'Segoe UI Emoji', 'Segoe UI'; background: transparent;");
    m_gridLayout->addWidget(sectionLabel2, row, 0, 1, maxCols);
    row++; col = 0;
    
    // System Cleaner
    auto cleanerCard = createCard(
        "🧹", tr("System Cleaner"),
        tr("Remove temporary files, browser cache, and other junk to free disk space."),
        "Ctrl+L", false
    );
    connect(cleanerCard, &ToolCard::clicked, this, &ToolsWidget::systemCleanerRequested);
    addCard(cleanerCard);
    
    // === Analysis Section ===
    row++; col = 0;
    auto sectionLabel3 = new QLabel(tr("🔍 Analysis & Diagnostics"));
    sectionLabel3->setStyleSheet("font-size: 13px; font-weight: 600; color: #0078d7; margin-top: 20px; font-family: 'Segoe UI Emoji', 'Segoe UI'; background: transparent;");
    m_gridLayout->addWidget(sectionLabel3, row, 0, 1, maxCols);
    row++; col = 0;
    
    // Storage Health
    auto storageCard = createCard(
        "💾", tr("Storage Health"),
        tr("Check SSD/HDD health using S.M.A.R.T. data and NVMe diagnostics."),
        "Ctrl+H", true
    );
    connect(storageCard, &ToolCard::clicked, this, &ToolsWidget::storageHealthRequested);
    addCard(storageCard);
    
    // Detailed Memory
    auto memoryCard = createCard(
        "🧠", tr("Detailed Memory"),
        tr("Analyze RAM usage per process, detect memory leaks, view working sets."),
        "Ctrl+M", false
    );
    connect(memoryCard, &ToolCard::clicked, this, &ToolsWidget::detailedMemoryRequested);
    addCard(memoryCard);
    
    // Add stretch to push cards to top
    m_gridLayout->setRowStretch(row + 1, 1);
}
