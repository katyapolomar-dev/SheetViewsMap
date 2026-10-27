using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SheetViewsMap
{
    /// <summary>
    /// Читает из документа листы и размещённые на них виды.
    /// Вызывается ТОЛЬКО из обработчика ExternalEvent, то есть в корректном контексте Revit API.
    /// </summary>
    internal static class SheetMapReader
    {
        /// <summary>
        /// Параметр листа, задающий комплект (например «05-26-01-ТК»).
        /// Задаётся в проекте Autodesk'ом, название может отличаться - тогда
        /// поправьте строку ниже.
        /// </summary>
        internal const string SetParameterName = "ADSK_Штамп Раздел проекта";

        internal static SheetMapData Read(UIApplication app)
        {
            UIDocument uidoc = (app == null) ? null : app.ActiveUIDocument;
            if (uidoc == null || uidoc.Document == null)
                return SheetMapData.Failed("Нет открытого документа.");

            Document doc = uidoc.Document;

            if (doc.IsFamilyDocument)
                return SheetMapData.Failed("Открыто семейство (.rfa). Нужен проект (.rvt) с листами.");

            var data = new SheetMapData
            {
                DocumentName = doc.Title,
                Rows = new List<SheetMapRow>()
            };

            List<ViewSheet> sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsTemplate)
                .ToList();

            // ViewSheet.GetAllPlacedViews() НЕ возвращает графики (ScheduleSheetInstance),
            // поэтому собираем их отдельно и добавляем к списку видов листа.
            Dictionary<ElementId, List<ElementId>> schedulesBySheet = CollectSchedules(doc);

            var rowsByView = new Dictionary<ElementId, SheetMapRow>();

            foreach (ViewSheet sheet in sheets)
            {
                var viewIds = new HashSet<ElementId>(sheet.GetAllPlacedViews());

                List<ElementId> schedules;
                if (schedulesBySheet.TryGetValue(sheet.Id, out schedules))
                {
                    foreach (ElementId scheduleId in schedules)
                        viewIds.Add(scheduleId);
                }

                // Сначала разбираем лист целиком: комплект и проверка нумерации разрезов.
                string setKey = GetSetKey(sheet);
                int sectionStart = FindSectionStart(doc, viewIds);

                foreach (ElementId viewId in viewIds)
                {
                    View view = doc.GetElement(viewId) as View;
                    if (view == null)
                        continue;

                    // Ведомость изменений и спецификации в таблице не нужны.
                    if (IsExcludedView(view))
                        continue;

                    SheetMapRow row;
                    if (!rowsByView.TryGetValue(viewId, out row))
                    {
                        row = new SheetMapRow
                        {
                            ViewId = viewId,
                            ViewNumber = GetViewNumber(view),
                            ViewName = view.Name,
                            SheetRefs = new List<SheetRef>()
                        };
                        rowsByView.Add(viewId, row);
                    }

                    row.SheetRefs.Add(new SheetRef
                    {
                        Number = SafeSheetNumber(sheet),
                        Name = sheet.Name,
                        SheetId = sheet.Id,
                        SetKey = setKey,
                        SectionStart = sectionStart,

                        // Разрезы на листе есть, но начинаются не с единицы.
                        IsProblem = sectionStart > 0 && sectionStart != 1
                    });
                }
            }

            List<SheetMapRow> rows = rowsByView.Values.ToList();

            foreach (SheetMapRow row in rows)
            {
                // Убираем повторы по номеру листа. Столбец «Листы» выводит
                // именно SheetRefs, поэтому чистить надо здесь - иначе один
                // и тот же номер печатается в ячейке несколько раз.
                row.SheetRefs = row.SheetRefs
                    .GroupBy(s => s.Number, StringComparer.OrdinalIgnoreCase)
                    .Select(g => MergeSheetRefs(g))
                    .OrderBy(s => s.Number, NaturalStringComparer.Instance)
                    .ToList();

                row.Sheets = string.Join("; ", row.SheetRefs
                    .Select(s => s.IsProblem ? s.Number + " \u26A0" : s.Number));

                row.SheetCount = row.SheetRefs.Count;
                row.IsProblem = row.SheetRefs.Any(s => s.IsProblem);
                row.Sets = string.Join("; ", row.SheetRefs
                    .Select(s => s.SetKey)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
            }

            data.Rows = rows
                .OrderBy(r => r.ViewNumber, NaturalStringComparer.Instance)
                .ToList();
            data.SheetCount = sheets.Count;

            return data;
        }

        /// <summary>
        /// Схлопывает листы с одинаковым номером в один. Если хоть один из них
        /// проблемный - помечаем номер знаком ⚠.
        /// </summary>
        private static SheetRef MergeSheetRefs(IEnumerable<SheetRef> group)
        {
            var list = group.ToList();
            SheetRef first = list[0];

            if (!first.IsProblem && list.Any(s => s.IsProblem))
                first.IsProblem = true;

            return first;
        }

        /// <summary>
        /// Фрагменты имён видов, которые не должны попадать в таблицу:
        /// ведомость изменений и спецификация. В таблицу идут виды с номерами,
        /// а у этих вещей номеров нет - только мешают.
        ///
        /// Отличить их по типу нельзя: в публичном API нет ни RevisionSchedule,
        /// ни MultiCategorySchedule, все графики имеют ViewType.Schedule.
        /// Поэтому сверяем имя вида. Список открыт для правки.
        /// </summary>
        private static readonly string[] ExcludedViewNameParts =
        {
            "ведомость измен",   // Ведомость изменений
            "revision schedule", // Ведомость изменений (English UI)
            "специфика",         // Спецификация
            "multi-category",    // Multi-Category Schedule
            "multicategory"
        };

        private static bool IsExcludedView(View view)
        {
            string name = view.Name ?? string.Empty;
            if (name.Length == 0)
                return false;

            foreach (string part in ExcludedViewNameParts)
            {
                // Ordinal, а не обычный Contains: culture-сравнение считает
                // «е» и «ё» одинаковыми и даёт ложные совпадения.
                if (name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Комплект листа - значение параметра «ADSK_Штамп Раздел проекта».
        ///
        /// Ищем не только по точному имени: у параметра бывают варианты
        /// написания («ADSK_» без префикса, другое местоимение), а сам параметр
        /// может быть не текстовым, а ссылкой на элемент - тогда берём имя элемента.
        /// </summary>
        private static string GetSetKey(ViewSheet sheet)
        {
            Parameter parameter = FindSetParameter(sheet);
            if (parameter == null || !parameter.HasValue)
                return "(без комплекта)";

            if (parameter.StorageType == StorageType.String)
            {
                string value = parameter.AsString();
                return string.IsNullOrEmpty(value) || value.Trim().Length == 0
                    ? "(без комплекта)"
                    : value.Trim();
            }

            if (parameter.StorageType == StorageType.ElementId)
            {
                Element element = sheet.Document.GetElement(parameter.AsElementId());
                if (element != null && !string.IsNullOrEmpty(element.Name))
                    return element.Name.Trim();
            }

            if (parameter.StorageType == StorageType.Integer)
                return parameter.AsInteger().ToString();

            return "(без комплекта)";
        }

        private static Parameter FindSetParameter(ViewSheet sheet)
        {
            // 1. Точное имя.
            Parameter exact = sheet.LookupParameter(SetParameterName);
            if (exact != null)
                return exact;

            // 2. Без префикса «ADSK_».
            if (SetParameterName.StartsWith("ADSK_", StringComparison.OrdinalIgnoreCase))
            {
                Parameter withoutPrefix = sheet.LookupParameter(SetParameterName.Substring("ADSK_".Length));
                if (withoutPrefix != null)
                    return withoutPrefix;
            }

            // 3. Любой параметр, в названии которого есть и «штамп», и «раздел».
            foreach (Parameter parameter in sheet.Parameters)
            {
                if (parameter == null || parameter.IsReadOnly)
                    continue;

                string name = parameter.Definition == null ? string.Empty : parameter.Definition.Name;
                string lower = name.ToLowerInvariant();

                if (lower.Contains("штамп") && lower.Contains("раздел"))
                    return parameter;
            }

            return null;
        }

        /// <summary>
        /// С какого номера начинаются разрезы (ViewType.Section) на листе.
        /// Возвращает 0, если разрезов на листе нет.
        /// </summary>
        private static int FindSectionStart(Document doc, IEnumerable<ElementId> viewIds)
        {
            int result = 0;

            foreach (ElementId viewId in viewIds)
            {
                View view = doc.GetElement(viewId) as View;
                if (view == null || view.ViewType != ViewType.Section)
                    continue;

                int number = ParseTrailingNumber(GetViewNumber(view));
                if (number > 0 && (result == 0 || number < result))
                    result = number;
            }

            return result;
        }

        /// <summary>
        /// Достаёт число в конце номера вида: «5» → 5, «Р-12» → 12, «А-1.1» → 1.
        /// Если номер не заканчивается цифрой («3D», «АВТ»), вернётся 0 -
        /// такие номера не считаем нумерацией.
        /// </summary>
        private static int ParseTrailingNumber(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            // Номер обязан заканчиваться цифрой, иначе перед нами не номер вида,
            // а что-то вроде «3D» - и из него нельзя взять «3».
            if (!char.IsDigit(text[text.Length - 1]))
                return 0;

            int start = text.Length;
            while (start > 0 && char.IsDigit(text[start - 1]))
                start--;

            int value;
            return int.TryParse(text.Substring(start), out value) ? value : 0;
        }

        /// <summary>
        /// Графики на листах: у каждого ScheduleSheetInstance владельцем является лист (OwnerViewId).
        /// </summary>
        private static Dictionary<ElementId, List<ElementId>> CollectSchedules(Document doc)
        {
            var result = new Dictionary<ElementId, List<ElementId>>();

            IEnumerable<ScheduleSheetInstance> instances =
                new FilteredElementCollector(doc)
                    .OfClass(typeof(ScheduleSheetInstance))
                    .Cast<ScheduleSheetInstance>();

            foreach (ScheduleSheetInstance instance in instances)
            {
                ElementId owner = instance.OwnerViewId;
                if (owner == null || owner == ElementId.InvalidElementId)
                    continue;

                List<ElementId> list;
                if (!result.TryGetValue(owner, out list))
                {
                    list = new List<ElementId>();
                    result.Add(owner, list);
                }

                if (!list.Contains(instance.ScheduleId))
                    list.Add(instance.ScheduleId);
            }

            return result;
        }

        /// <summary>
        /// Имена параметра «Номер вида».
        ///
        /// В Revit API нет BuiltInParameter.VIEW_NUMBER — номер вида приходится искать
        /// по имени, а имена параметров зависят от языка интерфейса Revit.
        /// Русское имя — «Номер вида» (проверено по ресурсам RevitRes.dll), английское — «View Number».
        /// Если ваш Revit на другом языке — допишите нужное имя в начало списка.
        /// </summary>
        private static readonly string[] ViewNumberNames =
        {
            "Номер вида",
            "View Number"
        };

        private static string GetViewNumber(View view)
        {
            if (view == null)
                return string.Empty;

            // 1. Прямой поиск по известным именам.
            foreach (string name in ViewNumberNames)
            {
                Parameter p = view.LookupParameter(name);
                if (p != null && p.HasValue && p.StorageType == StorageType.String)
                {
                    string value = p.AsString();
                    if (!string.IsNullOrEmpty(value))
                        return value;
                }
            }

            // 2. Запасной вариант для незнакомого языка:
            //    ищем единственный текстовый параметр, в названии которого есть «номер»,
            //    но нет «лист» (иначе это будет «Номер листа»).
            var candidates = new List<Parameter>();

            foreach (Parameter p in view.Parameters)
            {
                if (p.StorageType != StorageType.String || !p.HasValue)
                    continue;

                string name = p.Definition == null ? string.Empty : p.Definition.Name;
                string lower = name.ToLowerInvariant();

                if (lower.IndexOf("номер", StringComparison.Ordinal) >= 0
                    || lower.IndexOf("number", StringComparison.Ordinal) >= 0
                    || lower.IndexOf("numero", StringComparison.Ordinal) >= 0
                    || lower.IndexOf("nummer", StringComparison.Ordinal) >= 0)
                {
                    if (lower.IndexOf("лист", StringComparison.Ordinal) >= 0
                        || lower.IndexOf("sheet", StringComparison.Ordinal) >= 0)
                        continue;

                    candidates.Add(p);
                }
            }

            if (candidates.Count == 1)
            {
                string value = candidates[0].AsString();
                if (!string.IsNullOrEmpty(value))
                    return value;
            }

            return string.Empty;
        }

        private static string SafeSheetNumber(ViewSheet sheet)
        {
            try
            {
                string value = sheet.SheetNumber;
                return string.IsNullOrEmpty(value) ? "(без номера)" : value;
            }
            catch
            {
                return "(без номера)";
            }
        }
    }

    /// <summary>
    /// Сортировка «по-человечески»: А-2 идёт раньше А-10, а не наоборот.
    /// </summary>
    internal sealed class NaturalStringComparer : IComparer<string>
    {
        internal static readonly NaturalStringComparer Instance = new NaturalStringComparer();

        public int Compare(string x, string y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (string.IsNullOrEmpty(x)) return string.IsNullOrEmpty(y) ? 0 : -1;
            if (string.IsNullOrEmpty(y)) return 1;

            int i = 0, j = 0;

            while (i < x.Length && j < y.Length)
            {
                bool digitX = char.IsDigit(x[i]);
                bool digitY = char.IsDigit(y[j]);

                if (digitX && digitY)
                {
                    int startX = i, startY = j;
                    while (i < x.Length && char.IsDigit(x[i])) i++;
                    while (j < y.Length && char.IsDigit(y[j])) j++;

                    string numX = x.Substring(startX, i - startX).TrimStart('0');
                    string numY = y.Substring(startY, j - startY).TrimStart('0');

                    if (numX.Length != numY.Length)
                        return numX.Length - numY.Length;

                    int cmp = string.CompareOrdinal(numX, numY);
                    if (cmp != 0) return cmp;
                }
                else
                {
                    int cmp = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                    if (cmp != 0) return cmp;
                    i++;
                    j++;
                }
            }

            return (x.Length - i) - (y.Length - j);
        }
    }
}