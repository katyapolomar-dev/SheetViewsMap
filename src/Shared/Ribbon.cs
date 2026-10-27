using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

// Color и Point есть и в Revit API, и в WPF — берём версию WPF явно.
using WpfColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;

namespace SheetViewsMap
{
    /// <summary>
    /// Команда кнопки на ленте: открывает отдельное окно плагина поверх окон Revit,
    /// а если окно уже открыто — закрывает его.
    ///
    /// Атрибуты [Transaction] и [Regeneration] обязательны. Без них Revit вообще
    /// не запускает команду и показывает окно
    /// «Невозможно запустить надстройку ... так как она не присвоена транзакции Transaction».
    /// Окно мы не меняем, но атрибут Revit требует наличия.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PaneCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                // MainWindowHandle - дескриптор окна Revit: по нему окно плагина
                // центрируется и прячется вместе со свёрнутым Revit.
                SheetMapWindow.Toggle(data.Application.MainWindowHandle);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("SheetViewsMap", "Не удалось открыть окно плагина:\n\n" + ex.Message);
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Создаёт на ленте Revit собственную вкладку с кнопкой вызова панели.
    /// </summary>
    internal static class Ribbon
    {
        private const string ButtonName = "SheetViewsMapPaneButton";

        internal static void Create(UIControlledApplication application)
        {
            try
            {
                application.CreateRibbonTab(App.RibbonTabName);
            }
            catch (Exception)
            {
                // Вкладка уже существует (например, Revit перечитал аддины).
                // Это не ошибка — просто идём дальше и берём панель внутри вкладки.
            }

            RibbonPanel panel;
            try
            {
                panel = application.CreateRibbonPanel(App.RibbonTabName, App.RibbonPanelName);
            }
            catch (Exception)
            {
                // Не удалось создать панель — кнопки не будет, но плагин продолжит работать.
                return;
            }

            // Защита от второй кнопки, если аддины перечитали без перезапуска Revit.
            if (panel.GetItems().Any(i => i.Name == ButtonName))
                return;

            var data = new PushButtonData(
                ButtonName,
                App.RibbonButtonText,
                typeof(App).Assembly.Location,
                typeof(PaneCommand).FullName)
            {
                ToolTip = App.RibbonButtonToolTip,
                LongDescription = App.RibbonButtonToolTip
            };

            // Иконку рисует тема - та же, что и у кнопок в окне плагина.
            try
            {
                ImageSource icon = Theme.CreatePlusIcon();
                data.LargeImage = icon;
                data.Image = icon;
            }
            catch (Exception)
            {
                // Без иконки кнопка тоже работает, просто выглядит пустой.
            }

            panel.AddItem(data);
        }
    }
}