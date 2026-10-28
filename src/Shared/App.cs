using System;
using Autodesk.Revit.UI;

namespace SheetViewsMap
{
    /// <summary>
    /// Точка входа плагина. Revit вызывает OnStartup при загрузке и ищет здесь класс App.
    /// </summary>
    public class App : IExternalApplication
    {
        // ---- Названия, которые видит пользователь. Меняются здесь. ----

        /// <summary>Заголовок окна плагина.</summary>
        internal const string WindowTitle = "Виды на листах";

        /// <summary>Вкладка на ленте Revit.</summary>
        internal const string RibbonTabName = "Sheet Views Map";

        /// <summary>Группа (панель) внутри вкладки.</summary>
        internal const string RibbonPanelName = "Виды на листах";

        /// <summary>Подпись на кнопке.</summary>
        internal const string RibbonButtonText = "Открыть окно";

        /// <summary>Подпись на кнопке настроек.</summary>
        internal const string SettingsButtonText = "Настройки";

        /// <summary>Подсказка при наведении на кнопку настроек.</summary>
        internal const string SettingsButtonToolTip =
            "Версия плагина и проверка обновлений.";

        /// <summary>Подсказка при наведении на кнопку.</summary>
        internal const string RibbonButtonToolTip =
            "Показать или скрыть окно «Виды на листах»: список всех номеров видов и листов, на которых они размещены.";

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                Bridge.Initialize();
                Ribbon.Create(application);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("SheetViewsMap",
                    "Не удалось загрузить плагин:\n\n" + ex.Message);
                return Result.Failed;
            }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}