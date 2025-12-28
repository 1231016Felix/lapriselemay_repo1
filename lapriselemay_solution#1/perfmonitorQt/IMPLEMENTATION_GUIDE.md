# IMPLEMENTATION COMPLÈTE - Services Windows & Historique Avancé

## Fichiers déjà existants et fonctionnels ✅

1. **servicemonitor.h/cpp** - Complet
   - Énumération des services
   - Start/Stop/Restart
   - Détection crashs
   - Monitoring ressources
   
2. **servicehistory.h/cpp** - Complet
   - Base SQLite
   - Recording snapshots
   - Export CSV/JSON
   
3. **metricshistory.h/cpp** - Complet
   - Historique métrique système
   - Agrégation données
   - Comparaison périodes
   
4. **servicesdialog.h/cpp** - Complet
   - UI gestion services
   - Filtres et recherche
   - Historique crashs
   
5. **historydialog.h/cpp** - Complet
   - Graphiques historique
   - Export données
   
6. **interactivechart.h/cpp** - Complet
   - Graphiques interactifs
   - Zoom/Pan
   - Export images

## Fichiers à créer

### 1. timerangeselector.h
```cpp
#pragma once
#include <QWidget>
#include <QComboBox>
#include <QDateTimeEdit>
#include <QPushButton>
#include <QDateTime>

class TimeRangeSelector : public QWidget
{
    Q_OBJECT
public:
    explicit TimeRangeSelector(QWidget* parent = nullptr);
    
    void setTimeRange(const QDateTime& start, const QDateTime& end);
    std::pair<QDateTime, QDateTime> timeRange() const;
    
signals:
    void timeRangeChanged(const QDateTime& start, const QDateTime& end);
    
private slots:
    void onPresetChanged(int index);
    void onCustomRangeChanged();
    void onApplyClicked();
    
private:
    void setupUi();
    void updateCustomEdits();
    
    QComboBox* m_presetCombo{nullptr};
    QDateTimeEdit* m_startEdit{nullptr};
    QDateTimeEdit* m_endEdit{nullptr};
    QPushButton* m_applyButton{nullptr};
    
    QDateTime m_startTime;
    QDateTime m_endTime;
};
```

