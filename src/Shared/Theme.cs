using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SheetViewsMap
{
    /// <summary>
    /// Цвета и готовые элементы интерфейса.
    ///
    /// Палитра: тёмно-зелёный фон и розовый акцент.
    /// Кисти "замораживаются" (Freeze) - это обязательное требование WPF
    /// для ресурсов, которыми пользуются несколько элементов интерфейса.
    /// </summary>
    internal static class Theme
    {
        // ---- Палитра ----

        internal static readonly Brush Background = Make(0x0E, 0x3B, 0x33);
        internal static readonly Brush Surface = Make(0x14, 0x48, 0x3E);
        internal static readonly Brush SurfaceAlt = Make(0x1B, 0x56, 0x4A);
        internal static readonly Brush RowBackground = Make(0x0F, 0x3E, 0x36);
        internal static readonly Brush RowBackgroundAlt = Make(0x12, 0x44, 0x3A);
        internal static readonly Brush HeaderBackground = Make(0x16, 0x4F, 0x44);

        // ВАЖНО: поле нельзя называть Border - внутри класса это имя начнёт
        // перекрывать тип System.Windows.Controls.Border, и код перестанет
        // компилироваться. Та же история, что с Visibility в SheetMapPane.
        internal static readonly Brush BorderColor = Make(0x1C, 0x5A, 0x4E);

        internal static readonly Brush Accent = Make(0xF2, 0xC1, 0xD1);
        internal static readonly Brush AccentHover = Make(0xF8, 0xD3, 0xDE);
        internal static readonly Brush AccentPressed = Make(0xE3, 0xA8, 0xBB);

        internal static readonly Brush Text = Make(0xEC, 0xD8, 0xE0);
        internal static readonly Brush TextStrong = Make(0xF7, 0xCF, 0xDC);
        internal static readonly Brush TextMuted = Make(0x86, 0xAF, 0xA2);

        internal static readonly Brush Selection = Make(0xF2, 0xC1, 0xD1);
        internal static readonly Brush OnSelection = Make(0x0E, 0x3B, 0x33);

        // ---- Размеры шрифта (в пунктах) ----

        /// <summary>Текст в ячейках таблицы и заголовки столбцов.</summary>
        internal const double FontCell = 14;

        /// <summary>Поля ввода, кнопки, списки.</summary>
        internal const double FontUi = 13.5;

        /// <summary>Подписи, статус, подсказки.</summary>
        internal const double FontSmall = 12.5;

        private static SolidColorBrush Make(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        // ------------------------------------------------------------------ кнопки

        /// <summary>
        /// Плоская кнопка со скруглением.
        /// primary = true - розовая заливка (главное действие),
        /// primary = false - тёмная заливка с розовым текстом.
        /// </summary>
        /// <summary>
        /// Плоская кнопка.
        /// primary = true - розовая заливка с тёмно-зелёным текстом (главное действие),
        /// primary = false - тёмная заливка с розовым текстом.
        /// </summary>
        internal static Button CreateButton(string text, bool primary)
        {
            Brush background = primary ? Accent : Surface;
            Brush foreground = primary ? OnSelection : Accent;
            Brush hover = primary ? AccentHover : SurfaceAlt;
            Brush pressed = primary ? AccentPressed : HeaderBackground;

            return new Button
            {
                // Текст кладём готовым TextBlock с явным цветом: так он
                // гарантированно не сольётся с фоном главной кнопки.
                Content = CreateText(text, foreground, FontUi, false),
                Foreground = foreground,
                Background = background,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(14, 6, 14, 6),
                MinHeight = 30,
                Cursor = Cursors.Hand,
                FocusVisualStyle = null,
                Template = CreateButtonTemplate(background, foreground, hover, pressed)
            };
        }

        /// <summary>Обводка иконки на ленте.</summary>
        private static readonly Brush IconStroke = Make(0xFC, 0xE7, 0xEE);

        private static ControlTemplate CreateButtonTemplate(Brush background, Brush foreground, Brush hover, Brush pressed)
        {
            var template = new ControlTemplate(typeof(Button));

            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "buttonBorder";

            // Фон берём у самой кнопки - тогда её можно переключать
            // (включена/выключена), просто сменив Button.Background.
            border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            border.SetValue(Border.BorderThicknessProperty, new Thickness(0));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            border.SetValue(Border.PaddingProperty, new Thickness(14, 6, 14, 6));

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetBinding(TextElement.ForegroundProperty, new Binding("Foreground") { RelativeSource = RelativeSource.TemplatedParent });
            content.SetBinding(TextElement.FontWeightProperty, new Binding("FontWeight") { RelativeSource = RelativeSource.TemplatedParent });
            border.AppendChild(content);

            template.VisualTree = border;

            var over = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            over.Setters.Add(new Setter(Border.BackgroundProperty, hover) { TargetName = "buttonBorder" });
            template.Triggers.Add(over);

            var down = new Trigger { Property = Button.IsPressedProperty, Value = true };
            down.Setters.Add(new Setter(Border.BackgroundProperty, pressed) { TargetName = "buttonBorder" });
            template.Triggers.Add(down);

            var disabled = new Trigger { Property = Button.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(Border.BackgroundProperty, RowBackground) { TargetName = "buttonBorder" });
            disabled.Setters.Add(new Setter(TextElement.ForegroundProperty, TextMuted));
            template.Triggers.Add(disabled);

            return template;
        }

        /// <summary>
        /// Переключает вид кнопки: включённая - розовая заливка с тёмно-зелёным текстом.
        /// Фон кнопки берётся из Button.Background, надпись - из TextBlock внутри кнопки.
        /// </summary>
        internal static void SetButtonActive(Button button, bool active)
        {
            button.Background = active ? Accent : Surface;
            button.Foreground = active ? OnSelection : Accent;

            var text = button.Content as TextBlock;
            if (text != null)
                text.Foreground = active ? OnSelection : Accent;
        }

        // ------------------------------------------------------------------ иконка

        /// <summary>
        /// Иконка кнопки «Настройки»: шестерёнка в тех же розовых тонах,
        /// что и остальные иконки. Рисуется кодом, внешних файлов не нужно.
        /// </summary>
        internal static ImageSource CreateSettingsIcon()
        {
            var drawing = new DrawingGroup();

            // Прозрачный квадрат задаёт размер иконки ровно 32x32.
            drawing.Children.Add(new GeometryDrawing(
                Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 32, 32))));

            var fill = new LinearGradientBrush();
            fill.StartPoint = new System.Windows.Point(0.3, 0);
            fill.EndPoint = new System.Windows.Point(0.7, 1);
            fill.GradientStops.Add(new GradientStop(Color.FromRgb(0xF9, 0xD6, 0xE2), 0.0));
            fill.GradientStops.Add(new GradientStop(Color.FromRgb(0xF2, 0xC1, 0xD1), 0.55));
            fill.GradientStops.Add(new GradientStop(Color.FromRgb(0xE0, 0xA8, 0xBD), 1.0));
            fill.Freeze();

            var stroke = new Pen(IconStroke, 1.4);
            stroke.Freeze();

            const double center = 16.0;
            const double toothWidth = 6.5;
            const double toothLength = 5.5;
            const double toothRadius = 12.0;

            // Зубья шестерёнки: восемь скруглённых прямоугольников вокруг центра.
            for (int i = 0; i < 8; i++)
            {
                var tooth = new RectangleGeometry(
                    new Rect(center - toothWidth / 2, center - toothRadius - toothLength / 2, toothWidth, toothLength),
                    1.6, 1.6);
                tooth.Transform = new RotateTransform(i * 45.0, center, center);
                tooth.Freeze();
                drawing.Children.Add(new GeometryDrawing(fill, stroke, tooth));
            }

            // Обод: кольцо — внешний круг с вырезанным центром (дырка прозрачная).
            var outer = new EllipseGeometry(new System.Windows.Point(center, center), 10.5, 10.5);
            var inner = new EllipseGeometry(new System.Windows.Point(center, center), 4.6, 4.6);
            var ring = new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner);
            drawing.Children.Add(new GeometryDrawing(fill, stroke, ring));

            drawing.Freeze();

            var image = new DrawingImage(drawing);
            image.Freeze();
            return image;
        }

        /// <summary>
        /// Иконка кнопки на ленте: объёмный плюс в тех же розовых тонах,
        /// что и кнопки в окне. Рисуется кодом, внешних файлов не нужно.
        /// </summary>
        internal static ImageSource CreatePlusIcon()
        {
            var drawing = new DrawingGroup();

            // Прозрачный квадрат задаёт размер иконки ровно 32x32: без него
            // границы рисуются по самому плюсу и картинка получается 32x29.
            drawing.Children.Add(new GeometryDrawing(
                Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 32, 32))));

            var fill = new LinearGradientBrush();
            fill.StartPoint = new System.Windows.Point(0.3, 0);
            fill.EndPoint = new System.Windows.Point(0.7, 1);
            fill.GradientStops.Add(new GradientStop(Color.FromRgb(0xF9, 0xD6, 0xE2), 0.0));
            fill.GradientStops.Add(new GradientStop(Color.FromRgb(0xF2, 0xC1, 0xD1), 0.55));
            fill.GradientStops.Add(new GradientStop(Color.FromRgb(0xE0, 0xA8, 0xBD), 1.0));
            fill.Freeze();

            var stroke = new Pen(IconStroke, 1.4);
            stroke.Freeze();

            // Плюс собираем из двух скруглённых прямоугольников со скруглёнными
            // концами - так он выглядит «мягким», а не угловатым.
            var shapes = new Geometry[]
            {
                new RectangleGeometry(new Rect(3.5, 12.5, 25, 7), 3.5, 3.5),
                new RectangleGeometry(new Rect(12.5, 3.5, 7, 25), 3.5, 3.5)
            };

            foreach (Geometry shape in shapes)
                drawing.Children.Add(new GeometryDrawing(fill, stroke, shape));

            // Мягкий блик сверху.
            var highlight = new LinearGradientBrush();
            highlight.StartPoint = new System.Windows.Point(0.5, 0);
            highlight.EndPoint = new System.Windows.Point(0.5, 1);
            highlight.GradientStops.Add(new GradientStop(Color.FromArgb(0x77, 0xFF, 0xFF, 0xFF), 0.0));
            highlight.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.5));
            highlight.Freeze();

            foreach (Geometry shape in shapes)
            {
                var clip = new RectangleGeometry(new Rect(0, 0, 32, 15));
                var clipped = new CombinedGeometry(clip, shape);
                drawing.Children.Add(new GeometryDrawing(highlight, null, clipped));
            }

            drawing.Freeze();

            var image = new DrawingImage(drawing);
            image.Freeze();
            return image;
        }

        // ------------------------------------------------------------------ поле поиска

        internal static TextBox CreateSearchBox()
        {
            return new TextBox
            {
                Background = Surface,
                Foreground = Text,
                CaretBrush = Accent,
                SelectionBrush = Accent,
                BorderBrush = BorderColor,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 7, 8, 7),
                FontSize = FontUi,
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        // ------------------------------------------------------------------ текст

        internal static TextBlock CreateText(string text, Brush color, double size, bool bold)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = color,
                FontSize = size,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        // ------------------------------------------------------------------ таблица

        /// <summary>
        /// Стиль текста в ячейке: светлый, а у отмеченной строки - тёмный на розовом.
        /// </summary>
        internal static Style CreateCellTextStyle(bool centered)
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.ForegroundProperty, Text));
            style.Setters.Add(new Setter(TextBlock.FontSizeProperty, FontCell));
            style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(6, 0, 6, 0)));

            if (centered)
                style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center));

            // Отмеченная строка заливается розовым - текст делаем тёмным.
            var checkedTrigger = new DataTrigger
            {
                Binding = new Binding("IsSelected"),
                Value = true
            };
            checkedTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, OnSelection));
            style.Triggers.Add(checkedTrigger);

            return style;
        }

        /// <summary>
        /// Стиль ячейки: обычная - прозрачная, отмеченная - розовая,
        /// просто выделенная курсором - чуть светлее фона.
        /// </summary>
        internal static Style CreateCellStyle()
        {
            var style = new Style(typeof(DataGridCell));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));

            // Строка выделена курсором.
            var highlighted = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
            highlighted.Setters.Add(new Setter(Control.BackgroundProperty, SurfaceAlt));
            style.Triggers.Add(highlighted);

            // Строка отмечена галочкой. Этот триггер должен быть последним,
            // чтобы он имел приоритет при совпадении условий.
            var checkedTrigger = new DataTrigger
            {
                Binding = new Binding("IsSelected"),
                Value = true
            };
            checkedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, Selection));
            style.Triggers.Add(checkedTrigger);

            return style;
        }

        internal static Style CreateHeaderStyle()
        {
            var style = new Style(typeof(DataGridColumnHeader));
            style.Setters.Add(new Setter(Control.BackgroundProperty, HeaderBackground));
            style.Setters.Add(new Setter(Control.ForegroundProperty, TextStrong));
            style.Setters.Add(new Setter(Control.FontSizeProperty, FontCell));
            style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, BorderColor));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(7, 7, 7, 7)));

            return style;
        }

        // ------------------------------------------------------------------ прочее

        /// <summary>
        /// Галочка в розовом цвете. Системный шаблон CheckBox цвета не
        /// поддерживает, поэтому рисуем свой: рамка розовая, при отметке
        /// заливается розовым, а галочка внутри - тёмно-зелёная.
        /// </summary>
        internal static Style CreateCheckBoxStyle()
        {
            var style = new Style(typeof(CheckBox));
            style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Accent));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(Control.TemplateProperty, CreateCheckBoxTemplate()));
            return style;
        }

        private static ControlTemplate CreateCheckBoxTemplate()
        {
            var template = new ControlTemplate(typeof(CheckBox));

            // Рамка 17x17 с розовой обводкой.
            var box = new FrameworkElementFactory(typeof(Border));
            box.Name = "checkBoxBorder";
            box.SetValue(Border.WidthProperty, 17.0);
            box.SetValue(Border.HeightProperty, 17.0);
            box.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            box.SetValue(Border.BorderBrushProperty, Accent);
            box.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
            box.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));

            // Сама галочка. Размер НЕ задаём - тогда WPF измеряет её по геометрии
            // и центрирует внутри рамки через Horizontal/VerticalAlignment.
            var mark = new FrameworkElementFactory(typeof(Path));
            mark.Name = "checkBoxMark";
            mark.SetValue(Path.DataProperty, Geometry.Parse("M 0.5,3.4 L 3.1,6 L 7.6,0.4"));
            mark.SetValue(Path.StrokeProperty, OnSelection);
            mark.SetValue(Path.StrokeThicknessProperty, 2.0);
            mark.SetValue(Path.StrokeStartLineCapProperty, PenLineCap.Round);
            mark.SetValue(Path.StrokeEndLineCapProperty, PenLineCap.Round);
            mark.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            mark.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            mark.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);

            box.AppendChild(mark);
            template.VisualTree = box;

            var checkedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, Accent) { TargetName = "checkBoxBorder" });

            // Тёмная рамка нужна, чтобы розовая галочка не сливалась
            // с розовой заливкой отмеченной строки.
            checkedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, OnSelection) { TargetName = "checkBoxBorder" });
            checkedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible) { TargetName = "checkBoxMark" });
            template.Triggers.Add(checkedTrigger);

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, AccentHover) { TargetName = "checkBoxBorder" });
            template.Triggers.Add(hoverTrigger);

            var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4) { TargetName = "checkBoxBorder" });
            template.Triggers.Add(disabledTrigger);

            template.Seal();
            return template;
        }

        internal static Style CreateListBoxStyle()
        {
            var style = new Style(typeof(ListBox));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Surface));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Text));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, BorderColor));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            return style;
        }

        // ------------------------------------------------------------------ выпадающий список

        /// <summary>
        /// Выпадающий список в цветах темы. Системный шаблон ComboBox светлый,
        /// поэтому рисуем свой: рамка, список с Popup и розовая стрелка.
        /// </summary>
        internal static ComboBox CreateComboBox()
        {
            var combo = new ComboBox
            {
                Background = Surface,
                Foreground = Text,
                BorderBrush = BorderColor,
                BorderThickness = new Thickness(1),
                FontSize = FontUi,
                MinHeight = 32,
                Padding = new Thickness(9, 5, 28, 5),
                VerticalContentAlignment = VerticalAlignment.Center,
                MaxDropDownHeight = 320,
                ItemContainerStyle = CreateComboBoxItemStyle()
            };

            // Прикреплённое свойство задаём отдельно - в инициализаторе
            // объекта компилятор не понимает, к чему оно относится.
            ScrollViewer.SetVerticalScrollBarVisibility(combo, ScrollBarVisibility.Auto);

            combo.Template = CreateComboBoxTemplate();
            return combo;
        }

        private static Style CreateComboBoxItemStyle()
        {
            var style = new Style(typeof(ComboBoxItem));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Text));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Surface));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9, 6, 9, 6)));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));

            var hovered = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
            hovered.Setters.Add(new Setter(Control.BackgroundProperty, HeaderBackground));
            style.Triggers.Add(hovered);

            var chosen = new Trigger { Property = ComboBoxItem.IsSelectedProperty, Value = true };
            chosen.Setters.Add(new Setter(Control.BackgroundProperty, Accent));
            chosen.Setters.Add(new Setter(Control.ForegroundProperty, OnSelection));
            style.Triggers.Add(chosen);

            return style;
        }

        private static ControlTemplate CreateComboBoxTemplate()
        {
            var template = new ControlTemplate(typeof(ComboBox));
            var grid = new FrameworkElementFactory(typeof(Grid));

            // ---- рамка ----
            var outline = new FrameworkElementFactory(typeof(Border));
            outline.Name = "comboOutline";
            outline.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
            outline.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });
            outline.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            outline.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            grid.AppendChild(outline);

            // ---- выбранное значение ----
            // ContentPresenter лежит рядом с кнопкой, а не внутри неё: только так
            // ContentSource="SelectionBoxItem" находит значение у ComboBox,
            // а не у ToggleButton.
            var selected = new FrameworkElementFactory(typeof(ContentPresenter));
            selected.SetValue(ContentPresenter.ContentSourceProperty, "SelectionBoxItem");
            selected.SetBinding(ContentPresenter.ContentTemplateProperty, new Binding("SelectionBoxItemTemplate") { RelativeSource = RelativeSource.TemplatedParent });
            selected.SetBinding(ContentPresenter.ContentStringFormatProperty, new Binding("SelectionBoxItemStringFormat") { RelativeSource = RelativeSource.TemplatedParent });
            selected.SetBinding(FrameworkElement.MarginProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
            selected.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            selected.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            selected.SetValue(UIElement.IsHitTestVisibleProperty, false);
            grid.AppendChild(selected);

            // ---- розовая стрелка ----
            var arrow = new FrameworkElementFactory(typeof(Path));
            arrow.SetValue(Path.DataProperty, Geometry.Parse("M 0,0 L 4.5,4.5 L 9,0"));
            arrow.SetValue(Path.StrokeProperty, Accent);
            arrow.SetValue(Path.StrokeThicknessProperty, 1.6);
            arrow.SetValue(Path.StrokeEndLineCapProperty, PenLineCap.Round);
            arrow.SetValue(Path.StrokeStartLineCapProperty, PenLineCap.Round);
            arrow.SetValue(FrameworkElement.WidthProperty, 10.0);
            arrow.SetValue(FrameworkElement.HeightProperty, 6.0);
            arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 11, 0));
            arrow.SetValue(UIElement.IsHitTestVisibleProperty, false);
            grid.AppendChild(arrow);

            // ---- прозрачная кнопка поверх: ловит щелчки ----
            var toggle = new FrameworkElementFactory(typeof(ToggleButton));
            toggle.Name = "ToggleDropDown";
            toggle.SetValue(ToggleButton.FocusableProperty, false);
            toggle.SetValue(ToggleButton.ClickModeProperty, ClickMode.Press);
            toggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding("IsDropDownOpen")
            {
                RelativeSource = RelativeSource.TemplatedParent,
                Mode = BindingMode.TwoWay
            });

            var toggleTemplate = new ControlTemplate(typeof(ToggleButton));
            var toggleSurface = new FrameworkElementFactory(typeof(Border));

            // Фон обязателен: у Border с Background = null отключён hit-test,
            // и щелчок по списку не доходит до кнопки - выпадающий список
            // просто не раскрывается.
            toggleSurface.SetValue(Border.BackgroundProperty, Brushes.Transparent);

            toggleTemplate.VisualTree = toggleSurface;
            toggle.SetValue(Control.TemplateProperty, toggleTemplate);
            grid.AppendChild(toggle);

            // ---- выпадающий список ----
            var popup = new FrameworkElementFactory(typeof(Popup));
            popup.Name = "PART_Popup";
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(Popup.PopupAnimationProperty, PopupAnimation.Fade);
            popup.SetValue(Popup.FocusableProperty, false);
            popup.SetBinding(Popup.IsOpenProperty, new Binding("IsDropDownOpen")
            {
                RelativeSource = RelativeSource.TemplatedParent,
                Mode = BindingMode.TwoWay
            });

            var popupBorder = new FrameworkElementFactory(typeof(Border));
            popupBorder.SetValue(Border.BackgroundProperty, Surface);
            popupBorder.SetValue(Border.BorderBrushProperty, BorderColor);
            popupBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            popupBorder.SetValue(Border.MarginProperty, new Thickness(0, 2, 0, 2));

            // У рамки НЕ задаём Padding: внутри Popup он учитывается дважды
            // вместе с высотой ScrollViewer, и последний пункт обрезается.
            // Отступы пунктов заданы в стиле ComboBoxItem.

            // Ограничение высоты задаём на рамке, а не на ScrollViewer:
            // внутри Popup привязка к MaxDropDownHeight считается неверно
            // и последний пункт списка обрезается.
            popupBorder.SetBinding(FrameworkElement.MaxHeightProperty,
                new Binding("MaxDropDownHeight") { RelativeSource = RelativeSource.TemplatedParent });

            var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
            scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);

            var items = new FrameworkElementFactory(typeof(ItemsPresenter));
            scroll.AppendChild(items);
            popupBorder.AppendChild(scroll);
            popup.AppendChild(popupBorder);
            grid.AppendChild(popup);

            template.VisualTree = grid;

            var focused = new Trigger { Property = ComboBox.IsKeyboardFocusWithinProperty, Value = true };
            focused.Setters.Add(new Setter(Border.BorderBrushProperty, Accent) { TargetName = "comboOutline" });
            template.Triggers.Add(focused);

            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5) { TargetName = "comboOutline" });
            template.Triggers.Add(disabled);

            return template;
        }
    }
}