using System;
using Autodesk.Revit.UI;

namespace PallMall
{
    /// <summary>
    /// Читает данные для окна наборов печати (листы, комплект, формат).
    /// Отдельное событие от полного чтения: окно открывается сразу,
    /// а данные приходят, когда Revit освободится.
    /// </summary>
    public sealed class PrintSetsDataHandler : IExternalEventHandler
    {
        public void Execute(UIApplication app)
        {
            PrintSetData data;

            try
            {
                data = PrintSetReader.Read(app);
            }
            catch (Exception ex)
            {
                data = PrintSetData.Failed(ex.Message);
            }

            Bridge.PublishPrintSetData(data);
        }

        public string GetName()
        {
            return "PallMall — чтение листов для наборов печати";
        }
    }
}
