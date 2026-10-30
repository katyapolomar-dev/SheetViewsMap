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
    /// Логика: кнопка плагина включена → при каждом ВЫБОРЕ инструмента
    /// «Труба» (появлении контекстной вкладки черчения) плагин ОДИН РАЗ нажимает
    /// переключатель «Наследование отметок» на ленте. Дальше не трогает —
    /// до следующего выбора инструмента. Состояние кнопки сохраняется
    /// между сессиями Revit (settings.ini).
    ///
    /// Опция не представлена в Revit API, поэтому работаем с лентой напрямую:
    /// DispatcherTimer на потоке Revit находит кнопку в визуальном WPF-дереве
    /// ленты и нажимает её через AutomationPeer (полная эмуляция щелчка мышью).
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
        private static Type _componentManagerType;
        private static Assembly _ribbonSourceAssembly;
        private static DateTime _lastClickUtc = DateTime.MinValue;
        private static DateTime _lastThrottledLogUtc = DateTime.MinValue;
        private static DateTime _lastDumpLogUtc = DateTime.MinValue;

        /// <summary>Активен ли инструмент черчения труб (в прошлый тик).</summary>
        private static bool _pipeToolWasActive;

        /// <summary>Сколько попыток нажатия осталось для текущего выбора инструмента.</summary>
        private static int _clickAttemptsLeft;

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
                // Корень визуального дерева ленты - WPF-элемент; обход
                // выполняется на потоке Revit и ничего не блокирует.
                if (!(GetRibbon() is System.Windows.DependencyObject ribbon))
                {
                    LogThrottled("лента недоступна");
                    return;
                }

                // Момент выбора инструмента «Труба»: переход «не активна → активна».
                bool pipeToolActive = IsPipeToolActive(ribbon);
                bool justActivated = pipeToolActive && !_pipeToolWasActive;
                _pipeToolWasActive = pipeToolActive;

                if (!pipeToolActive)
                {
                    LogThrottled("инструмент трубопровода не активен");
                    return;
                }

                // Одно нажатие на каждое включение инструмента: после перехода
                // делаем до 10 попыток (по тику), пока контекстная панель
                // «Инструменты размещения» не появится в визуальном дереве.
                if (justActivated)
                    _clickAttemptsLeft = 10;

                if (_clickAttemptsLeft <= 0)
                {
                    DumpPipeTabDiagnostics(ribbon, nowUtc);
                    return;
                }

                _clickAttemptsLeft--;

                if (TryClickInheritElevationToggle(ribbon, nowUtc))
                {
                    _clickAttemptsLeft = 0;
                    return;
                }

                if (_clickAttemptsLeft == 0)
                    LogThrottled("переключатель не найден за 5 секунд после включения инструмента");
            }
            catch (Exception ex)
            {
                LogThrottled("tick error: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------ нажатие

        /// <summary>
        /// Ищет в визуальном дереве ленты переключатель «Наследование отметок».
        /// true - переключатель найден (нажат, если был выключен);
        /// false - не найден (визуальное дерево ещё не построено или режим не тот).
        /// </summary>
        private static bool TryClickInheritElevationToggle(System.Windows.DependencyObject root, DateTime nowUtc)
        {
            var stack = new Stack<System.Windows.DependencyObject>();
            var visited = new HashSet<System.Windows.DependencyObject>();
            int toggleButtonsSeen = 0;

            stack.Push(root);

            while (stack.Count > 0)
            {
                System.Windows.DependencyObject current = stack.Pop();

                if (current == null || visited.Contains(current))
                    continue;

                visited.Add(current);

                // Интересуют WPF-переключатели.
                if (current is System.Windows.Controls.Primitives.ToggleButton toggleButton)
                {
                    toggleButtonsSeen++;

                    string normalized = CollectVisualText(toggleButton);
                    if (MatchesTargetPhrase(normalized))
                    {
                        // Нашли настоящую кнопку «Наследование отметок»:
                        // если выключена - нажимаем через AutomationPeer
                        // (тот же код, что и при щелчке мышью).
                        if (toggleButton.IsChecked != true)
                        {
                            toggleButton.Dispatcher.Invoke(new Action(() =>
                            {
                                var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(toggleButton);
                                var provider = peer == null
                                    ? null
                                    : peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Toggle)
                                        as System.Windows.Automation.Provider.IToggleProvider;

                                provider?.Toggle();
                            }), DispatcherPriority.Send);

                            _lastClickUtc = nowUtc;
                            Log("нажата кнопка «Наследование отметок»");
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

        /// <summary>Приводит объект к IEnumerable (или возвращает пустой набор).</summary>
        private static IEnumerable Each(object collection)
        {
            var enumerable = collection as IEnumerable;
            if (enumerable != null)
                return enumerable;

            return new object[0];
        }

        // ------------------------------------------------------------------ диагностика

        /// <summary>Раз в 30 секунд пишет компактный дамп контекстной вкладки
        /// черчения труб (если инструмент активен и переключатель ещё не нажат).</summary>
        private static void DumpPipeTabDiagnostics(object ribbon, DateTime nowUtc)
        {
            object tabs = GetProperty(ribbon, "Tabs");
            object contextual = GetProperty(ribbon, "ContextualTabs");

            var tabObjects = new List<object>();

            if (tabs != null)
                foreach (object tab in Each(tabs))
                    tabObjects.Add(tab);

            if (contextual != null)
                foreach (object tab in Each(contextual))
                    tabObjects.Add(tab);

            foreach (object tab in tabObjects)
            {
                string title = GetString(tab, "Title") ?? string.Empty;
                if (title.IndexOf("Труба", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (nowUtc - _lastDumpLogUtc < TimeSpan.FromSeconds(30))
                    continue;

                _lastDumpLogUtc = nowUtc;
                Log("=== ДАМП вкладки \"" + title + "\" ===");
                Log(DumpTabCompact(tab));
                Log("=== конец дампа ===");
            }
        }

        /// <summary>Компактный дамп вкладки: каждая панель и каждый элемент — одной строкой.</summary>
        private static string DumpTabCompact(object tab)
        {
            var sb = new StringBuilder();
            string tabTitle = GetString(tab, "Title") ?? "?";
            sb.AppendLine("Вкладка \"" + tabTitle + "\":");

            object panels = GetProperty(tab, "Panels");
            if (panels == null)
            {
                sb.AppendLine("  (свойство Panels не найдено)");
                return sb.ToString();
            }

            foreach (object panel in Each(panels))
            {
                object source = GetProperty(panel, "Source");
                string panelTitle = source == null ? "?" : GetString(source, "Title") ?? "?";
                sb.AppendLine("  Панель \"" + panelTitle + "\":");

                object items = source == null ? null : GetProperty(source, "Items");
                if (items == null)
                {
                    sb.AppendLine("    (свойство Items не найдено)");
                    continue;
                }

                int index = 0;
                foreach (object entry in Each(items))
                {
                    sb.AppendLine("    " + DescribeCompact(entry));

                    if (++index >= 30)
                    {
                        sb.AppendLine("    …(дальше обрезано)");
                        break;
                    }
                }
            }

            return sb.ToString();
        }

        /// <summary>Краткое описание элемента ленты: тип, подписи, состояние.</summary>
        private static string DescribeCompact(object entry)
        {
            string type = entry.GetType().Name;
            string title = GetString(entry, "Title");
            string text = GetString(entry, "Text");
            string automationName = GetString(entry, "AutomationName");
            object isChecked = GetProperty(entry, "IsChecked");
            string state = isChecked is bool b ? ((bool)b ? "ВКЛ" : "выкл") : "-";

            return type
                + " | Title=\"" + (title ?? "-") + "\""
                + " | Text=\"" + (text ?? "-") + "\""
                + " | AutomationName=\"" + (automationName ?? "-") + "\""
                + " | IsChecked=" + state;
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
