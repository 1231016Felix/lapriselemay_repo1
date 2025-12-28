#pragma once

#include <QWidget>
#include <QGridLayout>
#include <QPushButton>
#include <QLabel>
#include <QFrame>
#include <QEnterEvent>
#include <vector>
#include <functional>

/**
 * @brief A modern card-style button for tools
 */
class ToolCard : public QFrame
{
    Q_OBJECT

public:
    ToolCard(const QString& icon, const QString& title, 
             const QString& description, const QString& shortcut = QString(),
             QWidget* parent = nullptr);
    
    void setEnabled(bool enabled);
    void setNeedsAdmin(bool needs);

signals:
    void clicked();

protected:
    void enterEvent(QEnterEvent* event) override;
    void leaveEvent(QEvent* event) override;
    void mousePressEvent(QMouseEvent* event) override;
    void mouseReleaseEvent(QMouseEvent* event) override;
    void paintEvent(QPaintEvent* event) override;

private:
    void updateStyle();
    
    QLabel* m_iconLabel{nullptr};
    QLabel* m_titleLabel{nullptr};
    QLabel* m_descLabel{nullptr};
    QLabel* m_shortcutLabel{nullptr};
    QLabel* m_adminBadge{nullptr};
    
    bool m_hovered{false};
    bool m_pressed{false};
    bool m_requiresAdmin{false};
};

/**
 * @brief Widget displaying all available tools in a grid of cards
 */
class ToolsWidget : public QWidget
{
    Q_OBJECT

public:
    explicit ToolsWidget(QWidget* parent = nullptr);

signals:
    // Tool signals
    void startupManagerRequested();
    void systemCleanerRequested();
    void storageHealthRequested();
    void detailedMemoryRequested();
    void energyModeRequested();
    void energyModeConfigRequested();
    void purgeMemoryRequested();
    
    // New features
    void servicesManagerRequested();
    void metricsHistoryRequested();
    void diskScannerRequested();
    void networkSpeedTestRequested();

private:
    void setupUi();
    void createToolCards();
    ToolCard* createCard(const QString& icon, const QString& title,
                         const QString& description, const QString& shortcut,
                         bool needsAdmin = false);
    
    QGridLayout* m_gridLayout{nullptr};
    std::vector<ToolCard*> m_cards;
};
