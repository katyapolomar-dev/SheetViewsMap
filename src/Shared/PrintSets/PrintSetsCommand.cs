using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SheetViewsMap
{
    /// <summary>
    /// Кнопка «Наборы печати»: открывает окно со списком будущих наборов.
    ///
    /// Модель здесь НЕ читается: окно появляется сразу, а данные приходят
    /// из отдельного лёгкого чтения через ExternalEvent.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PrintSetsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uidoc = data.Application.ActiveUIDocument;
                if (uidoc == null || uidoc.Document == null)
                {
                    TaskDialog.Show(App.RibbonTabName, "Нет открытого документа.");
                    return Result.Cancelled;
                }

                PrintSetsWindow.Show(data.Application.MainWindowHandle, uidoc.Document.Title);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show(App.RibbonTabName, "Не удалось открыть наборы печати:\n\n" + ex.Message);
                return Result.Failed;
            }
        }
    }
}
