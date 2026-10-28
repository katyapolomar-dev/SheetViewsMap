using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Autodesk.Revit.DB;

// Имена типов задаём явно через using-псевдонимы. Внутри класса, наследующего
// UserControl, простые имена Visibility и GridLinesVisibility перекрываются
// унаследованными свойствами FrameworkElement/Control, из-за чего
// «Visibility.Collapsed» не компилируется. А Grid и Binding дополнительно
// пересекаются с одноимёнными типами из Autodesk.Revit.DB.
using WpfBinding = System.Windows.Data.Binding;
using WpfGrid = System.Windows.Controls.Grid;
using WpfDataGridGridLinesVisibility = System.Windows.Controls.DataGridGridLinesVisibility;
using WpfVisibility = System.Windows.Visibility;

namespace SheetViewsMap
{
    /// <summary>
    /// Содержимое окна плагина: поиск, таблица видов с галочками и нижняя
    /// панель действий.
    ///
    /// Интерфейс собирается кодом, а не в XAML-редакторе, — чтобы проект
    /// собирался без Visual Studio (dotnet build достаточно). Всё, что тут
    /// происходит, — обычный WPF: Revit API панель не трогает, только
    /// общается с Bridge.
    /// </summary>
    public sealed class SheetMapPane : UserControl
    {
        private readonly DataGrid _grid;
        private readonly TextBox _filterBox;
        private readonly TextBox _numberBox;
        private readonly ComboBox _setBox;
        private readonly Button _problemButton;
        private readonly Button _goodButton;
        private readonly TextBlock _status;
        private readonly TextBlock _selectionLabel;
        private readonly Button _copyButton;
        private readonly Button _refreshButton;

        private readonly ListBox _pickerList;
        private readonly TextBlock _pickerTitle;
        private readonly Border _pickerPanel;

        private readonly DispatcherTimer _timer;

        private readonly List<SheetMapRow> _allRows = new List<SheetMapRow>();
        private readonly List<SheetMapRow> _visibleRows = new List<SheetMapRow>();
        private List<SheetRef> _pickerRefs = new List<SheetRef>();

        private bool _initialLoadRequested;

        private const string AllSets = "Все комплекты";

        /// <summary>Ширина слота под номер листа - от неё зависит выравнивание значков.</summary>
        private const double SheetNumberSlotWidth = 78;
        private bool _problemMode;
        private bool _goodMode;

        /// <summary>Пауза между листами при пролистывании, миллисекунды.</summary>
        private const int SheetStepDelayMs = 900;

        private DateTime _nextSheetAllowedAt = DateTime.MinValue;

