using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace SheetViewsMap
{
    /// <summary>
    /// Отдельное окно плагина поверх окон Revit.
    ///
    /// Отличие от закрепляемой панели (DockablePane): это самостоятельное окно
    /// Windows, поэтому оно может перекрывать другие программы и всегда быть
    /// поверх всего (TopMost).
    ///
    /// Настройки Revit, включая MainWindowHandle, использовать нельзя —
    /// передаём дескриптор окна Revit из PaneCommand.
    /// </summary>
    internal static class SheetMapWindow
    {
        private const double DefaultWidth = 960;
        private const double DefaultHeight = 700;
        private const double MinWidth = 680;
        private const double MinHeight = 420;

        private static Window _window;
        private static SheetMapPane _pane;

        /// <summary>Дескриптор главного окна Revit — окно принадлежит ему и живёт над ним.</summary>
        private static IntPtr _revitHandle = IntPtr.Zero;

        /// <summary>
        /// Показать окно, если оно скрыто, и спрятать, если видно.
        /// </summary>
        internal static void Toggle(IntPtr revitHandle)
        {
            _revitHandle = revitHandle;

            if (_window != null)
            {
                if (_window.IsVisible)
                    HideByUser();
                else
                    ShowExisting();

                return;
            }

            Create();
        }

        // ------------------------------------------------------------------ создание

        private static void Create()
        {
            _pane = new SheetMapPane();

            var window = new Window
            {
                Title = App.WindowTitle,
                Width = DefaultWidth,
                Height = DefaultHeight,
                MinWidth = MinWidth,
                MinHeight = MinHeight,
                ResizeMode = ResizeMode.CanResize,
                WindowStartupLocation = WindowStartupLocation.Manual,

                // Окно принадлежит Revit: всегда над ним, но под остальными
                // программами. Вместе с Revit перемещается, сворачивается
                // и восстанавливается - Windows делает это сама.
                Topmost = false,

                ShowInTaskbar = false,
                ShowActivated = true,
                Content = _pane
            };

            _window = window;

            if (_revitHandle != IntPtr.Zero)
            {
                try
                {
                    new WindowInteropHelper(window).Owner = _revitHandle;
                }
                catch
                {
                    // Если дескриптор не подошёл - окно просто будет обычным.
                }
            }

            // Сначала возвращаем сохранённое положение, и только если его нет
            // или оно указывает за пределы экранов - центрируем по окну Revit.
            if (!TryRestoreBounds(window))
                CenterOverRevit(window);

            window.IsVisibleChanged += (s, e) =>
            {
                if (window.IsVisible)
                    _pane.StartWatching();
                else
                    _pane.StopWatching();
            };

            window.Closed += (s, e) =>
            {
                SaveBounds(window);
                _window = null;
                _pane = null;
            };

            window.Show();
            window.Activate();
        }

        private static void ShowExisting()
        {
            _window.Show();
            _window.Activate();
        }

        private static void HideByUser()
        {
            SaveBounds(_window);
            _window.Hide();
        }

        // ------------------------------------------------------------------ положение и размер

        private static string SettingsPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SheetViewsMap",
                    "window.ini");
            }
        }

        /// <summary>
        /// Возвращает сохранённые положение и размер окна.
        /// Возвращает false, если настройки нет либо она указывает за пределы экранов.
        /// </summary>
        private static bool TryRestoreBounds(Window window)
        {
            double? left = ReadDouble("Left");
            double? top = ReadDouble("Top");
            double? width = ReadDouble("Width");
            double? height = ReadDouble("Height");

            if (!left.HasValue || !top.HasValue || !width.HasValue || !height.HasValue)
                return false;

            double restoredWidth = Math.Max(width.Value, MinWidth);
            double restoredHeight = Math.Max(height.Value, MinHeight);

            // Окно могли запомнить с монитора, которого уже нет.
            if (!IsRectOnVirtualScreen(left.Value, top.Value, restoredWidth, restoredHeight))
                return false;

            window.Width = restoredWidth;
            window.Height = restoredHeight;
            window.Left = left.Value;
            window.Top = top.Value;

            return true;
        }

        private static void SaveBounds(Window window)
        {
            try
            {
                // Сохраняем только нормальное (не развёрнутое) положение.
                if (window.WindowState != WindowState.Normal)
                    return;

                // И не сохраняем бессмыслицу, если окно уехало за пределы экранов.
                if (!IsRectOnVirtualScreen(window.Left, window.Top, window.Width, window.Height))
                    return;

                string folder = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                File.WriteAllLines(SettingsPath, new[]
                {
                    "Left=" + window.Left.ToString("0", CultureInfo.InvariantCulture),
                    "Top=" + window.Top.ToString("0", CultureInfo.InvariantCulture),
                    "Width=" + window.Width.ToString("0", CultureInfo.InvariantCulture),
                    "Height=" + window.Height.ToString("0", CultureInfo.InvariantCulture)
                });
            }
            catch
            {
                // Не смогли сохранить - не страшно, окно просто откроется по центру.
            }
        }

        private static double? ReadDouble(string key)
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return null;

                foreach (string line in File.ReadAllLines(SettingsPath))
                {
                    int separator = line.IndexOf('=');
                    if (separator <= 0)
                        continue;

                    if (!string.Equals(line.Substring(0, separator).Trim(), key, StringComparison.OrdinalIgnoreCase))
                        continue;

                    double value;
                    string text = line.Substring(separator + 1).Trim();
                    if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                        return value;
                }
            }
            catch
            {
                // Повреждённый файл настроек - просто игнорируем.
            }

            return null;
        }

        private static bool IsRectOnVirtualScreen(double left, double top, double width, double height)
        {
            double vsLeft = SystemParameters.VirtualScreenLeft;
            double vsTop = SystemParameters.VirtualScreenTop;
            double vsRight = vsLeft + SystemParameters.VirtualScreenWidth;
            double vsBottom = vsTop + SystemParameters.VirtualScreenHeight;

            // Требуем, чтобы заметная часть окна попадала на экран,
            // иначе вернём его к центру.
            const int margin = 80;

            return left + width > vsLeft + margin
                && left < vsRight - margin
                && top + height > vsTop + margin
                && top < vsBottom - margin;
        }

        private static void CenterOverRevit(Window window)
        {
            RECT rect;
            double scale = GetDpiScale();

            if (_revitHandle != IntPtr.Zero && GetWindowRect(_revitHandle, out rect))
            {
                double left = rect.Left / scale;
                double top = rect.Top / scale;
                double width = (rect.Right - rect.Left) / scale;
                double height = (rect.Bottom - rect.Top) / scale;

                window.Left = left + (width - window.Width) / 2;
                window.Top = top + (height - window.Height) / 2;
            }
            else
            {
                window.Left = SystemParameters.WorkArea.Left
                    + (SystemParameters.WorkArea.Width - window.Width) / 2;
                window.Top = SystemParameters.WorkArea.Top
                    + (SystemParameters.WorkArea.Height - window.Height) / 2;
            }
        }

        // ------------------------------------------------------------------ Win32

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr handle, out RECT rect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr handle, IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr dc, int index);

        /// <summary>
        /// GetWindowRect отдаёт пиксели, а WPF работает в единицах 1/96 дюйма.
        /// На мониторах с масштабированием их нужно пересчитать.
        /// </summary>
        private static double GetDpiScale()
        {
            try
            {
                IntPtr dc = GetDC(IntPtr.Zero);
                if (dc == IntPtr.Zero)
                    return 1.0;

                try
                {
                    int dpi = GetDeviceCaps(dc, 88); // LOGPIXELSX
                    return dpi > 0 ? dpi / 96.0 : 1.0;
                }
                finally
                {
                    ReleaseDC(IntPtr.Zero, dc);
                }
            }
            catch
            {
                return 1.0;
            }
        }
    }
}