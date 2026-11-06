using System;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using WpfGrid = System.Windows.Controls.Grid;

namespace RevitMCP.UI
{
    /// <summary>
    /// 4.9 B 的備援路徑：如果 <see cref="Autodesk.Revit.UI.PreviewControl"/> 嵌不進停靠面板
    /// （或這台電腦上會出錯），改用一個獨立的小型無模式視窗顯示同樣的兩格預覽
    /// （透視圖＋所在樓層平面）。PreviewControl 的建立／Dispose 都在呼叫端（面板，經
    /// ExternalEventManager）完成，這個視窗只負責「顯示」。
    /// </summary>
    internal class CameraPreviewWindow : Window
    {
        private readonly ContentControl _slotA, _slotB;
        private PreviewControl _controlA, _controlB;

        public CameraPreviewWindow()
        {
            Title = "相機預覽（實驗）";
            Width = 900;
            Height = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = CameraManagerTheme.Background;
            FontFamily = CameraManagerTheme.UiFont;

            WpfGrid root = new WpfGrid { Background = CameraManagerTheme.Background };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _slotA = new ContentControl { Margin = new Thickness(4), BorderBrush = CameraManagerTheme.BorderBrush, BorderThickness = new Thickness(1) };
            _slotB = new ContentControl { Margin = new Thickness(4), BorderBrush = CameraManagerTheme.BorderBrush, BorderThickness = new Thickness(1) };
            WpfGrid.SetColumn(_slotB, 1);
            root.Children.Add(_slotA);
            root.Children.Add(_slotB);

            Content = root;
            Closed += (s, e) => DisposeControls();
        }

        /// <summary>由呼叫端在 Revit API context（ExternalEvent）內建立好 PreviewControl 後傳進來。</summary>
        public void SetPreviews(PreviewControl a, PreviewControl b)
        {
            DisposeControls();
            _controlA = a;
            _controlB = b;
            _slotA.Content = a;
            _slotB.Content = b;
        }

        private void DisposeControls()
        {
            try { _controlA?.Dispose(); } catch { /* 視窗關閉時盡量不要再拋例外 */ }
            try { _controlB?.Dispose(); } catch { }
            _controlA = null;
            _controlB = null;
        }
    }
}
