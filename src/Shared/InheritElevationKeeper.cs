using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Threading;

namespace SheetViewsMap
{
    /// <summary>
    /// Держит опцию «Наследовать отметку» включённой при черчении трубопроводов.
    ///
    /// Опция не представлена в Revit API, но лента Revit — это WPF-объекты с
    /// объектной моделью Autodesk.Windows (сборка UIFramework.dll, грузится самим
    /// Revit'ом). Доступ через рефлексию - это работает на всех годах Revit и на
    /// обоих рантаймах (.NET Framework и .NET 8), без ссылок на внутренние сборки.
    ///
    /// DispatcherTimer на потоке Revit раз в полсекунды обходит модель ленты,
    /// находит переключатель «Наследовать отметку» и ставит IsChecked = true,
    /// если он выключен. Попытки пишутся в лог inherit-log.txt.
    /// </summary>
    internal static class InheritElevationKeeper
    {
        private const string RuName = "наследовать отметку";
        private const string EnName = "inherit elevation";

        private static readonly TimeSpan SearchInterval = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan ApplyCooldown = TimeSpan.FromSeconds(2);
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SheetViewsMap", "inherit-log.txt");

        private static DispatcherTimer _timer;
        private static DateTime _lastApplyUtc = DateTime.MinValue;
        private static DateTime _lastNotFoundLogUtc = DateTime.MinValue;

        /// <summary>Включает или выключает автоматическое сопровождение опции.</summary>
        internal static void SetEnabled(bool enabled)
        {
            if (enabled)
            {
                if (_timer != null)
                    return;

                Log("keeper v4 enabled");
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
            if (nowUtc - _lastApplyUtc < ApplyCooldown)
                return;

            try
            {
                ApplyIfNeeded(nowUtc);
            }
            catch (Exception ex)
            {
                LogThrottled("tick error: " + ex.Message);
            }
        }

        private static void ApplyIfNeeded(DateTime nowUtc)
        {
            object ribbon = GetRibbon();
            if (ribbon == null)
            {
                LogThrottled("лента недоступна");
                return;
            }

            object tabs = GetProperty(ribbon, "Tabs");
            if (tabs == null)
                return;

            var tabTitles = new List<string>();

            foreach (object tab in Each(tabs))
            {
                tabTitles.Add(GetString(tab, "Title") ?? "?");

                object panels = GetProperty(tab, "Panels");
                if (panels == null)
                    continue;

                foreach (object panel in Each(panels))
                {
                    object source = GetProperty(panel, "Source");
                    object items = source == null ? null : GetProperty(source, "Items");

                    if (items != null && WalkItems(items, nowUtc, 0))
                        return;
                }
            }

            LogThrottled("переключатель не найден; вкладки: " +
                (tabTitles.Count > 0 ? string.Join(" | ", tabTitles.ToArray()) : "нет"));
        }

        private static object GetRibbon()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!string.Equals(assembly.GetName().Name, "UIFramework", StringComparison.OrdinalIgnoreCase))
                    continue;

                Type type = assembly.GetType("Autodesk.Windows.ComponentManager");
                return type == null ? null : GetStaticProperty(type, "Ribbon");
            }

            return null;
        }

        private static bool WalkItems(object items, DateTime nowUtc, int depth)
        {
            if (items == null || depth > 5)
                return false;

            foreach (object entry in Each(items))
            {
                if (entry == null)
                    continue;

                string text = GetString(entry, "Title") ?? GetString(entry, "Text") ?? string.Empty;
                bool matches = text.IndexOf(RuName, StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf(EnName, StringComparison.OrdinalIgnoreCase) >= 0;

                string typeName = entry.GetType().Name;

                // Любой элемент с вхождением имени и булевым IsChecked -
                // это переключатель (RibbonToggleButton / RibbonSplitToggleButton / ...).
                if (matches)
                {
                    object isChecked = GetProperty(entry, "IsChecked");
                    if (isChecked is bool)
                    {
                        if (!(bool)isChecked)
                        {
                            SetProperty(entry, "IsChecked", true);
                            _lastApplyUtc = nowUtc;
                            Log("включена: \"" + text.Trim() + "\" (" + typeName + ")");
                        }

                        return true;
                    }
                }

                // Контейнеры (сплит/пулдаун-кнопки) обходим рекурсивно.
                object inner = GetProperty(entry, "Items");
                if (inner != null)
                {
                    if (matches && typeName == "RibbonSplitButton")
                    {
                        object isChecked = GetProperty(entry, "IsChecked");
                        bool on = isChecked is bool && (bool)isChecked;

                        if (!on)
                        {
                            SetProperty(entry, "IsChecked", true);
                            _lastApplyUtc = nowUtc;
                            Log("включен сплит: \"" + text.Trim() + "\"");
                        }

                        return true;
                    }

                    if (WalkItems(inner, nowUtc, depth + 1))
                        return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------ рефлексия

        private static IEnumerable Each(object collection)
        {
            var enumerable = collection as IEnumerable;
            if (enumerable != null)
                return enumerable;

            // Не-коллекция (например, свойство вернуло null) - пустой набор.
            return new object[0];
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

        private static void SetProperty(object target, string name, object value)
        {
            try
            {
                target.GetType().GetProperty(name)?.SetValue(target, value, null);
            }
            catch
            {
            }
        }

        // ------------------------------------------------------------------ лог

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
