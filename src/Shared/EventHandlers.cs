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
    /// Открывает один элемент из очереди: лист или отдельный вид.
    /// Запуск происходит по кнопке «Открыть лист», двойному щелчку по строке
    /// и выбору вида в выпадающем списке колонки «Листы».
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
                ElementId targetId = Bridge.TakeNextSheetId();
                UIDocument uidoc = (app == null) ? null : app.ActiveUIDocument;

                if (uidoc == null || uidoc.Document == null)
                {
                    error = "Нет открытого документа.";
                }
                else if (targetId == null || targetId == ElementId.InvalidElementId)
                {
                    error = "Ничего не выбрано.";
                }
                else if (uidoc.Document.IsReadOnly)
                {
                    error = "Документ открыт только для чтения — сменить вид нельзя.";
                }
                else
                {
                    Element element = uidoc.Document.GetElement(targetId);
                    ViewSheet sheet = element as ViewSheet;
                    View view = element as View;

                    if (sheet != null)
                        uidoc.RequestViewChange(sheet);
                    else if (view != null)
                        uidoc.RequestViewChange(view);
                    else
                        error = "Элемент не найден в текущем документе (возможно, открыт другой проект).";
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
            return "SheetViewsMap — открытие листа или вида";
        }
    }
}