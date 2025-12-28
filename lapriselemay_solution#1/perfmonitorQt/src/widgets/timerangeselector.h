#pragma once

#include <QWidget>
#include <QComboBox>
#include <QDateTimeEdit>
#include <QPushButton>
#include <QDateTime>

/**
 * @brief Widget for selecting time ranges
 * 
 * Provides preset ranges (last hour, 24h, 7 days, etc.) and custom range selection
 */
class TimeRangeSelector : public QWidget
{
    Q_OBJECT

public:
    explicit TimeRangeSelector(QWidget* parent = nullptr);
    
    /// Set the current time range
    void setTimeRange(const QDateTime& start, const QDateTime& end);
    
    /// Get the current time range
    std::pair<QDateTime, QDateTime> timeRange() const;

signals:
    /// Emitted when the time range changes
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
