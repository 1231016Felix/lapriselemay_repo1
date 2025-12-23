#include "processdialog.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QGridLayout>
#include <QGroupBox>
#include <QPushButton>

#ifdef _WIN32
#include <Windows.h>
#include <Psapi.h>
#include <TlHelp32.h>
#pragma comment(lib, "psapi.lib")
#endif

ProcessDialog::ProcessDialog(quint32 pid, const QString& processName, QWidget *parent)
    : QDialog(parent)
    , m_pid(pid)
    , m_processName(processName)
{
    setWindowTitle(QString("Process Details - %1").arg(processName));
    setMinimumSize(450, 400);
    
    setupUi();
    loadProcessInfo();
}

void ProcessDialog::setupUi()
{
    auto mainLayout = new QVBoxLayout(this);
    
    // Basic info group
    auto infoGroup = new QGroupBox(tr("Process Information"));
    auto infoLayout = new QGridLayout(infoGroup);
    
    infoLayout->addWidget(new QLabel(tr("Name:")), 0, 0);
    m_nameLabel = new QLabel();
    m_nameLabel->setStyleSheet("font-weight: bold;");
    infoLayout->addWidget(m_nameLabel, 0, 1);
    
    infoLayout->addWidget(new QLabel(tr("PID:")), 0, 2);
    m_pidLabel = new QLabel();
    infoLayout->addWidget(m_pidLabel, 0, 3);
    
    infoLayout->addWidget(new QLabel(tr("Path:")), 1, 0);
    m_pathLabel = new QLabel();
    m_pathLabel->setWordWrap(true);
    infoLayout->addWidget(m_pathLabel, 1, 1, 1, 3);
    
    infoLayout->addWidget(new QLabel(tr("User:")), 2, 0);
    m_userLabel = new QLabel();
    infoLayout->addWidget(m_userLabel, 2, 1, 1, 3);
    
    mainLayout->addWidget(infoGroup);
    
    // Performance group
    auto perfGroup = new QGroupBox(tr("Performance"));
    auto perfLayout = new QGridLayout(perfGroup);
    
    perfLayout->addWidget(new QLabel(tr("CPU:")), 0, 0);
    m_cpuLabel = new QLabel();
    perfLayout->addWidget(m_cpuLabel, 0, 1);
    
    perfLayout->addWidget(new QLabel(tr("Memory:")), 0, 2);
    m_memoryLabel = new QLabel();
    perfLayout->addWidget(m_memoryLabel, 0, 3);
    
    perfLayout->addWidget(new QLabel(tr("Threads:")), 1, 0);
    m_threadsLabel = new QLabel();
    perfLayout->addWidget(m_threadsLabel, 1, 1);
    
    perfLayout->addWidget(new QLabel(tr("Handles:")), 1, 2);
    m_handlesLabel = new QLabel();
    perfLayout->addWidget(m_handlesLabel, 1, 3);
    
    mainLayout->addWidget(perfGroup);
    
    // Tab widget for detailed info
    m_tabWidget = new QTabWidget();
    
    // Modules tab
    auto modulesWidget = new QWidget();
    auto modulesLayout = new QVBoxLayout(modulesWidget);
    modulesLayout->addWidget(new QLabel(tr("Loaded modules will appear here...")));
    m_tabWidget->addTab(modulesWidget, tr("Modules"));
    
    // Threads tab
    auto threadsWidget = new QWidget();
    auto threadsLayout = new QVBoxLayout(threadsWidget);
    threadsLayout->addWidget(new QLabel(tr("Thread information will appear here...")));
    m_tabWidget->addTab(threadsWidget, tr("Threads"));
    
    mainLayout->addWidget(m_tabWidget);
    
    // Buttons
    auto buttonLayout = new QHBoxLayout();
    buttonLayout->addStretch();
    
    auto closeBtn = new QPushButton(tr("Close"));
    connect(closeBtn, &QPushButton::clicked, this, &QDialog::accept);
    buttonLayout->addWidget(closeBtn);
    
    mainLayout->addLayout(buttonLayout);
}

void ProcessDialog::loadProcessInfo()
{
    m_nameLabel->setText(m_processName);
    m_pidLabel->setText(QString::number(m_pid));
    
#ifdef _WIN32
    HANDLE hProcess = OpenProcess(
        PROCESS_QUERY_INFORMATION | PROCESS_VM_READ,
        FALSE, static_cast<DWORD>(m_pid)
    );
    
    if (hProcess) {
        // Get path
        wchar_t path[MAX_PATH] = {0};
        DWORD size = MAX_PATH;
        if (QueryFullProcessImageNameW(hProcess, 0, path, &size)) {
            m_pathLabel->setText(QString::fromWCharArray(path));
        } else {
            m_pathLabel->setText(tr("N/A"));
        }
        
        // Get memory info
        PROCESS_MEMORY_COUNTERS_EX pmc;
        if (GetProcessMemoryInfo(hProcess, 
            reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&pmc), sizeof(pmc))) {
            double mb = pmc.WorkingSetSize / (1024.0 * 1024.0);
            m_memoryLabel->setText(QString("%1 MB").arg(mb, 0, 'f', 1));
        }
        
        // Get handle count
        DWORD handleCount = 0;
        if (GetProcessHandleCount(hProcess, &handleCount)) {
            m_handlesLabel->setText(QString::number(handleCount));
        }
        
        CloseHandle(hProcess);
    } else {
        m_pathLabel->setText(tr("Access Denied"));
        m_memoryLabel->setText(tr("N/A"));
        m_handlesLabel->setText(tr("N/A"));
    }
    
    // Get thread count from snapshot
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
    if (snapshot != INVALID_HANDLE_VALUE) {
        THREADENTRY32 te;
        te.dwSize = sizeof(te);
        
        int threadCount = 0;
        if (Thread32First(snapshot, &te)) {
            do {
                if (te.th32OwnerProcessID == static_cast<DWORD>(m_pid)) {
                    threadCount++;
                }
            } while (Thread32Next(snapshot, &te));
        }
        m_threadsLabel->setText(QString::number(threadCount));
        CloseHandle(snapshot);
    }
    
    m_cpuLabel->setText(tr("Calculating..."));
    m_userLabel->setText(tr("N/A"));
#else
    m_pathLabel->setText(tr("N/A"));
    m_memoryLabel->setText(tr("N/A"));
    m_handlesLabel->setText(tr("N/A"));
    m_threadsLabel->setText(tr("N/A"));
    m_cpuLabel->setText(tr("N/A"));
    m_userLabel->setText(tr("N/A"));
#endif
}
