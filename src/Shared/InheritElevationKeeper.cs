using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Threading;

namespace SheetViewsMap
{
    /// <summary>
    /// Держит опцию «Наследование отметок» включённой при черчении трубопроводов.
    ///
    /// Логика: пока кнопка плагина включена (ВКЛ) и активен инструмент «Труба»,
    /// плагин каждые 2 секунды проверяет состояние переключателя
    /// «Наследование отметок» на ленте и, если он выключен, включает его
    /// реальным щелчком мыши по экранным координатам кнопки
    /// (полная эмуляция щелчка человеком).
    ///
    /// Опция не представлена в Revit API, поэтому работаем с лентой напрямую:
    /// DispatcherTimer на потоке Revit читает состояние через объектную модель
    /// Autodesk.Windows (рефлексия), нажатие - через пользовательский ввод.
    ///
    /// Попытки пишутся в лог inherit-log.txt.
    /// </summary>
    internal static class InheritElevationKeeper
    {
        private const string RuName = "наследоватьотметку";
        private const string RuName2 = "наследованиеотметок";
        private const string EnName = "inheritelevation";

        private static readonly TimeSpan SearchInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ClickCooldown = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ThrottledLogInterval = TimeSpan.FromSeconds(30);
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SheetViewsMap", "inherit-log.txt");