        public SheetMapPane()
        {
            Background = Theme.Background;

            // ---------- шапка: фильтр по номеру вида и общий поиск ----------
            var header = new StackPanel { Margin = new Thickness(14, 12, 14, 0) };

            header.Children.Add(Theme.CreateText(
                "Все номера видов и листы, на которых они размещены.",
                Theme.TextMuted, Theme.FontSmall, false));

            var filterRow = new WpfGrid();
            filterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            filterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            filterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            filterRow.Margin = new Thickness(0, 8, 0, 0);

            // ---- выбор комплекта листов ----
            var setStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            setStack.Children.Add(Theme.CreateText("Комплект:", Theme.TextStrong, Theme.FontUi, true));

            _setBox = Theme.CreateComboBox();
            _setBox.Width = 220;
            _setBox.Margin = new Thickness(8, 0, 0, 0);
            _setBox.ToolTip = "Комплект листов (параметр «ADSK_Штамп Раздел проекта»)";
            _setBox.SelectionChanged += (s, e) => ApplyFilter();
            setStack.Children.Add(_setBox);
            WpfGrid.SetColumn(setStack, 0);
            filterRow.Children.Add(setStack);

            // ---- «№ вида»: отдельный фильтр только по номеру вида ----
            var numberHost = new WpfGrid();
            numberHost.Margin = new Thickness(0, 0, 8, 0);
            numberHost.Width = 215;

            _numberBox = Theme.CreateSearchBox();
            _numberBox.ToolTip =
                "Фильтр по номеру вида. Сначала ищет точное совпадение (например A-101); если таких нет - все номера, начинающиеся с введённого.";
            numberHost.Children.Add(_numberBox);

            var numberHint = Theme.CreateText("№ вида, напр. A-101", Theme.TextMuted, Theme.FontUi, false);
            numberHint.Margin = new Thickness(11, 0, 0, 0);
            numberHint.VerticalAlignment = VerticalAlignment.Center;
            numberHint.IsHitTestVisible = false;
            numberHost.Children.Add(numberHint);

            var numberStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            numberStack.Children.Add(Theme.CreateText("№ вида:", Theme.TextStrong, Theme.FontUi, true));
            numberStack.Children.Add(numberHost);
            WpfGrid.SetColumn(numberStack, 1);
            filterRow.Children.Add(numberStack);

            _numberBox.TextChanged += (s, e) =>
            {
                ApplyFilter();
                numberHint.Visibility = string.IsNullOrEmpty(_numberBox.Text)
                    ? WpfVisibility.Visible
                    : WpfVisibility.Collapsed;
            };

            // ---- общий поиск: название вида, номер вида, номер листа ----
            _filterBox = Theme.CreateSearchBox();
            _filterBox.ToolTip = "Подстрока в номере вида, названии вида или в списке листов";

            var searchHost = new WpfGrid();
            searchHost.Children.Add(_filterBox);

            var searchHint = Theme.CreateText("Поиск: название вида или номер листа", Theme.TextMuted, Theme.FontUi, false);
            searchHint.Margin = new Thickness(11, 0, 0, 0);
            searchHint.VerticalAlignment = VerticalAlignment.Center;
            searchHint.IsHitTestVisible = false;
            searchHost.Children.Add(searchHint);

            _filterBox.TextChanged += (s, e) =>
            {
                ApplyFilter();
                searchHint.Visibility = string.IsNullOrEmpty(_filterBox.Text)
                    ? WpfVisibility.Visible
                    : WpfVisibility.Collapsed;
            };

            WpfGrid.SetColumn(searchHost, 2);
            filterRow.Children.Add(searchHost);

            header.Children.Add(filterRow);

            // ---- две кнопки: проблемные листы / гудные ----
            var checkRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 0)
            };
            checkRow.Children.Add(Theme.CreateText("Показывать:", Theme.TextStrong, Theme.FontUi, true));

            _problemButton = Theme.CreateButton("Проблемные", false);
            _problemButton.Width = 118;
            _problemButton.Margin = new Thickness(10, 0, 8, 0);
            _problemButton.ToolTip = "Только листы, где нумерация разрезов начинается не с 1";
            _problemButton.Click += (s, e) => ToggleProblemFilter(true);

            _goodButton = Theme.CreateButton("Гудные", false);
            _goodButton.Width = 100;
            _goodButton.Margin = new Thickness(0, 0, 8, 0);
            _goodButton.ToolTip = "Только листы с корректной нумерацией разрезов";
            _goodButton.Click += (s, e) => ToggleProblemFilter(false);

            checkRow.Children.Add(_problemButton);
            checkRow.Children.Add(_goodButton);
            header.Children.Add(checkRow);

            // ---------- таблица ----------
            _grid = new DataGrid
            {
                AutoGenerateColumns = false,

                // false - чтобы работали галочки; текстовые колонки
                // отдельно помечены как нередактируемые.
                IsReadOnly = false,

                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserResizeRows = false,
                CanUserReorderColumns = false,
                CanUserSortColumns = false,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = WpfDataGridGridLinesVisibility.None,
                RowHeaderWidth = 0,
                Background = Theme.Background,
                RowBackground = Theme.RowBackground,
                AlternatingRowBackground = Theme.RowBackgroundAlt,
                BorderThickness = new Thickness(0),
                ColumnHeaderStyle = Theme.CreateHeaderStyle(),
                CellStyle = Theme.CreateCellStyle(),
                EnableRowVirtualization = false,
                Margin = new Thickness(14, 10, 14, 0)
            };

