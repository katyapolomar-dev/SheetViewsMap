using Autodesk.Revit.DB;

namespace SheetViewsMap
{
    /// <summary>
    /// Данные листа, нужные для наборов печати: комплект и формат.
    ///
    /// Формат читается с основной надписи (категория «Основные надписи»):
    /// параметр «Формат А» задаёт номер формата (1 = А1), «Кратность» —
    /// во сколько раз лист больше по короткой стороне.
    /// </summary>
    public sealed class SheetPrintInfo
    {
        public ElementId SheetId { get; set; }

        /// <summary>Номер листа — для отчёта и сортировки.</summary>
        public string Number { get; set; }

        /// <summary>Комплект: значение параметра «ADSK_Штамп Раздел проекта».</summary>
        public string SetName { get; set; }

        /// <summary>Подпись формата («А1», «А1x3»). null — определить не удалось.</summary>
        public string Format { get; set; }

        /// <summary>Почему формат не определён (для отчёта о пропущенных листах).</summary>
        public string FormatProblem { get; set; }

        /// <summary>На листе больше одной основной надписи — в отчёт попадёт предупреждение.</summary>
        public bool HasMultipleTitleBlocks { get; set; }
    }
}
