using System.Globalization;
using System.Text;

namespace SheetViewsMap
{
    /// <summary>
    /// Правила имён наборов печати.
    ///
    /// Формат: «А{номер}», кратность 1 не пишется. Кратность 2 означает,
    /// что лист ровно вдвое больше, — это следующий формат (А3x2 = А2).
    /// Кратность 3 и больше в стандартный формат не переводится, поэтому
    /// пишется как есть: А1x3, А3x4 (x — латинская).
    /// </summary>
    internal static class PrintSetNaming
    {
        /// <summary>Символы, которые Revit не разрешает в именах элементов.</summary>
        private const string ForbiddenCharacters = "\\/:*?\"<>|{}[];";

        internal static string FormatLabel(int format, int multiplicity)
        {
            if (multiplicity <= 1)
                return "А" + format.ToString(CultureInfo.InvariantCulture);

            if (multiplicity == 2)
                return format > 0
                    ? "А" + (format - 1).ToString(CultureInfo.InvariantCulture)
                    : "А0x2";

            return "А" + format.ToString(CultureInfo.InvariantCulture) +
                   "x" + multiplicity.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Имя набора: комплект и формат через подчёркивание, например «АР_А1x3».</summary>
        internal static string SetName(string set, string format)
        {
            string left = Sanitize(set);
            string right = Sanitize(format);

            if (left.Length == 0)
                return right;

            return right.Length == 0 ? left : left + "_" + right;
        }

        /// <summary>Убирает символы, запрещённые в именах Revit, и лишние пробелы.</summary>
        internal static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var builder = new StringBuilder(value.Length);
            bool previousWasSpace = false;

            foreach (char symbol in value.Trim())
            {
                char current = ForbiddenCharacters.IndexOf(symbol) >= 0 ? '-' : symbol;

                if (char.IsWhiteSpace(current))
                {
                    if (previousWasSpace)
                        continue;

                    builder.Append(' ');
                    previousWasSpace = true;
                    continue;
                }

                builder.Append(current);
                previousWasSpace = false;
            }

            return builder.ToString().Trim();
        }
    }
}
