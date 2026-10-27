using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SheetViewsMap
{
    /// <summary>
    /// Перечитывает листы и раскладку видов. Запускается Revit'ом по кнопке «Обновить».
    /// </summary>
    public sealed class RefreshHandler : IExternalEventHandler
    {
        public void Execute(UIApplication app)
        {
            SheetMapData data;

            try
            {
                data = SheetMapReader.Read(app);
            }
            catch (Exception ex)
            {
                // Чтение не должно ронять Revit: показываем текст ошибки в панели.
                data = SheetMapData.Failed(ex.Message);
            }

            Bridge.PublishData(data);
        }

        public string GetName()
        {
            return "SheetViewsMap — чтение листов и видов";
        }
    }

    /// <summary>
    /// Открывает один лист из очереди: запуск происходит по кнопке «Открыть лист»
    /// и по её нажатию дальше — панель вызывает Raise() для следующего.
    /// RequestViewChange() выбран вместо присваивания ActiveView, потому что
    /// он как раз разрешён для вызова из ExternalEvent.
    /// </summary>
    public sealed class NavigateHandler : IExternalEventHandler
    {
        public void Execute(UIApplication app)
        {
            string error = null;

            try
            {
                ElementId sheetId = Bridge.TakeNextSheetId();
                UIDocument uidoc = (app == null) ? null : app.ActiveUIDocument;

                if (uidoc == null || uidoc.Document == null)
                {
                    error = "Нет открытого документа.";
                }
                else if (sheetId == null || sheetId == ElementId.InvalidElementId)
                {
                    error = "Лист не выбран.";
                }
                else if (uidoc.Document.IsReadOnly)
                {
                    error = "Документ открыт только для чтения — сменить вид нельзя.";
                }
                else
                {
                    ViewSheet sheet = uidoc.Document.GetElement(sheetId) as ViewSheet;

                    if (sheet == null)
                        error = "Лист не найден в текущем документе (возможно, открыт другой проект).";
                    else
                        uidoc.RequestViewChange(sheet);
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            // Ошибка обрывает всю очередь: продолжать открывать остальное бессмысленно.
            if (error != null)
                Bridge.ClearPendingSheets();

            Bridge.PublishNavigateResult(error);
        }

        public string GetName()
        {
            return "SheetViewsMap — открытие листа";
        }
    }
}