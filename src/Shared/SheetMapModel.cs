using System.Collections.Generic;
using System.ComponentModel;
using Autodesk.Revit.DB;

namespace SheetViewsMap
{
    /// <summary>
    /// Ссылка на лист: номер + название. Используется при переходе двойным щелчком.
    /// </summary>
    public sealed class SheetRef
    {
        public string Number { get; set; }
        public string Name { get; set; }
        public ElementId SheetId { get; set; }

        /// <summary>Комплект листов (значение параметра «ADSK_Штамп Раздел проекта»).</summary>
        public string SetKey { get; set; }

        /// <summary>Номера разрезов на листе, отсортированные по возрастанию.</summary>
        public List<int> SectionNumbers { get; set; }

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
    }

    /// <summary>
    /// Одна строка таблицы: вид (номер + название) и все листы, на которых он размещён.
    ///
    /// Реализует INotifyPropertyChanged из-за галочки: при её изменении
    /// интерфейсу нужно обновить счётчик «Выбрано: N из M».
    /// </summary>
    public sealed class SheetMapRow : INotifyPropertyChanged
    {
        /// <summary>Порядковый номер строки в таблице. Заполняется при отрисовке.</summary>
        public string Number { get; set; }

        public string ViewNumber { get; set; }
        public string ViewName { get; set; }
        public string Sheets { get; set; }
        public int SheetCount { get; set; }

        /// <summary>Комплекты листов, на которых размещён этот вид.</summary>
        public string Sets { get; set; }

        /// <summary>Хотя бы один из листов строки имеет некорректную нумерацию разрезов.</summary>
        public bool IsProblem { get; set; }

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

        /// <summary>Идентификатор вида в Revit. Нужен для перехода, в таблице не показывается.</summary>
        public ElementId ViewId { get; set; }

        /// <summary>Варианты листов для перехода. Если лист один — переходим сразу.</summary>
        public List<SheetRef> SheetRefs { get; set; }

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
        public List<SheetMapRow> Rows { get; set; }

        public static SheetMapData Failed(string message)
        {
            return new SheetMapData
            {
                DocumentName = null,
                Error = message,
                SheetCount = 0,
                Rows = new List<SheetMapRow>()
            };
        }
    }
}