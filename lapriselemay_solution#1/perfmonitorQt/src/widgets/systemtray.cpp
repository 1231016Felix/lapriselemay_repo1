#include "systemtray.h"

#include <QApplication>
#include <QPainter>
#include <QPixmap>

SystemTrayManager::SystemTrayManager(QWidget *parent)
    : QObject(parent)
    , m_parent(parent)
{
    m_trayIcon = std::make_unique<QSystemTrayIcon>(this);
    
    // Set initial icon
    m_trayIcon->setIcon(createTrayIcon(0));
    m_trayIcon->setToolTip("PerfMonitorQt\nCPU: 0%\nMemory: 0%");
    
    createContextMenu();
    
    connect(m_trayIcon.get(), &QSystemTrayIcon::activated,
            this, &SystemTrayManager::activated);
    
    m_trayIcon->show();
}

SystemTrayManager::~SystemTrayManager() = default;

void SystemTrayManager::createContextMenu()
{
    m_contextMenu = std::make_unique<QMenu>();
    
    auto showAction = m_contextMenu->addAction("Show Window");
    connect(showAction, &QAction::triggered, this, &SystemTrayManager::showRequested);
    
    m_contextMenu->addSeparator();
    
    auto cpuAction = m_contextMenu->addAction("CPU: ---%");
    cpuAction->setEnabled(false);
    cpuAction->setObjectName("cpuAction");
    
    auto memAction = m_contextMenu->addAction("Memory: ---%");
    memAction->setEnabled(false);
    memAction->setObjectName("memAction");
    
    m_contextMenu->addSeparator();
    
    auto exitAction = m_contextMenu->addAction("Exit");
    connect(exitAction, &QAction::triggered, this, &SystemTrayManager::exitRequested);
    
    m_trayIcon->setContextMenu(m_contextMenu.get());
}

void SystemTrayManager::show()
{
    m_trayIcon->show();
}

void SystemTrayManager::hide()
{
    m_trayIcon->hide();
}

bool SystemTrayManager::isVisible() const
{
    return m_trayIcon->isVisible();
}

void SystemTrayManager::updateTooltip(double cpuUsage, double memUsage)
{
    // Update tooltip
    QString tooltip = QString("PerfMonitorQt\nCPU: %1%\nMemory: %2%")
        .arg(cpuUsage, 0, 'f', 1)
        .arg(memUsage, 0, 'f', 1);
    m_trayIcon->setToolTip(tooltip);
    
    // Update icon based on CPU usage
    m_trayIcon->setIcon(createTrayIcon(cpuUsage));
    
    // Update context menu items
    auto cpuAction = m_contextMenu->findChild<QAction*>("cpuAction");
    if (cpuAction) {
        cpuAction->setText(QString("CPU: %1%").arg(cpuUsage, 0, 'f', 1));
    }
    
    auto memAction = m_contextMenu->findChild<QAction*>("memAction");
    if (memAction) {
        memAction->setText(QString("Memory: %1%").arg(memUsage, 0, 'f', 1));
    }
}

void SystemTrayManager::showNotification(const QString& title, const QString& message,
                                         QSystemTrayIcon::MessageIcon icon)
{
    m_trayIcon->showMessage(title, message, icon, 5000);
}

QIcon SystemTrayManager::createTrayIcon(double cpuUsage)
{
    const int size = 32;
    QPixmap pixmap(size, size);
    pixmap.fill(Qt::transparent);
    
    QPainter painter(&pixmap);
    painter.setRenderHint(QPainter::Antialiasing);
    
    // Background circle
    QColor bgColor(30, 30, 30);
    painter.setBrush(bgColor);
    painter.setPen(Qt::NoPen);
    painter.drawEllipse(1, 1, size - 2, size - 2);
    
    // Usage arc
    QColor usageColor;
    if (cpuUsage < 50) {
        usageColor = QColor(0, 200, 83);  // Green
    } else if (cpuUsage < 80) {
        usageColor = QColor(255, 193, 7);  // Yellow
    } else {
        usageColor = QColor(244, 67, 54);  // Red
    }
    
    // Draw arc based on usage
    int arcAngle = static_cast<int>(cpuUsage * 360 / 100 * 16);  // Qt uses 1/16th degree
    QPen arcPen(usageColor, 3);
    painter.setPen(arcPen);
    painter.drawArc(4, 4, size - 8, size - 8, 90 * 16, -arcAngle);
    
    // Center text
    painter.setPen(Qt::white);
    QFont font = painter.font();
    font.setPixelSize(10);
    font.setBold(true);
    painter.setFont(font);
    
    QString text = QString::number(static_cast<int>(cpuUsage));
    painter.drawText(pixmap.rect(), Qt::AlignCenter, text);
    
    return QIcon(pixmap);
}
