using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Threading;

namespace SheetViewsMap
{
    /// <summary>
    /// Держит опцию «Наследование отметок» включённой при черчении трубопроводов.
    ///
    /// Опция не представлена в Revit API, поэтому работаем с лентой напрямую:
    /// DispatcherTimer на потоке Revit раз в полсекунды ищет в визуальном дереве
    /// ленты кнопку-переключатель «Наследование отметок» и, если она выключена,
    /// нажимает её через AutomationPeer (полная эмуляция щелчка мышью —
    /// выполняется на потоке Revit, без UIA и без фоновых потоков).
    ///
    /// Собственные кнопки плагина (суффикс «: ВКЛ» / «: ВЫКЛ») и вкладка
    /// «Sheet Views Map» исключены из поиска.
    ///
    /// Попытки пишутся в лог inherit-log.txt.
    /// </summary>
    internal static class InheritElevationKeeper
    {
        private const string RuName = "наследоватьотметку";
        private const string RuName2 = "наследованиеотметок";
        private const string EnName = "inheritelevation";

        private static readonly TimeSpan SearchInterval = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan ClickCooldown = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ThrottledLogInterval = TimeSpan.FromSeconds(30);
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SheetViewsMap", "inherit-log.txt");

        private static DispatcherTimer _timer;
        private static DateTime _lastClickUtc = DateTime.MinValue;
        private static DateTime _lastThrottledLogUtc = DateTime.MinValue;

        /// <summary>Включает или выключает автоматическое сопровождение опции.</summary>
        internal static void SetEnabled(bool enabled)
        {
            if (enabled)
            {
                if (_timer != null)
                    return;

                Log("keeper v7 enabled");
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
            DateTime nowUtc = DateTime.UtcNow;
            if (nowUtc - _lastClickUtc < ClickCooldown)
                return;

            try
            {
                // Корень визуального дерева ленты - DependencyObject (WPF-элемент);
                // обход выполняется на потоке Revit и ничего не блокирует.
                if (!(GetRibbon() is System.Windows.DependencyObject ribbonVisual))
                {
                    LogThrottled("лента недоступна");
                    return;
                }

                TryClickInheritElevationToggle(ribbonVisual, nowUtc);
            }
            catch (Exception ex)
            {
                LogThrottled("tick error: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------ поиск кнопки

        private static void TryClickInheritElevationToggle(System.Windows.DependencyObject root, DateTime nowUtc)
        {
            var stack = new System.Collections.Generic.Stack<System.Windows.DependencyObject>();
            var visited = new System.Collections.Generic.HashSet<System.Windows.DependencyObject>();
            int toggleButtonsSeen = 0;
            bool clicked = false;

            stack.Push(root);

            while (stack.Count > 0 && !clicked)
            {
                System.Windows.DependencyObject current = stack.Pop();

                if (current == null || visited.Contains(current))
                    continue;

                visited.Add(current);

                // Интересуют WPF-переключатели и кнопки.
                if (current is System.Windows.Controls.Primitives.ToggleButton toggleButton)
                {
                    toggleButtonsSeen++;

                    string normalized = CollectVisualText(toggleButton);
                    if (MatchesTargetPhrase(normalized))
                    {
                        // Нашли настоящую кнопку «Наследование отметок»:
                        // если выключена - нажимаем через AutomationPeer.
                        if (toggleButton.IsChecked != true)
                        {
                            toggleButton.Dispatcher.Invoke
                            (
                                new Action(() =>
                                {
                                    var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(toggleButton);
                                    var toggleProvider = peer == null
                                        ? null
                                        : peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Toggle)
                                            as System.Windows.Automation.Provider.IToggleProvider;

                                    if (toggleProvider != null)
                                        toggleProvider.Toggle();
                                    else
                                        toggleButton.RaiseEvent(new System.Windows.RoutedEventArgs(
                                            System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                                }),
                                DispatcherPriority.Send
                            );

                            _lastClickUtc = nowUtc;
                            Log("нажата кнопка «Наследование отметок» (Toggle через AutomationPeer)");
                        }

                        clicked = true;
                    }
                }

                int childrenCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(current);
                for (int i = 0; i < childrenCount; i++)
                    stack.Push(System.Windows.Media.VisualTreeHelper.GetChild(current, i));
            }

            if (toggleButtonsSeen == 0)
                LogThrottled("ToggleButton на ленте не найдены (инструмент трубопровода не активен)");
            else if (!clicked)
                LogThrottled("ToggleButton: " + toggleButtonsSeen + ", целевая кнопка не найдена (опция, видимо, уже включена)");
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

        // ------------------------------------------------------------------ лента

        private static System.Windows.DependencyObject GetRibbon()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!string.Equals(assembly.GetName().Name, "UIFramework", StringComparison.OrdinalIgnoreCase))
                    continue;

                Type type = assembly.GetType("Autodesk.Windows.ComponentManager");
                object ribbon = type == null ? null : GetStaticProperty(type, "Ribbon");

                return ribbon as System.Windows.Media.Visual;
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
