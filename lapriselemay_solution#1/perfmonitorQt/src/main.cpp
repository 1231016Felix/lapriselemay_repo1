/**
 * PerfMonitorQt - Windows 11 Performance Monitor
 * Main entry point
 * 
 * Copyright (c) 2024 Félix-Antoine
 */

#include <QApplication>
#include <QStyleFactory>
#include <QFile>
#include <QMessageBox>
#include "mainwindow.h"

int main(int argc, char *argv[])
{
    // Enable high DPI scaling
    QApplication::setHighDpiScaleFactorRoundingPolicy(
        Qt::HighDpiScaleFactorRoundingPolicy::PassThrough);
    
    QApplication app(argc, argv);
    
    // Application metadata
    app.setApplicationName("PerfMonitorQt");
    app.setApplicationVersion("1.0.0");
    app.setOrganizationName("Félix-Antoine");
    app.setWindowIcon(QIcon(":/icons/app_icon.png"));
    
    // Apply Fusion style for modern look
    app.setStyle(QStyleFactory::create("Fusion"));
    
    // Dark theme palette
    QPalette darkPalette;
    darkPalette.setColor(QPalette::Window, QColor(30, 30, 30));
    darkPalette.setColor(QPalette::WindowText, Qt::white);
    darkPalette.setColor(QPalette::Base, QColor(25, 25, 25));
    darkPalette.setColor(QPalette::AlternateBase, QColor(35, 35, 35));
    darkPalette.setColor(QPalette::ToolTipBase, QColor(45, 45, 45));
    darkPalette.setColor(QPalette::ToolTipText, Qt::white);
    darkPalette.setColor(QPalette::Text, Qt::white);
    darkPalette.setColor(QPalette::Button, QColor(45, 45, 45));
    darkPalette.setColor(QPalette::ButtonText, Qt::white);
    darkPalette.setColor(QPalette::BrightText, Qt::red);
    darkPalette.setColor(QPalette::Link, QColor(42, 130, 218));
    darkPalette.setColor(QPalette::Highlight, QColor(0, 120, 215));
    darkPalette.setColor(QPalette::HighlightedText, Qt::white);
    darkPalette.setColor(QPalette::Disabled, QPalette::Text, QColor(127, 127, 127));
    darkPalette.setColor(QPalette::Disabled, QPalette::ButtonText, QColor(127, 127, 127));
    
    app.setPalette(darkPalette);
    
    // Custom stylesheet
    app.setStyleSheet(R"(
        QToolTip { 
            color: #ffffff; 
            background-color: #2d2d2d; 
            border: 1px solid #3d3d3d;
            padding: 4px;
            border-radius: 4px;
        }
        QGroupBox {
            font-weight: bold;
            border: 1px solid #3d3d3d;
            border-radius: 6px;
            margin-top: 12px;
            padding-top: 10px;
        }
        QGroupBox::title {
            subcontrol-origin: margin;
            left: 10px;
            padding: 0 5px;
        }
        QTabWidget::pane {
            border: 1px solid #3d3d3d;
            border-radius: 4px;
        }
        QTabBar::tab {
            background: #2d2d2d;
            border: 1px solid #3d3d3d;
            padding: 8px 16px;
            margin-right: 2px;
            border-top-left-radius: 4px;
            border-top-right-radius: 4px;
        }
        QTabBar::tab:selected {
            background: #0078d7;
        }
        QTabBar::tab:hover:!selected {
            background: #3d3d3d;
        }
        QProgressBar {
            border: 1px solid #3d3d3d;
            border-radius: 4px;
            text-align: center;
            background: #1e1e1e;
        }
        QProgressBar::chunk {
            background: qlineargradient(x1:0, y1:0, x2:1, y2:0,
                stop:0 #0078d7, stop:1 #00a2ed);
            border-radius: 3px;
        }
        QTableView {
            gridline-color: #3d3d3d;
            selection-background-color: #0078d7;
        }
        QHeaderView::section {
            background-color: #2d2d2d;
            padding: 6px;
            border: 1px solid #3d3d3d;
            font-weight: bold;
        }
        QScrollBar:vertical {
            background: #1e1e1e;
            width: 12px;
            margin: 0;
        }
        QScrollBar::handle:vertical {
            background: #4d4d4d;
            min-height: 30px;
            border-radius: 6px;
        }
        QScrollBar::handle:vertical:hover {
            background: #5d5d5d;
        }
        QPushButton {
            background-color: #0078d7;
            border: none;
            padding: 8px 16px;
            border-radius: 4px;
            font-weight: bold;
        }
        QPushButton:hover {
            background-color: #1a88e0;
        }
        QPushButton:pressed {
            background-color: #006cc1;
        }
        QPushButton:disabled {
            background-color: #4d4d4d;
            color: #7d7d7d;
        }
    )");
    
    try {
        MainWindow mainWindow;
        mainWindow.show();
        return app.exec();
    }
    catch (const std::exception& e) {
        QMessageBox::critical(nullptr, "Fatal Error", 
            QString("Application failed to start:\n%1").arg(e.what()));
        return 1;
    }
}
