#pragma once

#include <QWidget>
#include <QColor>
#include <deque>

class SparklineGraph : public QWidget
{
    Q_OBJECT

public:
    explicit SparklineGraph(int maxPoints = 60, 
                           const QColor& lineColor = QColor(0, 120, 215),
                           QWidget *parent = nullptr);
    ~SparklineGraph() override = default;

    void addValue(double value);
    void clear();
    
    void setLineColor(const QColor& color);
    void setFillColor(const QColor& color);
    void setBackgroundColor(const QColor& color);
    void setGridColor(const QColor& color);
    void setMaxValue(double max);
    void setAutoScale(bool enable);
    void setShowGrid(bool show);
    void setShowLabels(bool show);

protected:
    void paintEvent(QPaintEvent *event) override;
    void resizeEvent(QResizeEvent *event) override;

private:
    void updateMinMax();

    std::deque<double> m_values;
    int m_maxPoints;
    double m_minValue{0.0};
    double m_maxValue{100.0};
    double m_currentMax{100.0};
    bool m_autoScale{false};
    bool m_showGrid{true};
    bool m_showLabels{true};
    
    QColor m_lineColor;
    QColor m_fillColor;
    QColor m_backgroundColor{25, 25, 25};
    QColor m_gridColor{60, 60, 60};
};
