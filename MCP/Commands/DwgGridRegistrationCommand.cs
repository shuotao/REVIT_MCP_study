using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfGrid = System.Windows.Controls.Grid;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using RevitMCP.Core;
using RevitMCP.Core.Services;
using RevitMCP.Models;

namespace RevitMCP.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class DwgGridRegistrationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uiDoc = commandData.Application.ActiveUIDocument;
                if (uiDoc == null)
                {
                    TaskDialog.Show("DWG Grid Registration", "請先開啟 Revit 專案。");
                    return Result.Cancelled;
                }

                Document doc = uiDoc.Document;
                var levelViewService = new LevelViewService();
                IList<Level> allLevels = levelViewService.ResolveLevels(doc, Enumerable.Empty<string>(), Enumerable.Empty<long>());
                if (allLevels.Count == 0)
                {
                    TaskDialog.Show("DWG Grid Registration", "目前專案沒有可用 Level。");
                    return Result.Cancelled;
                }

                var fileWindow = new DwgFileSelectionWindow();
                if (fileWindow.ShowDialog() != true)
                {
                    return Result.Cancelled;
                }

                var setupWindow = new DwgRegistrationSetupWindow(fileWindow.DwgPath, allLevels);
                if (setupWindow.ShowDialog() != true)
                {
                    return Result.Cancelled;
                }

                IList<DwgImportResult> results = RunImport(doc, fileWindow.DwgPath, setupWindow.SelectedLevels, setupWindow.Settings);
                string report = new ReportService().BuildTextReport(results);
                var reportWindow = new DwgRegistrationReportWindow(report, results);
                reportWindow.ShowDialog();

                return results.Any(result => result.Success) ? Result.Succeeded : Result.Failed;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("DWG Grid Registration", "執行失敗: " + ex.Message);
                return Result.Failed;
            }
        }

        private static IList<DwgImportResult> RunImport(Document doc, string dwgPath, IList<Level> selectedLevels, DwgImportSettings settings)
        {
            var levelViewService = new LevelViewService();
            var importService = new DwgImportService();
            var results = new List<DwgImportResult>();

            foreach (Level level in selectedLevels)
            {
                using (Transaction transaction = new Transaction(doc, "DWG Grid Registration - " + level.Name))
                {
                    try
                    {
                        transaction.Start();
                        ViewPlan view = levelViewService.GetOrCreateCadFloorPlan(doc, level);
                        DwgImportResult result = importService.Import(doc, dwgPath, level, view, settings);
                        transaction.Commit();
                        results.Add(result);
                    }
                    catch (Exception ex)
                    {
                        if (transaction.HasStarted())
                        {
                            transaction.RollBack();
                        }

                        results.Add(new DwgImportResult
                        {
                            DwgPath = dwgPath,
                            LevelName = level.Name,
                            LevelId = Convert.ToInt64(level.Id.GetIdValue()),
                            LoadMode = settings.LoadMode.ToString(),
                            PlacementMode = settings.PlacementMode.ToString(),
                            Success = false,
                            Message = ex.Message ?? string.Empty
                        });
                    }
                }
            }

            return results;
        }
    }

    internal class DwgFileSelectionWindow : Window
    {
        private readonly WpfTextBox _pathBox;
        private readonly Button _nextButton;

        public DwgFileSelectionWindow()
        {
            Title = "DWG 放樣 - 1. 選擇檔案";
            Width = 560;
            Height = 210;
            MinWidth = 460;
            MinHeight = 190;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _pathBox = new WpfTextBox
            {
                IsReadOnly = true,
                Text = "尚未選擇 DWG 檔案",
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _nextButton = new Button { Content = "下一步", MinWidth = 80, IsDefault = true, IsEnabled = false };

            Content = BuildContent();
        }

        public string DwgPath { get; private set; } = string.Empty;

        private UIElement BuildContent()
        {
            var root = new DockPanel { Margin = new Thickness(14) };

            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            _nextButton.Click += (_, __) => DialogResult = true;
            var cancelButton = new Button { Content = "取消", MinWidth = 72, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
            footer.Children.Add(_nextButton);
            footer.Children.Add(cancelButton);
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = "選擇要匯入到樓層 CAD 放樣視圖的 DWG 檔案。",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var row = new WpfGrid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(_pathBox);

            var browseButton = new Button { Content = "選擇 DWG", MinWidth = 92, Margin = new Thickness(8, 0, 0, 0) };
            browseButton.Click += (_, __) => PickDwgFile();
            WpfGrid.SetColumn(browseButton, 1);
            row.Children.Add(browseButton);

            stack.Children.Add(row);
            root.Children.Add(stack);
            return root;
        }

        private void PickDwgFile()
        {
            var dialog = new OpenFileDialog
            {
                Title = "選擇 DWG 檔案",
                Filter = "DWG files (*.dwg)|*.dwg|All files (*.*)|*.*",
                Multiselect = false,
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) == true)
            {
                DwgPath = dialog.FileName;
                _pathBox.Text = DwgPath;
                _nextButton.IsEnabled = true;
            }
        }
    }

    internal class DwgRegistrationSetupWindow : Window
    {
        private readonly IList<LevelSelectionItem> _items;
        private readonly CheckBox _thisViewOnlyBox;
        private readonly CheckBox _pinAfterLoadBox;
        private readonly CheckBox _visibleLayersOnlyBox;
        private readonly CheckBox _blackAndWhiteBox;
        private readonly WpfComboBox _unitCombo;

        public DwgRegistrationSetupWindow(string dwgPath, IEnumerable<Level> levels)
        {
            Title = "DWG 放樣 - 2. 樓層與設定";
            Width = 560;
            Height = 620;
            MinWidth = 460;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            DwgPath = dwgPath;
            _items = levels
                .OrderBy(level => level.Elevation)
                .Select(level => new LevelSelectionItem(level))
                .ToList();

            _thisViewOnlyBox = new CheckBox { Content = "This View Only", IsChecked = true, Margin = new Thickness(4) };
            _pinAfterLoadBox = new CheckBox { Content = "Pin after load", IsChecked = true, Margin = new Thickness(4) };
            _visibleLayersOnlyBox = new CheckBox { Content = "Visible Layers Only", IsChecked = true, Margin = new Thickness(4) };
            _blackAndWhiteBox = new CheckBox { Content = "Black and White", IsChecked = true, Margin = new Thickness(4) };
            _unitCombo = new WpfComboBox { Margin = new Thickness(4), MinWidth = 140 };
            _unitCombo.Items.Add("Millimeter");
            _unitCombo.Items.Add("Meter");
            _unitCombo.Items.Add("Default");
            _unitCombo.SelectedIndex = 0;

            Content = BuildContent();
        }

        public string DwgPath { get; }

        public IList<Level> SelectedLevels => _items
            .Where(item => item.IsSelected)
            .Select(item => item.Level)
            .ToList();

        public DwgImportSettings Settings => new DwgImportSettings
        {
            ThisViewOnly = _thisViewOnlyBox.IsChecked == true,
            PinAfterLoad = _pinAfterLoadBox.IsChecked == true,
            VisibleLayersOnly = _visibleLayersOnlyBox.IsChecked == true,
            ColorMode = _blackAndWhiteBox.IsChecked == true ? ImportColorMode.BlackAndWhite : ImportColorMode.Preserved,
            Unit = ResolveUnit(_unitCombo.SelectedItem as string)
        };

        private UIElement BuildContent()
        {
            var root = new DockPanel { Margin = new Thickness(14) };

            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var runButton = new Button { Content = "開始執行", MinWidth = 88, IsDefault = true };
            runButton.Click += (_, __) => ConfirmAndRun();
            var cancelButton = new Button { Content = "取消", MinWidth = 72, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
            footer.Children.Add(runButton);
            footer.Children.Add(cancelButton);
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            var grid = new WpfGrid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var summary = new TextBlock
            {
                Text = "DWG: " + DwgPath,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            WpfGrid.SetRow(summary, 0);
            grid.Children.Add(summary);

            var levelGroup = new GroupBox { Header = "選擇樓層", Margin = new Thickness(0, 0, 0, 12) };
            var levelList = new ListBox();
            foreach (LevelSelectionItem item in _items)
            {
                var checkBox = new CheckBox
                {
                    Content = item.Name,
                    IsChecked = item.IsSelected,
                    Margin = new Thickness(4)
                };
                checkBox.Checked += (_, __) => item.IsSelected = true;
                checkBox.Unchecked += (_, __) => item.IsSelected = false;
                levelList.Items.Add(checkBox);
            }
            levelGroup.Content = levelList;
            WpfGrid.SetRow(levelGroup, 1);
            grid.Children.Add(levelGroup);

            var settingsGroup = new GroupBox { Header = "匯入設定" };
            var settingsPanel = new StackPanel();
            settingsPanel.Children.Add(_thisViewOnlyBox);
            settingsPanel.Children.Add(_pinAfterLoadBox);
            settingsPanel.Children.Add(_visibleLayersOnlyBox);
            settingsPanel.Children.Add(_blackAndWhiteBox);
            settingsPanel.Children.Add(new TextBlock { Text = "單位", Margin = new Thickness(4, 8, 4, 0) });
            settingsPanel.Children.Add(_unitCombo);
            settingsGroup.Content = settingsPanel;
            WpfGrid.SetRow(settingsGroup, 2);
            grid.Children.Add(settingsGroup);

            root.Children.Add(grid);
            return root;
        }

        private void ConfirmAndRun()
        {
            if (SelectedLevels.Count == 0)
            {
                MessageBox.Show(this, "請至少選擇一個 Level。", "DWG Grid Registration", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DialogResult = true;
        }

        private static ImportUnit ResolveUnit(string? unitName)
        {
            switch (unitName)
            {
                case "Meter":
                    return ImportUnit.Meter;
                case "Default":
                    return ImportUnit.Default;
                default:
                    return ImportUnit.Millimeter;
            }
        }
    }

    internal class DwgRegistrationReportWindow : Window
    {
        public DwgRegistrationReportWindow(string report, IEnumerable<DwgImportResult> results)
        {
            Title = "DWG 放樣 - 3. 執行報告";
            Width = 620;
            Height = 520;
            MinWidth = 500;
            MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Content = BuildContent(report, results);
        }

        private UIElement BuildContent(string report, IEnumerable<DwgImportResult> results)
        {
            var rows = results.ToList();
            var root = new DockPanel { Margin = new Thickness(14) };

            var closeButton = new Button
            {
                Content = "關閉",
                MinWidth = 72,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0),
                IsDefault = true,
                IsCancel = true
            };
            closeButton.Click += (_, __) => Close();
            DockPanel.SetDock(closeButton, Dock.Bottom);
            root.Children.Add(closeButton);

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = $"完成：{rows.Count(result => result.Success)} / {rows.Count} 成功",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var reportBox = new WpfTextBox
            {
                Text = report,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            stack.Children.Add(reportBox);
            root.Children.Add(stack);
            return root;
        }
    }

    internal class LevelSelectionItem
    {
        public LevelSelectionItem(Level level)
        {
            Level = level;
            Name = level.Name;
            IsSelected = true;
        }

        public Level Level { get; }
        public string Name { get; }
        public bool IsSelected { get; set; }
    }
}
