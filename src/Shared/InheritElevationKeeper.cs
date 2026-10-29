using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Automation;

namespace SheetViewsMap
{
    /// <summary>
    /// Держит опцию «Наследовать отметку» включённой при черчении трубопроводов.
    ///
    /// Опция есть только в интерфейсе Revit и не представлена в API, поэтому
    /// используется UI Automation. Таймер работает на фоновом потоке - он тикает
    /// и во время размещения труб, когда события Revit не приходят.
    ///
    /// Поиск двухпроходный: сначала точное совпадение имени, затем (раз в 5 тиков)
    /// полный обход дерева с поиском по вхождению. Каждая попытка пишется в лог
    /// inherit-log.txt - по нему видно, нашлась ли кнопка и почему не нажалась.
    /// </summary>
    internal static class InheritElevationKeeper
    {
        private const string RuName = "Наследовать отметку";
        private const string EnName = "Inherit Elevation";

        private static readonly TimeSpan SearchInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan BroadWalkInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan InvokeCooldown = TimeSpan.FromSeconds(3);
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SheetViewsMap", "inherit-log.txt");

        private static Timer _timer;
        private static int _revitProcessId = -1;
        private static int _tick;
        private static DateTime _lastInvokeUtc = DateTime.MinValue;
        private static DateTime _lastBroadWalkUtc = DateTime.MinValue;
        private static DateTime _lastNotFoundLogUtc = DateTime.MinValue;

        /// <summary>Включает или выключает автоматическое сопровождение опции.</summary>
        internal static void SetEnabled(bool enabled)
        {
            if (enabled)
            {
                if (_timer != null)
                    return;

                _revitProcessId = Process.GetCurrentProcess().Id;
                Log("keeper enabled (v2)");
                _timer = new Timer(OnTimerTick, null, TimeSpan.Zero, SearchInterval);
            }
            else
            {
                if (_timer == null)
                    return;

                _timer.Dispose();
                _timer = null;
                Log("keeper disabled");
            }
        }

        private static void OnTimerTick(object state)
        {
            DateTime nowUtc = DateTime.UtcNow;
            if (nowUtc - _lastInvokeUtc < InvokeCooldown)
                return;

            try
            {
                ToggleIfNeeded(nowUtc);
            }
            catch (Exception ex)
            {
                Log("tick error: " + ex.Message);
            }
        }

        private static void ToggleIfNeeded(DateTime nowUtc)
        {
            AutomationElement revitWindow = GetRevitWindow();
            if (revitWindow == null)
            {
                LogThrottled("окно Revit не найдено");
                return;
            }

            // Проход 1: точное имя (быстро, средствами UIA).
            AutomationElement toggle = FindExact(revitWindow);

            // Проход 2: не чаще раза в 5 секунд - полный обход дерева
            // с поиском по вхождению и любым типом элемента.
            bool broad = false;
            if (toggle == null && nowUtc - _lastBroadWalkUtc >= BroadWalkInterval)
            {
                _lastBroadWalkUtc = nowUtc;
                broad = true;
                toggle = WalkAndFind(revitWindow);
            }

            if (toggle == null)
            {
                LogThrottled(broad
                    ? "кнопка не найдена (инструмент трубопровода не активен или имя другое)"
                    : "кнопка не найдена точным поиском");
                return;
            }

            string description = Describe(toggle);
            Log("найдена: " + description);

            object pattern;
            if (toggle.TryGetCurrentPattern(TogglePattern.Pattern, out pattern))
            {
                var togglePattern = (TogglePattern)pattern;
                ToggleState state = togglePattern.Current.ToggleState;
                Log("TogglePattern, состояние: " + state);

                if (state == ToggleState.Off)
                {
                    togglePattern.Toggle();
                    _lastInvokeUtc = nowUtc;
                    Log("нажата (Toggle)");
                }

                return;
            }

            if (toggle.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern))
            {
                var selectionPattern = (SelectionItemPattern)pattern;
                bool selected = selectionPattern.Current.IsSelected;
                Log("SelectionItemPattern, выбран: " + selected);

                if (!selected)
                {
                    selectionPattern.Select();
                    _lastInvokeUtc = nowUtc;
                    Log("нажата (Select)");
                }

                return;
            }

            // Состояние прочитать нечем: вслепую не нажимаем - можно выключить
            // включённую опцию. Подробности в логе помогут разобраться.
            Log("ВНИМАНИЕ: у кнопки нет Toggle/SelectionItem паттерна, состояние неизвестно - не нажимаю");
        }

        // ------------------------------------------------------------------ поиск

        private static AutomationElement GetRevitWindow()
        {
            return AutomationElement.RootElement.FindFirst(
                TreeScope.Children,
                new PropertyCondition(AutomationElement.ProcessIdProperty, _revitProcessId));
        }

        private static AutomationElement FindExact(AutomationElement root)
        {
            var byName = new OrCondition(
                new PropertyCondition(AutomationElement.NameProperty, RuName),
                new PropertyCondition(AutomationElement.NameProperty, EnName));

            return root.FindFirst(TreeScope.Descendants, byName);
        }

        /// <summary>Обход дерева в глубину: имя содержит целевую строку, любой тип элемента.</summary>
        private static AutomationElement WalkAndFind(AutomationElement root)
        {
            var stack = new Stack<AutomationElement>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                AutomationElement element = stack.Pop();

                string name = null;
                try
                {
                    name = element.Current.Name;
                }
                catch (ElementNotAvailableException)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(name) &&
                    (name.IndexOf(RuName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     name.IndexOf(EnName, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return element;
                }

                AutomationElement child = TreeWalker.ControlViewWalker.GetFirstChild(element);
                while (child != null)
                {
                    stack.Push(child);
                    child = TreeWalker.ControlViewWalker.GetNextSibling(child);
                }
            }

            return null;
        }

        // ------------------------------------------------------------------ лог и мелочи

        private static string Describe(AutomationElement element)
        {
            try
            {
                var patterns = new List<string>();
                foreach (AutomationPattern pattern in element.GetSupportedPatterns())
                    patterns.Add(pattern.ProgrammaticName);

                return string.Format(
                    "\"{0}\" (тип {1}, класс {2}; паттерны: {3})",
                    element.Current.Name,
                    element.Current.ControlType,
                    element.Current.ClassName,
                    patterns.Count > 0 ? string.Join(", ", patterns) : "нет");
            }
            catch (Exception ex)
            {
                return "(не удалось описать элемент: " + ex.Message + ")";
            }
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

        /// <summary>«Не найдено» пишем не чаще раза в 30 секунд, чтобы лог не разбухал.</summary>
        private static void LogThrottled(string message)
        {
            DateTime nowUtc = DateTime.UtcNow;
            if (nowUtc - _lastNotFoundLogUtc < TimeSpan.FromSeconds(30))
                return;

            _lastNotFoundLogUtc = nowUtc;
            Log(message);
        }
    }
}
