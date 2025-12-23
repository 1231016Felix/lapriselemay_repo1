#pragma once

#include <QObject>
#include <QSystemTrayIcon>
#include <QMenu>
#include <memory>

class SystemTrayManager : public QObject
{
    Q_OBJECT

public:
    explicit SystemTrayManager(QWidget *parent = nullptr);
    ~SystemTrayManager() override;

    void show();
    void hide();
    [[nodiscard]] bool isVisible() const;
    
    void updateTooltip(double cpuUsage, double memUsage);
    void showNotification(const QString& title, const QString& message,
                         QSystemTrayIcon::MessageIcon icon = QSystemTrayIcon::Information);

signals:
    void activated(QSystemTrayIcon::ActivationReason reason);
    void showRequested();
    void exitRequested();

private:
    void createContextMenu();
    QIcon createTrayIcon(double cpuUsage);

    std::unique_ptr<QSystemTrayIcon> m_trayIcon;
    std::unique_ptr<QMenu> m_contextMenu;
    QWidget* m_parent{nullptr};
};
