using System;
using System.IO;

namespace SheetViewsMap
{
    /// <summary>
    /// Настройки плагина: хранятся в %LOCALAPPDATA%\SheetViewsMap\settings.ini
    /// и переживают перезапуск Revit.
    /// </summary>
    internal static class PluginSettings
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SheetViewsMap", "settings.ini");

        /// <summary>
        /// Режим «Наследовать отметку»: пока включён, плагин автоматически
        /// включает эту опцию при черчении трубопроводов.
        /// </summary>
        public static bool InheritElevationAlways { get; set; }

        public static void Load()
        {
            InheritElevationAlways = false;

            try
            {
                if (!File.Exists(SettingsPath))
                    return;

                foreach (string raw in File.ReadAllLines(SettingsPath))
                {
                    string line = raw ?? string.Empty;
                    int separator = line.IndexOf('=');
                    if (separator <= 0)
                        continue;

                    string key = line.Substring(0, separator).Trim();
                    string value = line.Substring(separator + 1).Trim();

                    if (string.Equals(key, "InheritElevationAlways", StringComparison.OrdinalIgnoreCase))
                        InheritElevationAlways = value == "1";
                }
            }
            catch
            {
                // Повреждённый файл настроек - просто начинаем с выключенного режима.
            }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));

                File.WriteAllLines(SettingsPath, new[]
                {
                    "InheritElevationAlways=" + (InheritElevationAlways ? "1" : "0")
                });
            }
            catch
            {
                // Не смогли сохранить - режим просто не переживёт перезапуск Revit.
            }
        }
    }
}
