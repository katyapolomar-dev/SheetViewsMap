using System;
using System.Diagnostics;
using System.Windows.Automation;
using System.Windows.Threading;

namespace SheetViewsMap
{
    /// <summary>
    /// Держит опцию «Наследовать отметку» включённой при черчении трубопроводов.
    ///
    /// Эта опция есть только в интерфейсе Revit (в контекстной вкладке черчения
    /// труб) и никак не представлена в API Revit. Поэтому работаем через
    /// UI Automation: раз в секунду ищем в ленте переключатель
    /// «Наследовать отметку» / «Inherit Elevation» и, если он выключен,
    /// программно нажимаем его. Вне режима черчения труб кнопки в интерфейсе
    /// нет - поиск ничего не находит и не нажимает лишнего.
    /// </summary>
    internal static class InheritElevationKeeper
    {
        private const string RuName = "Наследовать отметку";
        private const string EnName = "Inherit Elevation";

        private static readonly TimeSpan SearchInterval = TimeSpan.FromSeconds(1);

        /// <summary>После программного нажатия даём ленте время применить состояние.</summary>
        private static readonly TimeSpan InvokeCooldown = TimeSpan.FromSeconds(3);

        private static DispatcherTimer _timer;
        private static int _revitProcessId = -1;
        private static DateTime _lastInvokeUtc = DateTime.MinValue;

        /// <summary>Включает или выключает автоматическое сопровождение опции.</summary>
        internal static void SetEnabled(bool enabled)
        {
            if (enabled)
            {
                if (_timer != null)
                    return;

                _revitProcessId = Process.GetCurrentProcess().Id;
                _timer = new DispatcherTimer { Interval = SearchInterval };
                _timer.Tick += OnTimerTick;
                _timer.Start();
            }
            else
            {
                if (_timer == null)
                    return;

                _timer.Stop();
                _timer.Tick -= OnTimerTick;
                _timer = null;
            }
        }

        private static void OnTimerTick(object sender, EventArgs e)
        {
            DateTime nowUtc = DateTime.UtcNow;
            if (nowUtc - _lastInvokeUtc < InvokeCooldown)
                return;

            try
            {
                ToggleIfNeeded(nowUtc);
            }
            catch
            {
                // Лента может перестраиваться в произвольный момент - молча ждём следующего тика.
            }
        }

        private static void ToggleIfNeeded(DateTime nowUtc)
        {
            AutomationElement revitWindow = GetRevitWindow();
            if (revitWindow == null)
                return;

            AutomationElement toggle = FindToggle(revitWindow);
            if (toggle == null)
                return; // сейчас инструмент трубопровода не активен - кнопки нет

            object pattern;
            if (toggle.TryGetCurrentPattern(TogglePattern.Pattern, out pattern))
            {
                var togglePattern = (TogglePattern)pattern;
                if (togglePattern.Current.ToggleState == ToggleState.Off)
                {
                    togglePattern.Toggle();
                    _lastInvokeUtc = nowUtc;
                }

                return;
            }

            if (toggle.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern))
            {
                var selectionPattern = (SelectionItemPattern)pattern;
                if (!selectionPattern.Current.IsSelected)
                {
                    selectionPattern.Select();
                    _lastInvokeUtc = nowUtc;
                }
            }
        }

        /// <summary>Главное окно текущего процесса (это и есть окно Revit).</summary>
        private static AutomationElement GetRevitWindow()
        {
            return AutomationElement.RootElement.FindFirst(
                TreeScope.Children,
                new PropertyCondition(AutomationElement.ProcessIdProperty, _revitProcessId));
        }

        private static AutomationElement FindToggle(AutomationElement root)
        {
            // Быстрый точный поиск по обоим языкам.
            var byName = new OrCondition(
                new PropertyCondition(AutomationElement.NameProperty, RuName),
                new PropertyCondition(AutomationElement.NameProperty, EnName));

            AutomationElement element = root.FindFirst(TreeScope.Descendants, byName);
            if (element != null)
                return element;

            // Точное имя не нашлось (к имени мог добавиться горячий ключ) -
            // перебираем кнопки и ищем вхождение без учёта регистра.
            AutomationElementCollection buttons = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

            foreach (AutomationElement button in buttons)
            {
                string name = button.Current.Name;
                if (string.IsNullOrEmpty(name))
                    continue;

                if (name.IndexOf(RuName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf(EnName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return button;
                }
            }

            return null;
        }
    }
}