### 2. timerangeselector.cpp
```cpp
#include "timerangeselector.h"
#include <QHBoxLayout>
#include <QLabel>

TimeRangeSelector::TimeRangeSelector(QWidget* parent)
    : QWidget(parent)
    , m_startTime(QDateTime::currentDateTime().addDays(-1))
    , m_endTime(QDateTime::currentDateTime())
{
    setupUi();
}

void TimeRangeSelector::setupUi()
{
    QHBoxLayout* layout = new QHBoxLayout(this);
    layout->setContentsMargins(0, 0, 0, 0);
    
    // Preset selector
    m_presetCombo = new QComboBox();
    m_presetCombo->addItem(tr("Last Hour"), 1);
    m_presetCombo->addItem(tr("Last 6 Hours"), 6);
    m_presetCombo->addItem(tr("Last 24 Hours"), 24);
    m_presetCombo->addItem(tr("Last 7 Days"), 168);
    m_presetCombo->addItem(tr("Last 30 Days"), 720);
    m_presetCombo->addItem(tr("Custom..."), -1);
    m_presetCombo->setCurrentIndex(2); // Default to 24 hours
    connect(m_presetCombo, QOverload<int>::of(&QComboBox::currentIndexChanged),
            this, &TimeRangeSelector::onPresetChanged);
    
    // Custom range editors
    m_startEdit = new QDateTimeEdit(m_startTime);
    m_startEdit->setDisplayFormat("dd/MM/yyyy HH:mm");
    m_startEdit->setCalendarPopup(true);
    m_startEdit->setEnabled(false);
    connect(m_startEdit, &QDateTimeEdit::dateTimeChanged,
            this, &TimeRangeSelector::onCustomRangeChanged);
    
    m_endEdit = new QDateTimeEdit(m_endTime);
    m_endEdit->setDisplayFormat("dd/MM/yyyy HH:mm");
    m_endEdit->setCalendarPopup(true);
    m_endEdit->setEnabled(false);
    connect(m_endEdit, &QDateTimeEdit::dateTimeChanged,
            this, &TimeRangeSelector::onCustomRangeChanged);
    
    m_applyButton = new QPushButton(tr("Apply"));
    m_applyButton->setEnabled(false);
    connect(m_applyButton, &QPushButton::clicked,
            this, &TimeRangeSelector::onApplyClicked);
    
    layout->addWidget(m_presetCombo);
    layout->addWidget(new QLabel(tr("From:")));
    layout->addWidget(m_startEdit);
    layout->addWidget(new QLabel(tr("To:")));
    layout->addWidget(m_endEdit);
    layout->addWidget(m_applyButton);
}

void TimeRangeSelector::setTimeRange(const QDateTime& start, const QDateTime& end)
{
    m_startTime = start;
    m_endTime = end;
    m_startEdit->setDateTime(start);
    m_endEdit->setDateTime(end);
}

std::pair<QDateTime, QDateTime> TimeRangeSelector::timeRange() const
{
    return {m_startTime, m_endTime};
}

void TimeRangeSelector::onPresetChanged(int index)
{
    int hours = m_presetCombo->currentData().toInt();
    
    bool isCustom = (hours == -1);
    m_startEdit->setEnabled(isCustom);
    m_endEdit->setEnabled(isCustom);
    m_applyButton->setEnabled(isCustom);
    
    if (!isCustom) {
        m_endTime = QDateTime::currentDateTime();
        m_startTime = m_endTime.addSecs(-hours * 3600);
        updateCustomEdits();
        emit timeRangeChanged(m_startTime, m_endTime);
    }
}

void TimeRangeSelector::onCustomRangeChanged()
{
    m_applyButton->setEnabled(true);
}

void TimeRangeSelector::onApplyClicked()
{
    m_startTime = m_startEdit->dateTime();
    m_endTime = m_endEdit->dateTime();
    m_applyButton->setEnabled(false);
    emit timeRangeChanged(m_startTime, m_endTime);
}

void TimeRangeSelector::updateCustomEdits()
{
    m_startEdit->setDateTime(m_startTime);
    m_endEdit->setDateTime(m_endTime);
}
```

### 3. comparisonchart.h
```cpp
#pragma once
#include <QWidget>
#include <QChart>
#include <QChartView>
#include <QBarSeries>
#include <QBarSet>
#include <QBarCategoryAxis>
#include <QValueAxis>
#include <vector>

class ComparisonChart : public QWidget
{
    Q_OBJECT
public:
    explicit ComparisonChart(QWidget* parent = nullptr);
    
    void setData(const QStringList& categories,
                 const std::vector<double>& period1Values,
                 const std::vector<double>& period2Values,
                 const QString& period1Name,
                 const QString& period2Name);
    
    void setValueSuffix(const QString& suffix) { m_valueSuffix = suffix; }
    void setDarkTheme(bool dark);
    
private:
    void setupChart();
    
    QChart* m_chart{nullptr};
    QChartView* m_chartView{nullptr};
    QBarCategoryAxis* m_axisX{nullptr};
    QValueAxis* m_axisY{nullptr};
    QString m_valueSuffix;
    bool m_darkTheme{true};
};
```

