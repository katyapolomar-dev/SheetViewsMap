using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace PallMall
{
    /// <summary>
    /// Создаёт наборы печати по запросу из окна плагина:
    /// окно лишь собирает список наборов, а в контексте Revit API
    /// работу выполняет этот обработчик.
    /// </summary>
    public sealed class CreatePrintSetsHandler : IExternalEventHandler
    {
        public void Execute(UIApplication app)
        {
            string error = null;
            PrintSetReport report = null;

            try
            {
                UIDocument uidoc = (app == null) ? null : app.ActiveUIDocument;
                List<PrintSetPlan> plans = Bridge.TakePrintSetPlans();

                if (uidoc == null || uidoc.Document == null)
                    error = "Нет открытого документа.";
                else if (plans.Count == 0)
                    error = "Нечего создавать: список наборов пуст.";
                else
                    report = PrintSetCreator.Create(uidoc.Document, plans);
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            Bridge.PublishPrintSetResult(report, error);
        }

        public string GetName()
        {
            return "PallMall — наборы печати";
        }
    }
}