        private static DispatcherTimer _timer;
        private static Type _componentManagerType;
        private static Assembly _ribbonSourceAssembly;
        private static DateTime _lastClickUtc = DateTime.MinValue;
        private static DateTime _lastThrottledLogUtc = DateTime.MinValue;
        private static DateTime _lastDumpLogUtc = DateTime.MinValue;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT point);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, System.IntPtr dwExtraInfo);

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;

        /// <summary>Включает или выключает автоматическое сопровождение опции.</summary>
        internal static void SetEnabled(bool enabled)
        {
            if (enabled)
            {
                if (_timer != null)
                    return;

                Log("keeper v8 enabled");
                _timer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = SearchInterval
                };
                _timer.Tick += OnTick;
                _timer.Start();
            }
            else
            {
                if (_timer == null)
                    return;

                _timer.Stop();
                _timer.Tick -= OnTick;
                _timer = null;
                Log("keeper disabled");
            }
        }

        private static void OnTick(object sender, EventArgs e)
        {
            try
            {
                if (!(GetRibbon() is System.Windows.DependencyObject ribbon))
                {
                    LogThrottled("лента недоступна");
                    return;
                }

                bool pipeToolActive = IsPipeToolActive(ribbon);
                if (!pipeToolActive)
                {
                    LogThrottled("инструмент трубопровода не активен");
                    return;
                }

                // Состояние переключателя «Наследование отметок» в модели ленты.
                bool? toggleState = FindInheritElevationToggleState(ribbon);

                if (toggleState == null)
                {
                    LogThrottled("переключатель не найден в модели ленты (инструмент трубопровода не активен)");
                    return;
                }

                if (toggleState == false)
                {
                    // Выключена: выполняем РЕАЛЬНЫЙ щелчок мышью по кнопке.
                    bool clicked = TryClickInheritElevationToggle(ribbon);
                    LogThrottled(clicked
                        ? "выполнен щелчок по кнопке «Наследование отметок»"
                        : "переключатель не найден в визуальном дереве");
                }
                else
                {
                    LogThrottled("опция «Наследование отметок» включена ✓");
                }
            }
            catch (Exception ex)
            {
                LogThrottled("tick error: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------ состояние переключателя

        /// <summary>Состояние переключателя «Наследование отметок» в модели ленты.
        /// true - включён, false - выключен, null - не найден.</summary>
        private static bool? FindInheritElevationToggleState(object ribbon)
        {
            object tabs = GetProperty(ribbon, "Tabs");
            if (tabs != null)
                foreach (object tab in Each(tabs))
                {
                    var result = ScanTabForToggleState(tab, 0, new HashSet<object>());
                    if (result.HasValue)
                        return result;
                }

            object contextual = GetProperty(ribbon, "ContextualTabs");
            if (contextual != null)
                foreach (object tab in Each(contextual))
                {
                    var result = ScanTabForToggleState(tab, 0, new HashSet<object>());
                    if (result.HasValue)
                        return result;
                }

            return null;
        }

        /// <summary>Ищет переключатель «Наследование отметок» внутри вкладки
        /// (рекурсивно, включая fold-панели).</summary>
        private static bool? ScanTabForToggleState(object tab, int depth, HashSet<object> visited)
        {
            if (tab == null || depth > 6 || visited.Contains(tab))
                return null;
            visited.Add(tab);

            object panels = GetProperty(tab, "Panels");
            if (panels != null)
                foreach (object panel in Each(panels))
                {
                    object source = GetProperty(panel, "Source");
                    object items = source == null ? null : GetProperty(source, "Items");
                    if (items == null)
                        continue;

                    foreach (object item in Each(items))
                    {
                        string text = GetString(item, "Title") ?? GetString(item, "Text") ?? string.Empty;
                        if (text.IndexOf("наследован", StringComparison.OrdinalIgnoreCase) >= 0
                            && text.IndexOf("отмет", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            object isChecked = GetProperty(item, "IsChecked");
                            return isChecked is bool b ? b : (bool?)null;
                        }

                        // Контейнеры (fold panel и т.п.) обходим рекурсивно.
                        object inner = GetProperty(item, "Items");
                        if (inner != null)
                        {
                            var result = ScanTabForToggleState(inner, depth + 1, visited);
                            if (result.HasValue)
                                return result;
                        }
                    }
                }

            return null;
        }

        // ------------------------------------------------------------------ нажатие

        /// <summary>
        /// Ищет в визуальном дереве ленты ВИДИМЫЙ переключатель «Наследование отметок».
        /// true - переключатель найден (выполнен щелчок, если был выключен);
        /// false - не найден (визуальное дерево ещё не построено или режим не тот).
        /// </summary>
        private static bool TryClickInheritElevationToggle(object ribbon)
        {
            var stack = new Stack<System.Windows.DependencyObject>();
            var visited = new HashSet<System.Windows.DependencyObject>();

            stack.Push(ribbon);

            while (stack.Count > 0)
            {
                System.Windows.DependencyObject current = stack.Pop();

                if (current == null || visited.Contains(current))
                    continue;

                visited.Add(current);

                // Интересуют WPF-переключатели.
                if (current is System.Windows.Controls.Primitives.ToggleButton toggleButton)
                {
                    // Скрытые копии кнопки (свёрнутая панель ленты) пропускаем -
                    // нажатие невидимой копии отжимает видимую.
                    if (!toggleButton.IsVisible)
                        continue;

                    string normalized = CollectVisualText(toggleButton);
                    if (MatchesTargetPhrase(normalized))
                    {
                        // Нашли настоящую кнопку «Наследование отметок»:
                        // если выключена - выполняем РЕАЛЬНЫЙ щелчок мышью
                        // по её экранным координатам (курсор возвращается на место).
                        if (toggleButton.IsChecked != true)
                        {
                            toggleButton.Dispatcher.Invoke(new Action(() =>
                            {
                                // 1. Экранные координаты центра кнопки.
                                var center = toggleButton.PointToScreen(
                                    new System.Windows.Point(toggleButton.ActualWidth / 2, toggleButton.ActualHeight / 2));
                                int targetX = (int)Math.Round(center.X);
                                int targetY = (int)Math.Round(center.Y);

                                // 2. Запоминаем позицию курсора.
                                GetCursorPos(out POINT before);

                                // 3. Перемещаем курсор на кнопку и щёлкаем.
                                SetCursorPos(targetX, targetY);
                                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, System.IntPtr.Zero);
                                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, System.IntPtr.Zero);

                                // 4. Курсор возвращаем на прежнее место.
                                SetCursorPos(before.X, before.Y);
                            }), DispatcherPriority.Send);

                            _lastClickUtc = DateTime.UtcNow;
                            Log("выполнен реальный щелчок по кнопке «Наследование отметок»");
                        }

                        return true;
                    }
                }

                int childrenCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(current);
                for (int i = 0; i < childrenCount; i++)
                    stack.Push(System.Windows.Media.VisualTreeHelper.GetChild(current, i));
            }

            return false;
        }

        /// <summary>Собирает весь текст кнопки из визуального дерева (TextBlock'и) и Content.</summary>
        private static string CollectVisualText(System.Windows.DependencyObject root)
        {
            var sb = new StringBuilder();
            CollectText(root, sb, 0);
            return sb.ToString();
        }

        private static void CollectText(System.Windows.DependencyObject current, StringBuilder sb, int depth)
        {
            if (depth > 6)
                return;

            if (current is System.Windows.Controls.TextBlock textBlock)
                sb.Append(textBlock.Text);

            if (current is System.Windows.Controls.ContentControl contentControl)
            {
                string content = contentControl.Content as string;
                if (!string.IsNullOrEmpty(content))
                    sb.Append(content);
            }

            int childrenCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(current);
            for (int i = 0; i < childrenCount; i++)
                CollectText(System.Windows.Media.VisualTreeHelper.GetChild(current, i), sb, depth + 1);
        }

        // ------------------------------------------------------------------ лента и инструмент

        /// <summary>Активен ли инструмент черчения труб (есть вкладка с «Труба»).</summary>
        private static bool IsPipeToolActive(object ribbon)
        {
            object tabs = GetProperty(ribbon, "Tabs");
            if (tabs != null)
            {
                foreach (object tab in Each(tabs))
                {
                    string title = GetString(tab, "Title") ?? string.Empty;
                    if (title.IndexOf("Труба", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }

            object contextual = GetProperty(ribbon, "ContextualTabs");
            if (contextual != null)
            {
                foreach (object tab in Each(contextual))
                {
                    string title = GetString(tab, "Title") ?? string.Empty;
                    if (title.IndexOf("Труба", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }

            return false;
        }

        private static System.Windows.DependencyObject GetRibbon()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = null;
                try
                {
                    type = assembly.GetType("Autodesk.Windows.ComponentManager");
                }
                catch
                {
                }

                if (type == null)
                    continue;

                if (_ribbonSourceAssembly == null)
                {
                    _ribbonSourceAssembly = assembly;
                    Log("лента: ComponentManager из сборки " + assembly.GetName().Name +
                        ", свойство Ribbon: " + (GetStaticProperty(type, "Ribbon") != null));
                }

                return GetStaticProperty(type, "Ribbon") as System.Windows.DependencyObject;
            }

            return null;
        }

        private static object GetStaticProperty(Type type, string name)
        {
            try
            {
                PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
                return property == null ? null : property.GetValue(null, null);
            }
            catch
            {
                return null;
            }
        }

        private static object GetProperty(object target, string name)
        {
            if (target == null)
                return null;

            try
            {
                PropertyInfo property = target.GetType().GetProperty(name);
                return property == null ? null : property.GetValue(target, null);
            }
            catch
            {
                return null;
            }
        }

        private static string GetString(object target, string name)
        {
            object value = GetProperty(target, name);
            return value as string;
        }

        /// <summary>Приводит объект к IEnumerable (или возвращает пустой набор).</summary>
        private static IEnumerable Each(object collection)
        {
            var enumerable = collection as IEnumerable;
            if (enumerable != null)
                return enumerable;

            return new object[0];
        }

        // ------------------------------------------------------------------ фраза и лог

        /// <summary>Целевая фраза: текст (без пробелов и переносов строк, в нижнем
        /// регистре) содержит «наследоватьотметку», «наследованиеотметок» или
        /// «inheritelevation». Подписи двухстрочных кнопок Revit склеивает
        /// без пробела («Наследоватьотметку»).</summary>
        private static bool MatchesTargetPhrase(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            string normalized = NormalizeForMatch(value);

            return normalized.Contains("наследоватьотметку")
                || normalized.Contains("наследованиеотметок")
                || normalized.Contains("inheritelevation");
        }

        /// <summary>Убирает пробелы и переводы строк, приводит к нижнему регистру.</summary>
        private static string NormalizeForMatch(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace(" ", string.Empty)
                .Replace("\n", string.Empty)
                .Replace("\r", string.Empty)
                .Replace("\u00A0", string.Empty)
                .Replace("\u202F", string.Empty)
                .ToLowerInvariant();
        }

        private static void Log(string message)
        {
            string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message;

            try
            {
                string logDir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(logDir))
                    Directory.CreateDirectory(logDir);

                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 256 * 1024)
                    File.Delete(LogPath);

                File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
            }
        }

        /// <summary>Редкие события пишем не чаще раза в 30 секунд.</summary>
        private static void LogThrottled(string message)
        {
            DateTime nowUtc = DateTime.UtcNow;
            if (nowUtc - _lastThrottledLogUtc < ThrottledLogInterval)
                return;

            _lastThrottledLogUtc = nowUtc;
            Log(message);
        }
    }
}