### 4. comparisonchart.cpp  
```cpp
#include "comparisonchart.h"
#include <QVBoxLayout>
#include <QPainter>

ComparisonChart::ComparisonChart(QWidget* parent)
    : QWidget(parent)
{
    setupChart();
}

void ComparisonChart::setupChart()
{
    m_chart = new QChart();
    m_chart->setAnimationOptions(QChart::SeriesAnimations);
    m_chart->legend()->setVisible(true);
    m_chart->legend()->setAlignment(Qt::AlignBottom);
    
    m_chartView = new QChartView(m_chart, this);
    m_chartView->setRenderHint(QPainter::Antialiasing);
    
    QVBoxLayout* layout = new QVBoxLayout(this);
    layout->setContentsMargins(0, 0, 0, 0);
    layout->addWidget(m_chartView);
    
    setDarkTheme(true);
}

void ComparisonChart::setData(const QStringList& categories,
                               const std::vector<double>& period1Values,
                               const std::vector<double>& period2Values,
                               const QString& period1Name,
                               const QString& period2Name)
{
    m_chart->removeAllSeries();
    
    QBarSet* set1 = new QBarSet(period1Name);
    QBarSet* set2 = new QBarSet(period2Name);
    
    for (double val : period1Values) {
        *set1 << val;
    }
    for (double val : period2Values) {
        *set2 << val;
    }
    
    set1->setColor(QColor(0, 120, 215));
    set2->setColor(QColor(76, 175, 80));
    
    QBarSeries* series = new QBarSeries();
    series->append(set1);
    series->append(set2);
    
    m_chart->addSeries(series);
    
    // Update axes
    if (m_axisX) m_chart->removeAxis(m_axisX);
    if (m_axisY) m_chart->removeAxis(m_axisY);
    
    m_axisX = new QBarCategoryAxis();
    m_axisX->append(categories);
    m_chart->addAxis(m_axisX, Qt::AlignBottom);
    series->attachAxis(m_axisX);
    
    double maxVal = 0;
    for (double v : period1Values) maxVal = qMax(maxVal, v);
    for (double v : period2Values) maxVal = qMax(maxVal, v);
    
    m_axisY = new QValueAxis();
    m_axisY->setRange(0, maxVal * 1.1);
    m_chart->addAxis(m_axisY, Qt::AlignLeft);
    series->attachAxis(m_axisY);
    
    if (m_darkTheme) {
        m_axisX->setLabelsColor(Qt::white);
        m_axisY->setLabelsColor(Qt::white);
        m_axisX->setGridLineColor(QColor(60, 60, 60));
        m_axisY->setGridLineColor(QColor(60, 60, 60));
    }
}

void ComparisonChart::setDarkTheme(bool dark)
{
    m_darkTheme = dark;
    
    if (dark) {
        m_chart->setBackgroundBrush(QColor(30, 30, 30));
        m_chart->setPlotAreaBackgroundBrush(QColor(25, 25, 25));
        m_chart->setPlotAreaBackgroundVisible(true);
        m_chart->setTitleBrush(Qt::white);
        m_chart->legend()->setLabelColor(Qt::white);
    } else {
        m_chart->setBackgroundBrush(Qt::white);
        m_chart->setPlotAreaBackgroundBrush(QColor(250, 250, 250));
        m_chart->setPlotAreaBackgroundVisible(true);
        m_chart->setTitleBrush(Qt::black);
        m_chart->legend()->setLabelColor(Qt::black);
    }
}
```

## Intégration dans MainWindow

Ajouter dans mainwindow.h:
```cpp
private:
    QPushButton* m_servicesButton{nullptr};
    QPushButton* m_serviceHistoryButton{nullptr};
    QPushButton* m_metricsHistoryButton{nullptr};
    
    std::unique_ptr<ServiceMonitor> m_serviceMonitor;
    std::unique_ptr<ServiceHistoryManager> m_serviceHistory;
    std::unique_ptr<MetricsHistory> m_metricsHistory;
```

