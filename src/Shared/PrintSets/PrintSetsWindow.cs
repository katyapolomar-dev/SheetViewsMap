using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PallMall
{
    /// <summary>
    /// Окно наборов печати.
    ///
    /// Открывается сразу (как окно «Виды на листах»), данные читает отдельный
    /// лёгкий обработчик через Bridge: только листы, комплект и формат.
    /// Окно не модальное и не блокирует Revit.
    ///
    /// Фильтр по комплектам — отдельная кнопка с выпадающим списком.
    /// Пока комплект не выбран, наборов нет: список собирается по комплекту.
    /// </summary>
    internal sealed class PrintSetsWindow : Window
    {
        private static PrintSetsWindow _instance;

        private readonly TextBlock _documentLabel;
        private readonly Button _filterButton;
        private readonly Popup _filterPopup;
        private readonly ListBox _filterList;

        private readonly StackPanel _list;
        private readonly TextBlock _status;
        private readonly Button _createButton;

        private readonly DispatcherTimer _timer;

        /// <summary>Имена наборов, которые пользователь снял галочкой.</summary>
        private readonly HashSet<string> _unchecked =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private PrintSetData _data;
        private bool _dataRequested;
        private bool _dataFailed;

        private string _selectedSet;
        private PrintSetPlanResult _plan = new PrintSetPlanResult();
        private bool _awaitingResult;

        private const string NoSetSelected = "Комплект: не выбран";

        private PrintSetsWindow()
        {
            Title = "Наборы печати — PallMall";
            Width = 720;
            Height = 620;
            MinWidth = 560;
            MinHeight = 420;
            Background = Theme.Background;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResize;

            // Как у окна «Виды на листах»: окно принадлежит Revit, живёт поверх него,
            // не блокирует работу и сворачивается вместе с Revit.
            Topmost = false;
            ShowInTaskbar = false;
            ShowActivated = true;

            var root = new DockPanel { LastChildFill = true, Background = Theme.Background };

            // ---------- шапка: пояснение и фильтр ----------
            var header = new StackPanel { Margin = new Thickness(16, 14, 16, 0) };

            _documentLabel = Theme.CreateText(string.Empty, Theme.TextStrong, Theme.FontUi, true);
            header.Children.Add(_documentLabel);

            header.Children.Add(Theme.CreateText(
                "Наборы собираются по выбранному комплекту: имя — «комплект_формат», например «АР_А1x3».",
                Theme.TextMuted, Theme.FontSmall, false));

            var filterRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 10, 0, 0)
            };

            _filterButton = Theme.CreateButton(NoSetSelected + " ▾", false);
            _filterButton.MinWidth = 240;
            _filterButton.HorizontalContentAlignment = HorizontalAlignment.Left;
            _filterButton.ToolTip = "Выбрать комплект: наборы печати соберутся по нему";
            _filterButton.IsEnabled = false;
            _filterButton.Click += (s, e) => ToggleFilterPopup();
            filterRow.Children.Add(_filterButton);

            var selectAll = Theme.CreateButton("Выбрать все", false);
            selectAll.Margin = new Thickness(8, 0, 8, 0);
            selectAll.Click += (s, e) =>
            {
                _unchecked.Clear();
                RebuildList();
            };
            filterRow.Children.Add(selectAll);

            var selectNone = Theme.CreateButton("Снять все", false);
            selectNone.Click += (s, e) =>
            {
                foreach (PrintSetPlan plan in _plan.Plans)
                    _unchecked.Add(plan.Name);

                RebuildList();
            };
            filterRow.Children.Add(selectNone);

            header.Children.Add(filterRow);
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // ---------- список наборов ----------
            _list = new StackPanel { Margin = new Thickness(16, 10, 16, 10) };

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _list
            };
            root.Children.Add(scroll);

            // ---------- низ: статус и кнопки ----------
            var bottom = new StackPanel { Margin = new Thickness(16, 0, 16, 14) };

            _status = Theme.CreateText(string.Empty, Theme.TextMuted, Theme.FontSmall, false);
            _status.TextWrapping = TextWrapping.Wrap;
            _status.Margin = new Thickness(0, 0, 0, 10);
            bottom.Children.Add(_status);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            _createButton = Theme.CreateButton("Создать", true);
            _createButton.Width = 140;
            _createButton.Margin = new Thickness(0, 0, 8, 0);
            _createButton.IsEnabled = false;
            _createButton.Click += (s, e) => CreateSelected();
            buttons.Children.Add(_createButton);

            var close = Theme.CreateButton("Закрыть", false);
            close.Width = 110;
            close.Click += (s, e) => Close();
            buttons.Children.Add(close);

            bottom.Children.Add(buttons);
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(bottom);

            Content = root;

            // ---------- выпадающий список комплектов ----------
            _filterList = new ListBox
            {
                Style = Theme.CreateListBoxStyle(),
                MaxHeight = 340,
                MinWidth = 300
            };
            _filterList.SelectionChanged += (s, e) => ApplyFilterSelection();

            _filterPopup = new Popup
            {
                Placement = PlacementMode.Bottom,
                PlacementTarget = _filterButton,
                StaysOpen = false,
                AllowsTransparency = true,
                Child = new Border
                {
                    Background = Theme.Surface,
                    BorderBrush = Theme.BorderColor,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(2),
                    Child = _filterList
                }
            };

            Closed += (s, e) =>
            {
                _timer.Stop();
                _instance = null;
            };

            // Данные читает Revit через Bridge — забираем по таймеру, окно открыто сразу.
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _timer.Tick += OnTimerTick;
            _timer.Start();

            SetStatus("Читаю листы и основные надписи…");
            RebuildList();
            RequestData();
        }

        /// <summary>
        /// Показывает окно (или активирует уже открытое).
        /// Модель читается уже после открытия — в фоне, через ExternalEvent.
        /// </summary>
        internal static void Show(IntPtr revitHandle, string documentName)
        {
            if (_instance != null)
            {
                _instance.Activate();
                return;
            }

            var window = new PrintSetsWindow();
            if (!string.IsNullOrEmpty(documentName))
                window._documentLabel.Text = documentName;

            _instance = window;

            if (revitHandle != IntPtr.Zero)
            {
                try
                {
                    new System.Windows.Interop.WindowInteropHelper(window).Owner = revitHandle;
                }
                catch (Exception)
                {
                    // Без владельца окно будет обычным самостоятельным окном.
                }
            }

            window.Show();
            window.Activate();
        }

        // ------------------------------------------------------------------ данные

        private void RequestData()
        {
            if (_data != null || _dataRequested || _dataFailed)
                return;

            if (!Bridge.IsReady)
                return;

            _dataRequested = Bridge.RequestPrintSetData();
        }

        private void ApplyData(PrintSetData data)
        {
            if (data == null)
                return;

            if (!string.IsNullOrEmpty(data.Error))
            {
                _dataFailed = true;
                SetStatus(data.Error, true);
                return;
            }

            _data = data;

            if (!string.IsNullOrEmpty(data.DocumentName))
                _documentLabel.Text = data.DocumentName;

            FillSetList();
            _filterButton.IsEnabled = true;

            SetStatus(string.Format(
                "Комплектов: {0}. Выберите комплект — листы прочитаны за {1:0.0} с.",
                _filterList.Items.Count,
                data.ReadMilliseconds / 1000.0));
        }

        // ------------------------------------------------------------------ фильтр

        private void FillSetList()
        {
            var sets = new List<string>();

            if (_data != null && _data.Sheets != null)
            {
                sets.AddRange(_data.Sheets
                    .Select(s => s.SetName)
                    .Where(s => !string.IsNullOrEmpty(s) && s != PrintSetPlanner.NoSet)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
            }

            _filterList.ItemsSource = sets;
        }

        private void ToggleFilterPopup()
        {
            if (_filterPopup.IsOpen)
            {
                _filterPopup.IsOpen = false;
                return;
            }

            if (_filterList.Items.Count == 0)
            {
                SetStatus("В модели не найдено ни одного комплекта: проверьте параметр «" +
                          SheetMapReader.SetParameterName + "» на листах.", true);
                return;
            }

            // Снимаем выбор, иначе повторный выбор того же комплекта не вызовет событие.
            _filterList.SelectedItem = null;
            _filterPopup.IsOpen = true;
        }

        private void ApplyFilterSelection()
        {
            string selected = _filterList.SelectedItem as string;
            if (string.IsNullOrEmpty(selected))
                return;

            _selectedSet = selected;
            _filterPopup.IsOpen = false;
            SetFilterButtonText("Комплект: " + selected + " ▾");
            RebuildList();
        }

        private void SetFilterButtonText(string text)
        {
            _filterButton.Content = Theme.CreateText(text, Theme.Accent, Theme.FontUi, false);
        }

        // ------------------------------------------------------------------ список

        private void RebuildList()
        {
            _list.Children.Clear();

            if (_data == null || string.IsNullOrEmpty(_selectedSet))
            {
                _plan = new PrintSetPlanResult();
                _list.Children.Add(Theme.CreateText(
                    _data == null
                        ? "Читаю листы…"
                        : "Выберите комплект — наборы соберутся по нему.",
                    Theme.TextMuted, Theme.FontUi, false));

                _createButton.IsEnabled = false;
                return;
            }

            _plan = PrintSetPlanner.Build(_data.Sheets, _selectedSet);

            if (_plan.Plans.Count == 0)
            {
                _list.Children.Add(Theme.CreateText(
                    "Для этого комплекта подходящих листов нет: проверьте формат на основных надписях.",
                    Theme.TextMuted, Theme.FontUi, false));
            }

            foreach (PrintSetPlan plan in _plan.Plans)
                _list.Children.Add(CreateRow(plan));

            _createButton.IsEnabled = _plan.Plans.Count > 0 && !_awaitingResult;
            UpdateSummary();
        }

        private FrameworkElement CreateRow(PrintSetPlan plan)
        {
            var row = new Border
            {
                Padding = new Thickness(6, 5, 6, 5),
                Margin = new Thickness(0, 0, 0, 2),
                Background = Theme.RowBackground,
                CornerRadius = new CornerRadius(3)
            };

            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var checkBox = new CheckBox
            {
                Style = Theme.CreateCheckBoxStyle(),
                IsChecked = !_unchecked.Contains(plan.Name),
                Margin = new Thickness(0, 0, 10, 0)
            };

            var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            label.Inlines.Add(new Run(plan.Name)
            {
                Foreground = Theme.Text,
                FontSize = Theme.FontUi,
                FontWeight = FontWeights.SemiBold
            });
            label.Inlines.Add(new Run("   " + plan.SheetCount + " " + SheetsWord(plan.SheetCount))
            {
                Foreground = Theme.TextMuted,
                FontSize = Theme.FontSmall
            });

            // Клик по подписи тоже переключает галочку — так удобнее попадать.
            label.Cursor = Cursors.Hand;
            label.MouseLeftButtonUp += (s, e) =>
                checkBox.IsChecked = !(checkBox.IsChecked ?? false);

            checkBox.Checked += (s, e) =>
            {
                _unchecked.Remove(plan.Name);
                UpdateSummary();
            };
            checkBox.Unchecked += (s, e) =>
            {
                _unchecked.Add(plan.Name);
                UpdateSummary();
            };

            panel.Children.Add(checkBox);
            panel.Children.Add(label);
            row.Child = panel;
            return row;
        }

        // ------------------------------------------------------------------ создание

        private void CreateSelected()
        {
            List<PrintSetPlan> selected = _plan.Plans
                .Where(p => !_unchecked.Contains(p.Name))
                .ToList();

            if (selected.Count == 0)
            {
                SetStatus("Не отмечено ни одного набора.", true);
                return;
            }

            if (!Bridge.IsReady)
            {
                SetStatus("Плагин ещё загружается — попробуйте через секунду.", true);
                return;
            }

            if (!Bridge.RequestCreatePrintSets(selected))
            {
                SetStatus("Предыдущее создание ещё выполняется, подождите…");
                return;
            }

            _awaitingResult = true;
            _createButton.IsEnabled = false;
            SetStatus(string.Format("Создаю наборы печати: {0}, листов: {1}…",
                selected.Count, selected.Sum(p => p.SheetCount)));
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            if (_data == null && !_dataFailed)
            {
                PrintSetData data;
                if (Bridge.TryTakePrintSetData(out data))
                {
                    _dataRequested = false;
                    ApplyData(data);
                }
                else
                {
                    RequestData();
                }
            }

            if (!_awaitingResult)
                return;

            PrintSetReport report;
            string error;
            if (!Bridge.TryTakePrintSetResult(out report, out error))
                return;

            _awaitingResult = false;
            _createButton.IsEnabled = _plan.Plans.Count > 0;

            if (error != null)
            {
                SetStatus("Не удалось создать наборы: " + error, true);
                return;
            }

            SetStatus(BuildReportText(report), report.Errors.Count > 0);
        }

        private static string BuildReportText(PrintSetReport report)
        {
            var parts = new List<string>();

            if (report.Created.Count > 0)
                parts.Add("создано: " + report.Created.Count);

            if (report.Updated.Count > 0)
                parts.Add("обновлено: " + report.Updated.Count);

            parts.Add("листов: " + report.SheetCount);

            string text = "Готово — " + string.Join(", ", parts) + ".";

            if (report.Errors.Count > 0)
                text += " Ошибки: " + string.Join("; ", report.Errors);

            return text;
        }

        // ------------------------------------------------------------------ статус

        private void UpdateSummary()
        {
            int sets = 0;
            int sheets = 0;

            foreach (PrintSetPlan plan in _plan.Plans)
            {
                if (_unchecked.Contains(plan.Name))
                    continue;

                sets++;
                sheets += plan.SheetCount;
            }

            string text = string.Format(
                "Отмечено наборов: {0}, листов: {1}.", sets, sheets);

            if (_plan.Skipped.Count > 0)
            {
                string reasons = string.Join(", ", _plan.Skipped
                    .GroupBy(s => s.Reason)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key + " — " + g.Count()));

                text += string.Format(" Пропущено листов: {0} ({1}).", _plan.Skipped.Count, reasons);
            }

            if (_plan.Warnings.Count > 0)
                text += " " + _plan.Warnings[0];

            _status.Foreground = _plan.Plans.Count > 0 ? Theme.TextMuted : Theme.Accent;
            _status.Text = text;
        }

        private void SetStatus(string text, bool isError = false)
        {
            _status.Foreground = isError ? Theme.Accent : Theme.TextMuted;
            _status.Text = text;
        }

        private static string SheetsWord(int count)
        {
            int mod100 = count % 100;
            int mod10 = count % 10;

            if (mod100 >= 11 && mod100 <= 14)
                return "листов";

            if (mod10 == 1)
                return "лист";

            if (mod10 >= 2 && mod10 <= 4)
                return "листа";

            return "листов";
        }
    }
}