            _grid.Columns.Add(TextColumn("№", "Number", new DataGridLength(44), 44, true));
            _grid.Columns.Add(CheckColumn(40, 40));
            _grid.Columns.Add(TextColumn("№ вида", "ViewNumber", new DataGridLength(110), 90, true));
            _grid.Columns.Add(TextColumn("Название вида", "ViewName", new DataGridLength(2, DataGridLengthUnitType.Star), 190, false));
            _grid.Columns.Add(SheetsColumn(300));
            _grid.MouseDoubleClick += OnGridMouseDoubleClick;

            // ---------- выбор листа (если вид размещён на нескольких листах) ----------
            _pickerTitle = new TextBlock
            {
                Foreground = Theme.TextStrong,
                FontSize = Theme.FontUi,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(12, 12, 12, 8),
                TextWrapping = TextWrapping.Wrap
            };

            _pickerList = new ListBox
            {
                Style = Theme.CreateListBoxStyle(),
                Height = 190,
                Margin = new Thickness(12, 0, 12, 10),
                FontSize = Theme.FontUi,
                ItemTemplate = SheetItemTemplate()
            };
            _pickerList.MouseDoubleClick += (s, e) => OpenSelectedSheet();

            var pickerOpen = Theme.CreateButton("Открыть лист", true);
            pickerOpen.Width = 124;
            pickerOpen.Margin = new Thickness(12, 0, 8, 12);
            pickerOpen.Click += (s, e) => OpenSelectedSheet();

            var pickerBack = Theme.CreateButton("Назад", false);
            pickerBack.Width = 100;
            pickerBack.Margin = new Thickness(0, 0, 0, 12);
            pickerBack.Click += (s, e) => HidePicker();

            var pickerButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            pickerButtons.Children.Add(pickerOpen);
            pickerButtons.Children.Add(pickerBack);

            var pickerStack = new StackPanel();
            pickerStack.Children.Add(_pickerTitle);
            pickerStack.Children.Add(_pickerList);
            pickerStack.Children.Add(pickerButtons);

            _pickerPanel = new Border
            {
                Visibility = WpfVisibility.Collapsed,
                Margin = new Thickness(14, 10, 14, 0),
                Background = Theme.Background,
                Child = pickerStack
            };

            var contentGrid = new WpfGrid();
            contentGrid.Children.Add(_grid);
            contentGrid.Children.Add(_pickerPanel);

            // ---------- нижняя панель: счётчик и кнопки ----------
            _selectionLabel = Theme.CreateText("Выбрано: 0", Theme.TextStrong, Theme.FontUi, true);
            _selectionLabel.Margin = new Thickness(0, 0, 12, 0);

            var selectAll = Theme.CreateButton("Выбрать все", false);
            selectAll.Width = 118;
            selectAll.Margin = new Thickness(0, 0, 8, 0);
            selectAll.Click += (s, e) => SelectAll();

            // Кнопка обязательна: данные читаются из модели только по запросу,
            // поэтому после смены документа их нужно перечитать вручную.
            var refreshButton = Theme.CreateButton("Обновить", false);
            refreshButton.Width = 104;
            refreshButton.Margin = new Thickness(0, 0, 8, 0);
            refreshButton.ToolTip = "Перечитать листы и виды из модели";
            refreshButton.Click += (s, e) => RequestRefresh();
            _refreshButton = refreshButton;

            _copyButton = Theme.CreateButton("Копировать", false);
            _copyButton.Width = 112;
            _copyButton.Margin = new Thickness(0, 0, 8, 0);
            _copyButton.Click += (s, e) => CopySelection();

            var openSheetButton = Theme.CreateButton("Открыть лист", true);
            openSheetButton.Width = 130;
            openSheetButton.Click += (s, e) => OpenSheetFromToolbar();

            var status = new TextBlock
            {
                Foreground = Theme.TextMuted,
                FontSize = Theme.FontSmall,
                Margin = new Thickness(14, 9, 14, 0),
                TextWrapping = TextWrapping.Wrap
            };
            _status = status;