Ajouter dans mainwindow.cpp (setupUi):
```cpp
// Services button
m_servicesButton = new QPushButton(tr("Windows Services"));
m_servicesButton->setIcon(QIcon(":/icons/service.png"));
connect(m_servicesButton, &QPushButton::clicked, this, [this]() {
    ServicesDialog dlg(this);
    dlg.exec();
});
toolsLayout->addWidget(m_servicesButton);

// Service History button
m_serviceHistoryButton = new QPushButton(tr("Service History"));
connect(m_serviceHistoryButton, &QPushButton::clicked, this, [this]() {
    ServiceHistoryDialog dlg(this);
    dlg.exec();
});
toolsLayout->addWidget(m_serviceHistoryButton);

// Metrics History button
m_metricsHistoryButton = new QPushButton(tr("Metrics History"));
connect(m_metricsHistoryButton, &QPushButton::clicked, this, [this]() {
    HistoryDialog dlg(m_metricsHistory.get(), this);
    dlg.exec();
});
toolsLayout->addWidget(m_metricsHistoryButton);
```

Initialiser dans le constructeur:
```cpp
MainWindow::MainWindow(QWidget *parent)
{
    // ... existing code ...
    
    // Initialize services monitoring and history
    m_serviceMonitor = std::make_unique<ServiceMonitor>(this);
    m_serviceMonitor->initialize();
    
    m_serviceHistory = std::make_unique<ServiceHistoryManager>(this);
    m_serviceHistory->initialize();
    
    m_metricsHistory = std::make_unique<MetricsHistory>(this);
    m_metricsHistory->initialize();
    
    // Connect service monitor to history recording
    connect(m_serviceMonitor.get(), &ServiceMonitor::servicesRefreshed,
            this, [this]() {
                m_serviceHistory->recordServiceSnapshots(m_serviceMonitor->services());
            });
    
    connect(m_serviceMonitor.get(), &ServiceMonitor::serviceCrashed,
            m_serviceHistory.get(), &ServiceHistoryManager::recordCrashEvent);
    
    // Start service monitoring
    m_serviceMonitor->startAutoRefresh(5000); // 5 seconds
    
    // Record metrics periodically
    QTimer* metricsTimer = new QTimer(this);
    connect(metricsTimer, &QTimer::timeout, this, &MainWindow::recordMetrics);
    metricsTimer->start(1000); // 1 second
}

void MainWindow::recordMetrics()
{
    // Record CPU
    m_metricsHistory->recordMetric(MetricType::CpuUsage, 
                                    m_cpuMonitor->overallUsage());
    
    // Record Memory
    m_metricsHistory->recordMetric(MetricType::MemoryUsed,
                                    m_memoryMonitor->usedMemory() / (1024.0 * 1024.0 * 1024.0));
    
    // Record GPU if available
    if (m_gpuMonitor->isAvailable()) {
        m_metricsHistory->recordMetric(MetricType::GpuUsage,
                                        m_gpuMonitor->usage());
    }
    
    // ... other metrics ...
}
```

## Résumé des fonctionnalités implémentées

### ✅ Services Windows
1. Liste complète avec filtrage
2. Start/Stop/Restart avec permissions admin
3. Changement type de démarrage
4. Détection services haute ressource
5. Historique des crashs
6. Vue dépendances
7. Enregistrement automatique historique

### ✅ Historique Avancé
1. Base SQLite persistante
2. Graphiques interactifs (zoom, pan, sélection)
3. Export CSV/JSON
4. Comparaison périodes (aujourd'hui/hier, semaine/semaine)
5. Statistiques agrégées
6. Top services par ressources
7. Rétention configurable

### ✅ Interface Graphique
1. Charts interactifs avec Qt Charts
2. Sélecteur plage temporelle
3. Graphiques comparaison
4. Tables triables et filtrables
5. Export images/PDF
6. Thème sombre

## Notes importantes

- Tous les fichiers utilisent Qt 6
- Compatible Windows uniquement (services)
- Nécessite droits administrateur pour contrôle services
- SQLite pour persistance données
- Auto-cleanup données anciennes (30 jours par défaut)
- Performance optimisée (buffering, indexes)
