using System;

namespace SheetViewsMap
{
    /// <summary>
    /// Сравнение версий вида «X.Y.Z».
    /// В .NET Framework нет парсера семантических версий, а Version.Parse
    /// требует ровно четыре части — поэтому сравниваем три числа вручную.
    /// </summary>
    internal static class PluginVersion
    {
        /// <summary>
        /// -1: left старше, 0: равны, 1: left новее. Суффиксы «v», «+сборка»
        /// и «-превью» игнорируются.
        /// </summary>
        public static int Compare(string left, string right)
        {
            int[] a = Parse(left);
            int[] b = Parse(right);

            for (int i = 0; i < a.Length; i++)
            {
                int cmp = a[i].CompareTo(b[i]);
                if (cmp != 0)
                    return cmp;
            }

            return 0;
        }

        private static int[] Parse(string version)
        {
            var result = new[] { 0, 0, 0 };

            if (string.IsNullOrEmpty(version))
                return result;

            string core = version.Trim().TrimStart('v', 'V');

            // Отрезаем метки сборки и превью: «1.2.0+7» и «1.2.0-beta» — это 1.2.0.
            int plus = core.IndexOf('+');
            if (plus >= 0)
                core = core.Substring(0, plus);

            int dash = core.IndexOf('-');
            if (dash >= 0)
                core = core.Substring(0, dash);

            string[] parts = core.Split('.');
            for (int i = 0; i < result.Length && i < parts.Length; i++)
            {
                int value;
                if (int.TryParse(parts[i], out value))
                    result[i] = value;
            }

            return result;
        }
    }
}
