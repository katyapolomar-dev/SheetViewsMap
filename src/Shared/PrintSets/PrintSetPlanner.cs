using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace PallMall
{
    /// <summary>Один будущий набор печати: имя и листы.</summary>
    public sealed class PrintSetPlan
    {
        public string Name { get; set; }
        public string SetName { get; set; }
        public string Format { get; set; }
        public List<ElementId> SheetIds { get; set; }

        public int SheetCount
        {
            get { return SheetIds == null ? 0 : SheetIds.Count; }
        }
    }

    /// <summary>Лист, который не попал в наборы, и причина.</summary>
    public sealed class PrintSetSkip
    {
        public string Number { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>Результат планирования: готовые наборы, пропуски и предупреждения.</summary>
    public sealed class PrintSetPlanResult
    {
        public PrintSetPlanResult()
        {
            Plans = new List<PrintSetPlan>();
            Skipped = new List<PrintSetSkip>();
            Warnings = new List<string>();
        }

        public List<PrintSetPlan> Plans { get; private set; }
        public List<PrintSetSkip> Skipped { get; private set; }
        public List<string> Warnings { get; private set; }

        public int SheetCount
        {
            get { return Plans.Sum(p => p.SheetCount); }
        }
    }

    /// <summary>
    /// Группирует листы по паре «комплект + формат» и даёт наборам имена.
    /// Revit API здесь не вызывается — только данные, поэтому логику легко проверять.
    /// </summary>
    internal static class PrintSetPlanner
    {
        /// <summary>Подпись варианта «все комплекты» в фильтре.</summary>
        internal const string AllSets = "Все комплекты";

        /// <summary>Служебное значение комплекта у листов без параметра.</summary>
        internal const string NoSet = "(без комплекта)";

        internal static PrintSetPlanResult Build(IEnumerable<SheetPrintInfo> sheets, string setFilter)
        {
            var result = new PrintSetPlanResult();
            if (sheets == null)
                return result;

            var groups = new Dictionary<string, PrintSetPlan>(StringComparer.OrdinalIgnoreCase);

            foreach (SheetPrintInfo sheet in sheets.OrderBy(s => s.Number, NaturalStringComparer.Instance))
            {
                if (!MatchesFilter(sheet, setFilter))
                    continue;

                if (string.IsNullOrEmpty(sheet.Format))
                {
                    result.Skipped.Add(new PrintSetSkip
                    {
                        Number = sheet.Number,
                        Reason = sheet.FormatProblem ?? "нет формата"
                    });
                    continue;
                }

                if (string.IsNullOrEmpty(sheet.SetName) || sheet.SetName == NoSet)
                {
                    result.Skipped.Add(new PrintSetSkip
                    {
                        Number = sheet.Number,
                        Reason = "нет комплекта"
                    });
                    continue;
                }

                if (sheet.HasMultipleTitleBlocks)
                {
                    result.Warnings.Add(
                        "Лист " + sheet.Number + ": несколько основных надписей — взят формат первой.");
                }

                string name = PrintSetNaming.SetName(sheet.SetName, sheet.Format);

                PrintSetPlan plan;
                if (!groups.TryGetValue(name, out plan))
                {
                    plan = new PrintSetPlan
                    {
                        Name = name,
                        SetName = sheet.SetName,
                        Format = sheet.Format,
                        SheetIds = new List<ElementId>()
                    };
                    groups.Add(name, plan);
                    result.Plans.Add(plan);
                }

                plan.SheetIds.Add(sheet.SheetId);
            }

            result.Plans.Sort(ComparePlans);
            return result;
        }

        private static bool MatchesFilter(SheetPrintInfo sheet, string setFilter)
        {
            if (string.IsNullOrEmpty(setFilter) || setFilter == AllSets)
                return true;

            return string.Equals(sheet.SetName, setFilter, StringComparison.OrdinalIgnoreCase);
        }

        private static int ComparePlans(PrintSetPlan left, PrintSetPlan right)
        {
            int bySet = string.Compare(left.SetName, right.SetName, StringComparison.OrdinalIgnoreCase);
            if (bySet != 0)
                return bySet;

            return string.Compare(left.Format, right.Format, StringComparison.OrdinalIgnoreCase);
        }
    }
}
