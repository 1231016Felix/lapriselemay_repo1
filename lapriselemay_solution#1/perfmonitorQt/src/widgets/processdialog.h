#pragma once

#include <QDialog>
#include <QLabel>
#include <QTabWidget>
#include <QtGlobal>

class ProcessDialog : public QDialog
{
    Q_OBJECT

public:
    explicit ProcessDialog(quint32 pid, const QString& processName, QWidget *parent = nullptr);
    ~ProcessDialog() override = default;

private:
    void setupUi();
    void loadProcessInfo();

    quint32 m_pid{0};
    QString m_processName;
    
    QLabel* m_pidLabel{nullptr};
    QLabel* m_nameLabel{nullptr};
    QLabel* m_pathLabel{nullptr};
    QLabel* m_cpuLabel{nullptr};
    QLabel* m_memoryLabel{nullptr};
    QLabel* m_threadsLabel{nullptr};
    QLabel* m_handlesLabel{nullptr};
    QLabel* m_userLabel{nullptr};
    QTabWidget* m_tabWidget{nullptr};
};
