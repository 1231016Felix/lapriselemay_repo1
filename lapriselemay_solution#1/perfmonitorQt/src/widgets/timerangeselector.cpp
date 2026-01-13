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

void TimeRangeSelector::onPresetChanged([[maybe_unused]] int index)
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
