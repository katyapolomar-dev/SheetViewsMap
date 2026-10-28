using System.Collections.Generic;
using System.ComponentModel;
using Autodesk.Revit.DB;

namespace SheetViewsMap
{
    /// <summary>
    /// Одна строка таблицы — проблемный ЛИСТ.
    ///
    /// Номера видов, размещённых на листе, видны в выпадающем списке
    /// (стрелка рядом с номером листа); клик по номеру вида открывает
    /// этот вид в Revit.
    /// </summary>
    public sealed class SheetMapRow : INotifyPropertyChanged
    {
        /// <summary>Номер листа (колонка «Листы»).</summary>
        public string Number { get; set; }

        /// <summary>Название листа. Столбца нет, участвует в поиске.</summary>
        public string SheetName { get; set; }

        /// <summary>Идентификатор листа в Revit — для перехода и открытия.</summary>
        public ElementId SheetId { get; set; }

        /// <summary>Комплект листов (значение параметра «ADSK_Штамп Раздел проекта»).</summary>
        public string Sets { get; set; }

        /// <summary>Номера видов, размещённых на листе (для выпадающего списка).</summary>
        public List<string> ViewNumbers { get; set; }

        /// <summary>Идентификаторы видов — параллельно ViewNumbers.</summary>
        public List<ElementId> ViewIds { get; set; }

        /// <summary>Номера видов одной строкой: для колонки «№ вида» и поиска.</summary>
        public string ViewNumbersText { get; set; }

        /// <summary>
        /// Причина, по которой лист помечен проблемным («разрезы должны
        /// начинаться с 1…», «пропущен номер 3» и т.п.). null, если проблем нет.
        /// </summary>
        public string SectionIssue { get; set; }

        /// <summary>
        /// Нумерация разрезов на листе нарушена: не начинается с 1, есть пропуски
        /// или лист «(окончание)» не продолжает лист «(начало)».
        /// </summary>
        public bool IsProblem { get; set; }

        /// <summary>На листе больше одного вида — у номера листа есть стрелка списка.</summary>
        public bool HasMultipleViews
        {
            get { return ViewNumbers != null && ViewNumbers.Count > 1; }
        }

        /// <summary>Отмечена ли строка галочкой.</summary>
        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged("IsSelected");
            }
        }

        private bool _isSelected;

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Результат одного чтения модели. Формируется в контексте Revit API обработчиком RefreshHandler.
    /// </summary>
    public sealed class SheetMapData
    {
        public string DocumentName { get; set; }
        public string Error { get; set; }
        public int SheetCount { get; set; }

        /// <summary>Сколько всего видов размещено на листах (для строки состояния).</summary>
        public int PlacedViewCount { get; set; }

        public List<SheetMapRow> Rows { get; set; }

        public static SheetMapData Failed(string message)
        {
            return new SheetMapData
            {
                DocumentName = null,
                Error = message,
                SheetCount = 0,
                PlacedViewCount = 0,
                Rows = new List<SheetMapRow>()
            };
        }
    }
}
