using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace PallMall
{
    /// <summary>Что получилось после создания наборов печати.</summary>
    internal sealed class PrintSetReport
    {
        public PrintSetReport()
        {
            Created = new List<string>();
            Updated = new List<string>();
            Errors = new List<string>();
        }

        public List<string> Created { get; private set; }
        public List<string> Updated { get; private set; }
        public List<string> Errors { get; private set; }

        /// <summary>Сколько листов вошло в наборы.</summary>
        public int SheetCount { get; set; }

        public int ChangedCount
        {
            get { return Created.Count + Updated.Count; }
        }
    }

    /// <summary>
    /// Создаёт и обновляет именованные наборы листов (ViewSheetSet).
    ///
    /// Наборы — это те же «Наборы видов/листов», что в диалоге печати:
    /// они же используются при публикации. Поэтому имя набора должно быть
    /// стабильным: повторный запуск обновляет состав, а не плодит копии.
    /// </summary>
    internal static class PrintSetCreator
    {
        private const string TransactionName = "PallMall — наборы печати";

        internal static PrintSetReport Create(Document doc, IList<PrintSetPlan> plans)
        {
            var report = new PrintSetReport();

            if (doc == null)
            {
                report.Errors.Add("Нет открытого документа.");
                return report;
            }

            if (plans == null || plans.Count == 0)
                return report;

            if (doc.IsReadOnly)
            {
                report.Errors.Add("Документ открыт только для чтения — наборы создать нельзя.");
                return report;
            }

            Dictionary<string, ViewSheetSet> existing = CollectExistingSets(doc);

            using (var transaction = new Transaction(doc, TransactionName))
            {
                transaction.Start();

                PrintManager printManager = doc.PrintManager;
                PrintRange previousRange = printManager.PrintRange;

                // Набор сохраняется из «текущего» набора печати, поэтому
                // переключаем диапазон на «Выбранные листы» и возвращаем обратно.
                printManager.PrintRange = PrintRange.Select;
                ViewSheetSetting setting = printManager.ViewSheetSetting;
                IViewSheetSet previousSet = setting.CurrentViewSheetSet;

                foreach (PrintSetPlan plan in plans)
                {
                    try
                    {
                        ViewSet sheets = BuildViewSet(doc, plan);
                        if (sheets.Size == 0)
                        {
                            report.Errors.Add(plan.Name + ": листы не найдены в документе.");
                            continue;
                        }

                        ViewSheetSet target;
                        if (existing.TryGetValue(plan.Name, out target))
                        {
                            setting.CurrentViewSheetSet = target;
                            target.Views = sheets;
                            setting.Save();
                            report.Updated.Add(plan.Name);
                        }
                        else
                        {
                            // Каждый новый набор собираем из «сеансового» набора:
                            // так SaveAs не трогает ранее сохранённые наборы.
                            setting.CurrentViewSheetSet = setting.InSession;
                            setting.CurrentViewSheetSet.Views = sheets;
                            setting.SaveAs(plan.Name);
                            report.Created.Add(plan.Name);
                        }

                        report.SheetCount += sheets.Size;
                    }
                    catch (Exception ex)
                    {
                        report.Errors.Add(plan.Name + ": " + ex.Message);
                    }
                }

                try
                {
                    printManager.PrintRange = previousRange;
                    setting.CurrentViewSheetSet = previousSet;
                }
                catch (Exception)
                {
                    // Не смогли вернуть настройки печати — на результат не влияет.
                }

                transaction.Commit();
            }

            return report;
        }

        private static Dictionary<string, ViewSheetSet> CollectExistingSets(Document doc)
        {
            var existing = new Dictionary<string, ViewSheetSet>(StringComparer.OrdinalIgnoreCase);

            foreach (ViewSheetSet set in new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheetSet))
                .Cast<ViewSheetSet>())
            {
                if (!string.IsNullOrEmpty(set.Name))
                    existing[set.Name] = set;
            }

            return existing;
        }

        private static ViewSet BuildViewSet(Document doc, PrintSetPlan plan)
        {
            var viewSet = new ViewSet();

            foreach (ElementId id in plan.SheetIds)
            {
                ViewSheet sheet = doc.GetElement(id) as ViewSheet;
                if (sheet != null)
                    viewSet.Insert(sheet);
            }

            return viewSet;
        }
    }
}
