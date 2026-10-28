using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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

            // Проход 1: собираем по каждому листу виды, комплект и номера разрезов.
            // Сразу проверить нельзя: листу «(окончание)» нужно знать, сколько
            // разрезов на парном листе «(начало)».
            var infos = new List<SheetInfo>();

            foreach (ViewSheet sheet in sheets)
            {
                var viewIds = new HashSet<ElementId>(sheet.GetAllPlacedViews());

                List<ElementId> schedules;
                if (schedulesBySheet.TryGetValue(sheet.Id, out schedules))
                {
                    foreach (ElementId scheduleId in schedules)
                        viewIds.Add(scheduleId);
                }

                // sheet.Name у листа — это «Название листа» (без номера листа).
                SheetNameParts name = ParseSheetName(sheet.Name);

                infos.Add(new SheetInfo
                {
                    Sheet = sheet,
                    ViewIds = viewIds,
                    SetKey = GetSetKey(sheet),
                    BaseName = name.Base,
                    IsStart = name.IsStart,
                    IsEnd = name.IsEnd,
                    Sections = CollectSectionNumbers(doc, viewIds)
                });
            }

            // Проход 2: проверяем нумерацию разрезов - пары «(начало)/(окончание)»
            // и обычные листы.
            MarkSectionProblems(infos);

            // Проход 3: строим строки таблицы.
            var rowsByView = new Dictionary<ElementId, SheetMapRow>();

            foreach (SheetInfo info in infos)
            {
                foreach (ElementId viewId in info.ViewIds)
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
                        Number = SafeSheetNumber(info.Sheet),
                        Name = info.Sheet.Name,
                        SheetId = info.Sheet.Id,
                        SetKey = info.SetKey,
                        SectionNumbers = info.Sections,
                        IsProblem = info.IsProblem,
                        SectionIssue = info.IsProblem ? info.Issue : null
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
        /// проблемный - помечаем номер знаком ⚠ и переносим причину.
        /// </summary>
        private static SheetRef MergeSheetRefs(IEnumerable<SheetRef> group)
        {
            var list = group.ToList();
            SheetRef first = list[0];

            if (!first.IsProblem && list.Any(s => s.IsProblem))
            {
                first.IsProblem = true;
                if (string.IsNullOrEmpty(first.SectionIssue))
                    first.SectionIssue = list.First(s => s.IsProblem).SectionIssue;
            }

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
        /// Всё, что нужно о листе для построения таблицы и проверки разрезов.
        /// </summary>
        private sealed class SheetInfo
        {
            public ViewSheet Sheet;
            public HashSet<ElementId> ViewIds;
            public string SetKey;

            /// <summary>Название листа без суффикса «(начало)/(окончание)».</summary>
            public string BaseName;
            public bool IsStart;
            public bool IsEnd;

            /// <summary>Отсортированные уникальные номера разрезов на листе.</summary>
            public List<int> Sections;

            public bool IsProblem;
            public string Issue;
        }

        private sealed class SheetNameParts
        {
            public string Base;
            public bool IsStart;
            public bool IsEnd;
        }

        /// <summary>
        /// Парные листы распознаём по суффиксу названия: «Трубопровод … (начало)»
        /// и «Трубопровод … (окончание)». Регистр и лишние пробелы не важны.
        /// </summary>
        private static readonly Regex PairedSheetNameRegex = new Regex(
            @"^\s*(?<base>.+?)\s*\(\s*(?<kind>начало|окончание)\s*\)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static SheetNameParts ParseSheetName(string sheetName)
        {
            var result = new SheetNameParts { Base = sheetName ?? string.Empty };

            if (string.IsNullOrEmpty(sheetName))
                return result;

            Match match = PairedSheetNameRegex.Match(sheetName);
            if (!match.Success)
                return result;

            string baseName = match.Groups["base"].Value.Trim();
            if (baseName.Length == 0)
                return result;

            result.Base = baseName;
            result.IsStart = string.Equals(match.Groups["kind"].Value, "начало", StringComparison.OrdinalIgnoreCase);
            result.IsEnd = string.Equals(match.Groups["kind"].Value, "окончание", StringComparison.OrdinalIgnoreCase);
            return result;
        }

        /// <summary>
        /// Проверяет нумерацию разрезов и заполняет IsProblem/Issue у каждого листа.
        ///
        /// Правила:
        /// - обычный лист и лист «(начало)»: разрезы начинаются с 1 и идут
        ///   по порядку без пропусков;
        /// - лист «(окончание)» продолжает одноимённый лист «(начало)»:
        ///   первый номер должен быть на единицу больше, чем число разрезов
        ///   на «(начало)», и дальше по порядку.
        /// </summary>
        private static void MarkSectionProblems(List<SheetInfo> infos)
        {
            // Группируем парные листы по базовому имени; остальные проверяем сразу.
            var pairs = new Dictionary<string, List<SheetInfo>>(StringComparer.OrdinalIgnoreCase);

            foreach (SheetInfo info in infos)
            {
                if (!info.IsStart && !info.IsEnd)
                {
                    MarkFromOne(info); // обычный лист
                    continue;
                }

                List<SheetInfo> group;
                if (!pairs.TryGetValue(info.BaseName, out group))
                {
                    group = new List<SheetInfo>();
                    pairs.Add(info.BaseName, group);
                }

                group.Add(info);
            }

            foreach (List<SheetInfo> group in pairs.Values)
            {
                List<SheetInfo> starts = group.Where(s => s.IsStart).ToList();
                List<SheetInfo> ends = group.Where(s => s.IsEnd).ToList();

                foreach (SheetInfo start in starts)
                    MarkFromOne(start);

                // Лист «(окончание)» продолжает разрезы листа(ов) «(начало)».
                int startCount = starts.Sum(s => s.Sections.Count);

                foreach (SheetInfo end in ends)
                {
                    if (starts.Count > 0)
                        MarkContinuation(end, startCount);
                    else
                        MarkFromOne(end); // «окончание» без «начала» проверяем как обычный лист
                }
            }
        }

        private static void MarkFromOne(SheetInfo info)
        {
            info.Issue = CheckSectionSequence(info.Sections, 1, false);
            info.IsProblem = info.Issue != null;
        }

        private static void MarkContinuation(SheetInfo info, int startCount)
        {
            info.Issue = CheckSectionSequence(info.Sections, startCount + 1, true);
            info.IsProblem = info.Issue != null;
        }

        /// <summary>
        /// Возвращает описание нарушения или null, если порядок правильный:
        /// номера начинаются с expectedFirst и идут без пропусков.
        /// </summary>
        private static string CheckSectionSequence(List<int> numbers, int expectedFirst, bool continuation)
        {
            if (numbers == null || numbers.Count == 0)
                return null;

            var issues = new List<string>();

            if (numbers[0] != expectedFirst)
            {
                issues.Add(continuation
                    ? "лист должен продолжать «начало»: разрезы должны начинаться с " + expectedFirst
                      + ", а начинаются с " + numbers[0]
                    : "разрезы должны начинаться с " + expectedFirst
                      + ", а начинаются с " + numbers[0]);
            }

            // Пропуски внутри диапазона от фактического первого номера до максимума.
            var present = new HashSet<int>(numbers);
            var missing = new List<int>();

            for (int n = numbers[0] + 1; n < numbers[numbers.Count - 1]; n++)
            {
                if (!present.Contains(n))
                    missing.Add(n);
            }

            if (missing.Count == 1)
                issues.Add("пропущен номер " + missing[0]);
            else if (missing.Count > 1)
                issues.Add("пропущены номера: " + JoinLimited(missing));

            return issues.Count == 0 ? null : string.Join("; ", issues);
        }

        /// <summary>Склеивает числа через запятую, не более шести — дальше «…».</summary>
        private static string JoinLimited(List<int> values)
        {
            const int maxShown = 6;
            string head = string.Join(", ", values.Take(maxShown));
            return values.Count > maxShown ? head + " …" : head;
        }

        /// <summary>
        /// Номера всех разрезов (ViewType.Section), размещённых на листе:
        /// отсортированный список без повторов.
        /// </summary>
        private static List<int> CollectSectionNumbers(Document doc, IEnumerable<ElementId> viewIds)
        {
            var result = new SortedSet<int>();

            foreach (ElementId viewId in viewIds)
            {
                View view = doc.GetElement(viewId) as View;
                if (view == null || view.ViewType != ViewType.Section)
                    continue;

                int number = ParseTrailingNumber(GetViewNumber(view));
                if (number > 0)
                    result.Add(number);
            }

            return result.ToList();
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