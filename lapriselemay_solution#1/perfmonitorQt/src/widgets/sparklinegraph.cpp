#include "sparklinegraph.h"

#include <QPainter>
#include <QPainterPath>
#include <QLinearGradient>
#include <algorithm>

SparklineGraph::SparklineGraph(int maxPoints, const QColor& lineColor, QWidget *parent)
    : QWidget(parent)
    , m_maxPoints(maxPoints)
    , m_lineColor(lineColor)
{
    // Create fill color as semi-transparent version of line color
    m_fillColor = lineColor;
    m_fillColor.setAlpha(50);
    
    setMinimumHeight(60);
    setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Expanding);
}

void SparklineGraph::addValue(double value)
{
    m_values.push_back(value);
    
    while (static_cast<int>(m_values.size()) > m_maxPoints) {
        m_values.pop_front();
    }
    
    if (m_autoScale) {
        updateMinMax();
    }
    
    update();
}

void SparklineGraph::clear()
{
    m_values.clear();
    update();
}

void SparklineGraph::setLineColor(const QColor& color)
{
    m_lineColor = color;
    m_fillColor = color;
    m_fillColor.setAlpha(50);
    update();
}

void SparklineGraph::setFillColor(const QColor& color)
{
    m_fillColor = color;
    update();
}

void SparklineGraph::setBackgroundColor(const QColor& color)
{
    m_backgroundColor = color;
    update();
}

void SparklineGraph::setGridColor(const QColor& color)
{
    m_gridColor = color;
    update();
}

void SparklineGraph::setMaxValue(double max)
{
    m_maxValue = max;
    m_currentMax = max;
    update();
}

void SparklineGraph::setAutoScale(bool enable)
{
    m_autoScale = enable;
    if (enable) {
        updateMinMax();
    } else {
        m_currentMax = m_maxValue;
    }
    update();
}

void SparklineGraph::setShowGrid(bool show)
{
    m_showGrid = show;
    update();
}

void SparklineGraph::setShowLabels(bool show)
{
    m_showLabels = show;
    update();
}

void SparklineGraph::updateMinMax()
{
    if (m_values.empty()) {
        m_currentMax = m_maxValue;
        return;
    }
    
    double maxVal = *std::max_element(m_values.begin(), m_values.end());
    m_currentMax = std::max(maxVal * 1.1, 10.0);  // 10% headroom, minimum 10
}

void SparklineGraph::paintEvent(QPaintEvent*)
{
    QPainter painter(this);
    painter.setRenderHint(QPainter::Antialiasing);
    
    constexpr int rightMargin = 5;
    constexpr int topMargin = 5;
    constexpr int bottomMargin = 5;
    const int margin = m_showLabels ? 35 : 5;
    
    QRect graphRect(margin, topMargin, 
                    width() - margin - rightMargin, 
                    height() - topMargin - bottomMargin);
    
    // Background
    painter.fillRect(rect(), m_backgroundColor);
    painter.fillRect(graphRect, QColor(30, 30, 30));
    
    // Border
    painter.setPen(QPen(m_gridColor, 1));
    painter.drawRect(graphRect);
    
    // Grid lines
    if (m_showGrid) {
        painter.setPen(QPen(m_gridColor, 1, Qt::DotLine));
        
        // Horizontal grid lines (25%, 50%, 75%)
        for (int i = 1; i < 4; ++i) {
            int y = graphRect.top() + (graphRect.height() * i / 4);
            painter.drawLine(graphRect.left(), y, graphRect.right(), y);
        }
        
        // Vertical grid lines
        int verticalLines = 6;
        for (int i = 1; i < verticalLines; ++i) {
            int x = graphRect.left() + (graphRect.width() * i / verticalLines);
            painter.drawLine(x, graphRect.top(), x, graphRect.bottom());
        }
    }
    
    // Labels
    if (m_showLabels) {
        painter.setPen(QColor(180, 180, 180));
        QFont font = painter.font();
        font.setPointSize(8);
        painter.setFont(font);
        
        // Y-axis labels
        painter.drawText(QRect(0, graphRect.top() - 8, margin - 5, 16),
                        Qt::AlignRight | Qt::AlignVCenter,
                        QString::number(m_currentMax, 'f', 0));
        painter.drawText(QRect(0, graphRect.center().y() - 8, margin - 5, 16),
                        Qt::AlignRight | Qt::AlignVCenter,
                        QString::number(m_currentMax / 2, 'f', 0));
        painter.drawText(QRect(0, graphRect.bottom() - 8, margin - 5, 16),
                        Qt::AlignRight | Qt::AlignVCenter,
                        "0");
    }
    
    // Draw data
    if (m_values.size() < 2) {
        return;
    }
    
    // Calculate points - data flows left to right (oldest on left, newest on right)
    QVector<QPointF> points;
    points.reserve(m_values.size());
    
    double xStep = static_cast<double>(graphRect.width()) / (m_maxPoints - 1);
    
    // Start from left side, newest values on the right
    for (size_t i = 0; i < m_values.size(); ++i) {
        double x = graphRect.left() + i * xStep;
        double normalizedValue = std::clamp(m_values[i] / m_currentMax, 0.0, 1.0);
        double y = graphRect.bottom() - normalizedValue * graphRect.height();
        points.append(QPointF(x, y));
    }
    
    // Create path for fill
    QPainterPath fillPath;
    fillPath.moveTo(points.first().x(), graphRect.bottom());
    for (const auto& pt : points) {
        fillPath.lineTo(pt);
    }
    fillPath.lineTo(points.last().x(), graphRect.bottom());
    fillPath.closeSubpath();
    
    // Gradient fill
    QLinearGradient gradient(0, graphRect.top(), 0, graphRect.bottom());
    QColor topColor = m_fillColor;
    topColor.setAlpha(100);
    QColor bottomColor = m_fillColor;
    bottomColor.setAlpha(20);
    gradient.setColorAt(0, topColor);
    gradient.setColorAt(1, bottomColor);
    
    painter.fillPath(fillPath, gradient);
    
    // Draw line
    painter.setPen(QPen(m_lineColor, 2));
    painter.drawPolyline(points.data(), points.size());
    
    // Draw current value indicator
    if (!m_values.empty()) {
        QPointF lastPoint = points.last();
        
        // Glow effect
        QRadialGradient glow(lastPoint, 8);
        QColor glowColor = m_lineColor;
        glowColor.setAlpha(100);
        glow.setColorAt(0, glowColor);
        glow.setColorAt(1, Qt::transparent);
        painter.setBrush(glow);
        painter.setPen(Qt::NoPen);
        painter.drawEllipse(lastPoint, 8, 8);
        
        // Point
        painter.setBrush(m_lineColor);
        painter.setPen(QPen(Qt::white, 1));
        painter.drawEllipse(lastPoint, 4, 4);
    }
}

void SparklineGraph::resizeEvent(QResizeEvent *event)
{
    QWidget::resizeEvent(event);
    update();
}
