using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace PallMall
{
    /// <summary>
    /// Лёгкое чтение модели для наборов печати: только листы, комплект и формат.
    ///
    /// Виды, графики и нумерация разрезов здесь не нужны — поэтому чтение
    /// в разы короче полного чтения для окна «Виды на листах».
    /// </summary>
    public sealed class PrintSetData
    {
        public string DocumentName { get; set; }
        public string Error { get; set; }
        public List<SheetPrintInfo> Sheets { get; set; }

        /// <summary>Сколько заняло чтение — для строки состояния.</summary>
        public long ReadMilliseconds { get; set; }

        public static PrintSetData Failed(string message)
        {
            return new PrintSetData
            {
                Error = message,
                Sheets = new List<SheetPrintInfo>()
            };
        }
    }

    /// <summary>
    /// Читает данные для наборов печати. Вызывается только из обработчика
    /// ExternalEvent, то есть в корректном контексте Revit API.
    /// </summary>
    internal static class PrintSetReader
    {
        internal static PrintSetData Read(UIApplication app)
        {
            var watch = Stopwatch.StartNew();

            UIDocument uidoc = (app == null) ? null : app.ActiveUIDocument;
            if (uidoc == null || uidoc.Document == null)
                return PrintSetData.Failed("Нет открытого документа.");

            Document doc = uidoc.Document;

            if (doc.IsFamilyDocument)
                return PrintSetData.Failed("Открыто семейство (.rfa). Нужен проект (.rvt) с листами.");

            var data = new PrintSetData
            {
                DocumentName = doc.Title,
                Sheets = new List<SheetPrintInfo>()
            };

            Dictionary<ElementId, List<FamilyInstance>> titleBlocksBySheet =
                SheetFormatReader.CollectBySheet(doc);

            foreach (ViewSheet sheet in new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsTemplate))
            {
                string problem;
                bool multipleTitleBlocks;

                List<FamilyInstance> titleBlocks;
                if (!titleBlocksBySheet.TryGetValue(sheet.Id, out titleBlocks))
                    titleBlocks = new List<FamilyInstance>();

                data.Sheets.Add(new SheetPrintInfo
                {
                    SheetId = sheet.Id,
                    Number = SafeSheetNumber(sheet),
                    SetName = SheetMapReader.GetSetKey(sheet),
                    Format = SheetFormatReader.Read(titleBlocks, out problem, out multipleTitleBlocks),
                    FormatProblem = problem,
                    HasMultipleTitleBlocks = multipleTitleBlocks
                });
            }

            data.Sheets = data.Sheets
                .OrderBy(s => s.Number, NaturalStringComparer.Instance)
                .ToList();

            watch.Stop();
            data.ReadMilliseconds = watch.ElapsedMilliseconds;
            return data;
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
}