            var bottom = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };

            var bottomLeft = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            bottomLeft.Children.Add(_selectionLabel);
            bottomLeft.Children.Add(selectAll);
            bottomLeft.Children.Add(_refreshButton);

            var bottomRight = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            bottomRight.Children.Add(_copyButton);
            bottomRight.Children.Add(openSheetButton);

            var buttonRow = new WpfGrid();
            buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            WpfGrid.SetColumn(bottomLeft, 0);
            WpfGrid.SetColumn(bottomRight, 1);
            buttonRow.Children.Add(bottomLeft);
            buttonRow.Children.Add(bottomRight);

            bottom.Children.Add(buttonRow);
            bottom.Children.Add(_status);

            // ---------- сборка макета ----------
            var root = new DockPanel { LastChildFill = true, Background = Theme.Background };
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(bottom);
            root.Children.Add(contentGrid);

            Content = root;

            SetStatus("Нажмите «Обновить», чтобы прочитать листы из модели.");
            UpdateSelectionCounter();

            // Revit выполняет ExternalEvent только когда он простаивает.
            // Поэтому результат забираем по таймеру, а не напрямую из обработчика.
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }

        /// <summary>
        /// Включает опрос результатов от Revit. Вызывается, когда окно показано.
        /// </summary>
        public void StartWatching()
        {
            if (!_timer.IsEnabled)
                _timer.Start();
        }

        /// <summary>
        /// Останавливает опрос, пока окно скрыто, чтобы не тратить ресурсы.
        /// </summary>
        public void StopWatching()
        {
            _timer.Stop();
        }

        // ------------------------------------------------------------------ таймер

        private void OnTimerTick(object sender, EventArgs e)
        {
            string navigateError;
            if (Bridge.TryTakeNavigateError(out navigateError))
            {
                if (navigateError != null)
                {
                    SetStatus("Не удалось открыть лист: " + navigateError, true);
                }
                else
                {
                    int remaining, total;
                    Bridge.GetQueueState(out remaining, out total);

                    SetStatus(remaining > 0
                        ? string.Format("Открываю листы: {0} из {1}", total - remaining, total)
                        : string.Format("Открыто листов: {0}", total));
                }
            }

            // Продолжаем очередь открытия. Между листами делаем паузу, иначе
            // Revit успевает перерисовать вид и человек ничего не увидит.
            if (Bridge.HasPendingSheets && DateTime.Now >= _nextSheetAllowedAt)
            {
                if (Bridge.RequestOpenNext())
                    _nextSheetAllowedAt = DateTime.Now.AddMilliseconds(SheetStepDelayMs);
            }

            SheetMapData data;
            if (Bridge.TryTakeData(out data))
                Render(data);

            // Первое автообновление, как только Revit освободится.
            if (!_initialLoadRequested && Bridge.IsReady)
            {
                _initialLoadRequested = true;
                RequestRefresh();
            }
        }

        // ------------------------------------------------------------------ данные

        private void RequestRefresh()
        {
            if (!Bridge.IsReady)
            {
                SetStatus("Плагин ещё загружается — попробуйте через секунду.", true);
                return;
            }

            if (!Bridge.RequestRefresh())
            {
                SetStatus("Предыдущий запрос ещё выполняется, подождите…");
                return;
            }

            SetStatus("Читаю листы и виды из модели…");
        }

        private void Render(SheetMapData data)
        {
            UnsubscribeRows();

            _allRows.Clear();
            HidePicker();

            if (!string.IsNullOrEmpty(data.Error))
            {
                _grid.ItemsSource = null;
                _visibleRows.Clear();
                UpdateSelectionCounter();
                SetStatus(data.Error, true);
                return;
            }

            foreach (SheetMapRow row in data.Rows)
            {
                row.PropertyChanged += OnRowPropertyChanged;
                _allRows.Add(row);
            }

            FillSetList();

            ApplyFilter();

            int problemRows = data.Rows.Count(r => r.IsProblem);

            string setsFound = string.Join(" · ", _setBox.Items.Cast<string>().Skip(1));
            string hint = (setsFound == "(без комплекта)")
                ? "  |  Комплекты не найдены: на листах пусто или нет параметра «" + SheetMapReader.SetParameterName + "»"
                : string.Empty;

            SetStatus(string.Format("{0} — листов: {1}, видов на листах: {2}, с проблемной нумерацией: {3}{4}",
                data.DocumentName, data.SheetCount, data.Rows.Count, problemRows, hint));
        }

        /// <summary>
        /// Наполняет выпадающий список комплектами, которые реально встречаются
        /// в документе.
        /// </summary>
        private void FillSetList()
        {
            var sets = new List<string> { AllSets };
            sets.AddRange(_allRows
                .SelectMany(r => (r.Sets ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase));

            string previous = _setBox.SelectedItem as string;

            _setBox.ItemsSource = sets;
            _setBox.SelectedItem = previous != null && sets.Contains(previous) ? previous : AllSets;
        }

        private void OnRowPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsSelected")
                UpdateSelectionCounter();
        }

        private void UnsubscribeRows()
        {
            foreach (SheetMapRow row in _allRows)
                row.PropertyChanged -= OnRowPropertyChanged;

            _visibleRows.Clear();
        }

        private void ApplyFilter()
        {
            string search = (_filterBox.Text ?? string.Empty).Trim();
            string number = (_numberBox.Text ?? string.Empty).Trim();
            string set = _setBox.SelectedItem as string;

            IEnumerable<SheetMapRow> rows = _allRows;

            // ---- комплект листов ----
            if (!string.IsNullOrEmpty(set) && set != AllSets)
            {
                rows = rows.Where(r => (r.Sets ?? string.Empty)
                    .IndexOf(set, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            // ---- проблемные / гудные ----
            if (_problemMode)
                rows = rows.Where(r => r.IsProblem);
            else if (_goodMode)
                rows = rows.Where(r => !r.IsProblem);

            // Сначала точный фильтр по номеру вида, при отсутствии совпадений -
            // все номера, начинающиеся с введённого.
            if (number.Length > 0)
            {
                var exact = rows.Where(r => string.Equals((r.ViewNumber ?? string.Empty).Trim(), number, StringComparison.OrdinalIgnoreCase)).ToList();
                rows = exact.Count > 0
                    ? exact
                    : rows.Where(r => (r.ViewNumber ?? string.Empty).Trim().StartsWith(number, StringComparison.OrdinalIgnoreCase));
            }

            if (search.Length > 0)
                rows = rows.Where(r => Matches(r, search));

            _visibleRows.Clear();
            _visibleRows.AddRange(rows);

            // Нумерация в колонке «№» считается по видимым строкам.
            for (int i = 0; i < _visibleRows.Count; i++)
                _visibleRows[i].Number = (i + 1).ToString();

            // Отдаём таблице НОВЫЙ список: если переиспользовать тот же
            // экземпляр, DataGrid не заметит изменения и не перерисуется.
            _grid.ItemsSource = new List<SheetMapRow>(_visibleRows);
            UpdateSelectionCounter();
        }

        private void ToggleProblemFilter(bool problem)
        {
            if (problem)
            {
                // Повторное нажатие снимает фильтр - так можно вернуться к общему списку.
                _problemMode = !_problemMode;
                _goodMode = false;
            }
            else
            {
                _goodMode = !_goodMode;
                _problemMode = false;
            }

            Theme.SetButtonActive(_problemButton, _problemMode);
            Theme.SetButtonActive(_goodButton, _goodMode);
            ApplyFilter();
        }

        private static bool Matches(SheetMapRow row, string query)
        {
            return Contains(row.ViewNumber, query)
                || Contains(row.ViewName, query)
                || Contains(row.Sheets, query);
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value)
                && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ------------------------------------------------------------------ галочки

        private void SelectAll()
        {
            bool allSelected = _visibleRows.Count > 0 && _visibleRows.All(r => r.IsSelected);

            foreach (SheetMapRow row in _visibleRows)
                row.IsSelected = !allSelected;

            UpdateSelectionCounter();
        }

        private void UpdateSelectionCounter()
        {
            int selected = _visibleRows.Count(r => r.IsSelected);
            _selectionLabel.Text = string.Format("Выбрано: {0} из {1}", selected, _visibleRows.Count);

            _copyButton.ToolTip = selected > 0
                ? string.Format("Скопировать {0} отмеченных строк в буфер обмена (вставится в Excel)", selected)
                : "Галочки не стоят — скопируются все видимые строки";
        }

        /// <summary>
        /// Копирование в буфер обмена в виде TSV: в Excel строки разделятся
        /// по столбцам сами.
        /// </summary>
        private void CopySelection()
        {
            List<SheetMapRow> rows = _visibleRows.Where(r => r.IsSelected).ToList();
            if (rows.Count == 0)
                rows = _visibleRows.ToList();

            if (rows.Count == 0)
                return;

            var text = new StringBuilder();
            text.AppendLine("№\t№ вида\tНазвание вида\tЛисты");

            foreach (SheetMapRow row in rows)
            {
                text.AppendLine(string.Join("\t", new[]
                {
                    row.Number,
                    row.ViewNumber,
                    row.ViewName,
                    row.Sheets
                }));
            }

            try
            {
                Clipboard.SetText(text.ToString());
                SetStatus(string.Format("Скопировано строк: {0}. Вставьте в Excel через Ctrl+V.", rows.Count));
            }
            catch (Exception ex)
            {
                SetStatus("Не удалось скопировать: " + ex.Message, true);
            }
        }

        // ------------------------------------------------------------------ переход на лист

        private void OnGridMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            SheetMapRow row = _grid.SelectedItem as SheetMapRow;
            if (row == null || row.SheetRefs == null || row.SheetRefs.Count == 0)
                return;

            if (row.SheetRefs.Count == 1)
            {
                Navigate(row.SheetRefs[0]);
                return;
            }

            ShowPicker(row);
        }

        private void OpenSheetFromToolbar()
        {
            // Собираем листы всех отмеченных строк. Revit показывает только один
            // вид за раз, поэтому листы открываются по очереди - смотрите Bridge.
            // Элемент ElementId сам по себе годен как ключ: в 2021 это класс,
            // а в 2022+ структура, но Equals/GetHashCode работают в обоих случаях.
            var targets = new List<SheetRef>();
            var used = new HashSet<ElementId>();

            foreach (SheetMapRow row in _visibleRows.Where(r => r.IsSelected))
                AddSheets(row, targets, used);

            // Если галочки не стоят - берём строку, на которой стоит курсор.
            if (targets.Count == 0)
            {
                SheetMapRow current = _grid.SelectedItem as SheetMapRow;
                if (current != null)
                    AddSheets(current, targets, used);
            }

            if (targets.Count == 0)
            {
                SetStatus("Сначала отметьте строку галочкой или щёлкните по ней.", true);
                return;
            }

            OpenSheets(targets);
        }

        private static void AddSheets(SheetMapRow row, List<SheetRef> targets, HashSet<ElementId> used)
        {
            if (row.SheetRefs == null)
                return;

            foreach (SheetRef reference in row.SheetRefs)
            {
                if (reference == null || reference.SheetId == null)
                    continue;

                // Один и тот же лист может попасть в выборку из разных видов.
                if (used.Add(reference.SheetId))
                    targets.Add(reference);
            }
        }

        private void OpenSheets(List<SheetRef> targets)
        {
            var ids = targets.Select(t => t.SheetId);

            if (!Bridge.RequestOpenSheets(ids))
            {
                SetStatus("Revit сейчас занят — попробуйте ещё раз.", true);
                return;
            }

            _nextSheetAllowedAt = DateTime.MinValue;

            if (targets.Count == 1)
                SetStatus("Открываю лист " + targets[0].Number + "…");
            else
                SetStatus("Открываю листы: " + targets.Count);
        }

        private void ShowPicker(SheetMapRow row)
        {
            _pickerRefs = row.SheetRefs;
            _pickerTitle.Text = string.Format(
                "Вид {0} — {1}{2}Размещён на {3} листах. Выберите нужный:",
                row.ViewNumber, row.ViewName, Environment.NewLine, row.SheetRefs.Count);

            _pickerList.ItemsSource = _pickerRefs;
            _pickerList.SelectedIndex = 0;

            _pickerPanel.Visibility = WpfVisibility.Visible;
            _grid.Visibility = WpfVisibility.Collapsed;
        }

        private void HidePicker()
        {
            _pickerPanel.Visibility = WpfVisibility.Collapsed;
            _grid.Visibility = WpfVisibility.Visible;
        }

        private void OpenSelectedSheet()
        {
            int index = _pickerList.SelectedIndex;
            if (index < 0 || index >= _pickerRefs.Count)
                return;

            SheetRef target = _pickerRefs[index];
            HidePicker();
            Navigate(target);
        }

        private void Navigate(SheetRef target)
        {
            OpenSheets(new List<SheetRef> { target });
        }

        // ------------------------------------------------------------------ мелочи

        private void SetStatus(string text)
        {
            SetStatus(text, false);
        }

        private void SetStatus(string text, bool isError)
        {
            _status.Text = text;
            _status.Foreground = isError ? Theme.AccentPressed : Theme.TextMuted;
        }

        /// <summary>
        /// Колонка таблицы.
        /// width - ширина: обычная число либо DataGridLength со Star,
        /// который делит свободное место между колонками;
        /// minWidth - минимальная ширина: без неё колонка может схлопнуться
        /// до нескольких пикселей, если окно узкое.
        /// centered - выравнивать текст по центру ячейки.
        /// </summary>
        private static DataGridTextColumn TextColumn(string header, string path, DataGridLength width, double minWidth, bool centered)
        {
            var column = new DataGridTextColumn
            {
                Header = header,
                Binding = new WpfBinding(path),
                ElementStyle = Theme.CreateCellTextStyle(centered),
                IsReadOnly = true,
                MinWidth = minWidth,
                Width = width
            };

            // Заголовок центрируем так же, как и содержимое ячеек.
            if (centered)
            {
                Style headerStyle = Theme.CreateHeaderStyle();
                headerStyle.Setters.Add(new Setter(System.Windows.Controls.Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
                column.HeaderStyle = headerStyle;
            }

            return column;
        }

        /// <summary>
        /// Колонка «Листы».
        ///
        /// Номера листов выводятся отдельными элементами, а знак ⚠ занимает
        /// место фиксированной ширины. Иначе знак прилипал бы к номеру и
        /// «прыгал» по строке: у листа А-101 он окажется левее, чем у А-100.
        /// </summary>
        private static DataGridTemplateColumn SheetsColumn(double minWidth)
        {
            var column = new DataGridTemplateColumn
            {
                Header = "Листы",
                CellTemplate = SheetsCellTemplate(),
                MinWidth = minWidth,
                Width = new DataGridLength(3, DataGridLengthUnitType.Star)
            };

            Style headerStyle = Theme.CreateHeaderStyle();
            headerStyle.Setters.Add(new Setter(System.Windows.Controls.Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            column.HeaderStyle = headerStyle;

            return column;
        }

        private static DataTemplate SheetsCellTemplate()
        {
            // Один номер листа: слева место под знак, справа сам номер.
            var item = new FrameworkElementFactory(typeof(StackPanel));
            item.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            item.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 14, 0));
            item.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            // Причина проблемы показывается в подсказке при наведении на номер
            // листа. SectionIssue равен null у правильных листов - подсказки нет.
            item.SetValue(FrameworkElement.ToolTipProperty, new WpfBinding("SectionIssue"));

            var mark = new FrameworkElementFactory(typeof(TextBlock));
            mark.SetValue(FrameworkElement.WidthProperty, 14.0);
            mark.SetValue(TextBlock.TextProperty, "\u26A0");
            mark.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            mark.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 4, 0));

            // Знак показываем только у проблемных листов, но место под него
            // зарезервировано всегда - поэтому все значки стоят по одной линии.
            var markStyle = new Style(typeof(TextBlock));
            markStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, Theme.AccentPressed));
            markStyle.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.Bold));
            markStyle.Setters.Add(new Setter(UIElement.VisibilityProperty, WpfVisibility.Collapsed));
            markStyle.Triggers.Add(ProblemOnCheckedTrigger());

            var problemTrigger = new DataTrigger
            {
                Binding = new WpfBinding("IsProblem"),
                Value = true
            };
            problemTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, WpfVisibility.Visible));
            markStyle.Triggers.Add(problemTrigger);
            mark.SetValue(FrameworkElement.StyleProperty, markStyle);

            item.AppendChild(mark);

            var number = new FrameworkElementFactory(typeof(TextBlock));
            number.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            // Фиксированная ширина номера: без неё каждый следующий номер
            // начинался бы с другой позиции, и значки ⚠ в разных строках
            // не совпали бы по вертикали.
            number.SetValue(FrameworkElement.WidthProperty, SheetNumberSlotWidth);
            number.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0));
            number.SetBinding(TextBlock.TextProperty, new WpfBinding("Number"));

            // Номер обычный светло-розовый; зелёным становится только тот,
            // что стоит рядом со знаком ⚠ у отмеченной строки.
            var numberStyle = new Style(typeof(TextBlock));
            numberStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, Theme.Text));
            numberStyle.Triggers.Add(ProblemOnCheckedTrigger());
            number.SetValue(FrameworkElement.StyleProperty, numberStyle);

            item.AppendChild(number);

            var itemTemplate = new DataTemplate { VisualTree = item };

            // Панель элементов: без неё ItemsControl выстраивает номера в столбик.
            var itemsPanel = new FrameworkElementFactory(typeof(StackPanel));
            itemsPanel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            var items = new FrameworkElementFactory(typeof(ItemsControl));
            items.SetValue(ItemsControl.ItemsSourceProperty, new WpfBinding("SheetRefs"));
            items.SetValue(ItemsControl.ItemTemplateProperty, itemTemplate);
            items.SetValue(ItemsControl.ItemsPanelProperty, new ItemsPanelTemplate(itemsPanel));

            var panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            panel.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            panel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            panel.AppendChild(items);

            return new DataTemplate { VisualTree = panel };
        }

        /// <summary>
        /// Колонка с галочкой. CheckBox создаётся в шаблоне ячейки, поэтому
        /// одна и та же разметка переиспользуется для всех строк.
        /// </summary>
        private static DataGridTemplateColumn CheckColumn(double width, double minWidth)
        {
            var checkBox = new FrameworkElementFactory(typeof(CheckBox));
            checkBox.SetBinding(CheckBox.IsCheckedProperty, new WpfBinding("IsSelected")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            checkBox.SetValue(FrameworkElement.StyleProperty, Theme.CreateCheckBoxStyle());

            return new DataGridTemplateColumn
            {
                Header = string.Empty,
                CellTemplate = new DataTemplate { VisualTree = checkBox },
                MinWidth = minWidth,
                Width = new DataGridLength(width)
            };
        }

        /// <summary>
        /// Знак ⚠ и номер рядом с ним становятся зелёными (цветом фона),
        /// но только у проблемного листа и только когда строка отмечена галочкой.
        ///
        /// Два условия сразу, поэтому нужен MultiDataTrigger:
        /// внутри шаблона ячейки IsProblem - это признак проблемности ЛИСТА,
        /// а признак отметки строки лежит в DataContext предка DataGridRow.
        /// AncestorLevel для FindAncestor нумеруется с единицы.
        /// </summary>
        private static MultiDataTrigger ProblemOnCheckedTrigger()
        {
            var trigger = new MultiDataTrigger();

            trigger.Conditions.Add(new Condition
            {
                Binding = new WpfBinding("DataContext.IsSelected")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGridRow), 1)
                },
                Value = true
            });

            trigger.Conditions.Add(new Condition
            {
                Binding = new WpfBinding("IsProblem"),
                Value = true
            });

            trigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, Theme.OnSelection));
            return trigger;
        }

        /// <summary>
        /// Разметка элемента списка листов: номер розовым, название светлым.
        /// </summary>
        private static DataTemplate SheetItemTemplate()
        {
            var row = new FrameworkElementFactory(typeof(StackPanel));
            row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            // Подсказка с причиной проблемы - как в колонке «Листы».
            row.SetValue(FrameworkElement.ToolTipProperty, new WpfBinding("SectionIssue"));

            var number = new FrameworkElementFactory(typeof(TextBlock));
            number.SetValue(TextBlock.ForegroundProperty, Theme.Accent);
            number.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            number.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0));
            number.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            number.SetBinding(TextBlock.TextProperty, new WpfBinding("Number"));
            row.AppendChild(number);

            var title = new FrameworkElementFactory(typeof(TextBlock));
            title.SetValue(TextBlock.ForegroundProperty, Theme.Text);
            title.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
            title.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            title.SetBinding(TextBlock.TextProperty, new WpfBinding("Name"));
            row.AppendChild(title);

            var template = new DataTemplate { VisualTree = row };
            template.Seal();
            return template;
        }
    }
}