using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using SheetViewsMap.Updates;

namespace SheetViewsMap
{
    /// <summary>
    /// Окно «Настройки»: версия плагина и проверка обновлений с GitHub Releases.
    ///
    /// Найденное обновление скачивается во staging-папку, после чего запускается
    /// внешний апдейтер: он дожидается закрытия Revit и сам подменяет файлы
    /// плагина. Пока Revit открыт, подменять файлы нельзя.
    /// </summary>
    public sealed class SettingsWindow : Window
    {
        private readonly UpdateService _updateService = new UpdateService();
        private ReleaseInfo _pendingRelease;

        private readonly TextBlock _statusText;
        private readonly ProgressBar _progress;
        private readonly Button _checkButton;
        private readonly Button _downloadButton;
        private readonly TextBlock _notesText;
        private readonly ScrollViewer _notesScroll;

        internal SettingsWindow(int revitYear)
        {
            Title = "Настройки — Sheet Views Map";
            Width = 640;
            MinWidth = 520;
            MinHeight = 320;
            SizeToContent = SizeToContent.Height;
            Background = Theme.Background;
            WindowStartupLocation = WindowStartupLocation.Manual;
            ShowInTaskbar = false;

            var root = new StackPanel { Margin = new Thickness(16) };

            // ---- версия ----
            string version = "Версия плагина: v" + _updateService.GetCurrentVersion();
            if (revitYear > 0)
                version += "   |   Revit " + revitYear;

            root.Children.Add(Theme.CreateText(version, Theme.TextStrong, Theme.FontUi, true));
            root.Children.Add(Theme.CreateText(
                "Обновление скачивается с GitHub и применится автоматически после закрытия Revit.",
                Theme.TextMuted, Theme.FontSmall, false));

            // ---- кнопки: проверить обновления и страница релизов ----
            var checkRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 12, 0, 0)
            };

            _checkButton = Theme.CreateButton("Проверить обновления", true);
            _checkButton.Width = 190;
            _checkButton.Click += OnCheckClick;
            checkRow.Children.Add(_checkButton);

            var releasesButton = Theme.CreateButton("Страница релизов", false);
            releasesButton.Width = 160;
            releasesButton.Margin = new Thickness(8, 0, 0, 0);
            releasesButton.ToolTip = "Открыть список релизов на GitHub в браузере";
            releasesButton.Click += OnOpenReleasesClick;
            checkRow.Children.Add(releasesButton);

            root.Children.Add(checkRow);

            // ---- статус ----
            _statusText = Theme.CreateText(
                "Нажмите «Проверить обновления», чтобы узнать о новых версиях.",
                Theme.Text, Theme.FontSmall, false);
            _statusText.Margin = new Thickness(0, 10, 0, 0);
            _statusText.TextWrapping = TextWrapping.Wrap;
            root.Children.Add(_statusText);

            // ---- прогресс (проверка - «бесконечный», скачивание - с процентами) ----
            _progress = new ProgressBar
            {
                Height = 6,
                Margin = new Thickness(0, 10, 0, 0),
                Foreground = Theme.Accent,
                Background = Theme.Surface,
                BorderThickness = new Thickness(0),
                Visibility = Visibility.Collapsed
            };
            root.Children.Add(_progress);

            // ---- описание найденного релиза ----
            _notesText = Theme.CreateText(string.Empty, Theme.Text, Theme.FontSmall, false);
            _notesText.TextWrapping = TextWrapping.Wrap;

            _notesScroll = new ScrollViewer
            {
                MaxHeight = 220,
                Margin = new Thickness(0, 10, 0, 0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _notesText,
                Visibility = Visibility.Collapsed
            };
            root.Children.Add(_notesScroll);

            // ---- кнопка скачивания ----
            _downloadButton = Theme.CreateButton("Скачать и установить", true);
            _downloadButton.Width = 220;
            _downloadButton.Margin = new Thickness(0, 10, 0, 0);
            _downloadButton.Visibility = Visibility.Collapsed;
            _downloadButton.Click += OnDownloadClick;
            root.Children.Add(_downloadButton);

            Content = root;
        }

        // ------------------------------------------------------------------ проверка обновлений

        private async void OnCheckClick(object sender, RoutedEventArgs e)
        {
            _pendingRelease = null;
            _downloadButton.Visibility = Visibility.Collapsed;
            _notesScroll.Visibility = Visibility.Collapsed;
            SetStatus("Проверяем обновления…");

            _checkButton.IsEnabled = false;
            _progress.IsIndeterminate = true;
            _progress.Visibility = Visibility.Visible;

            try
            {
                ReleaseInfo release = await _updateService.CheckForUpdateAsync();

                if (release == null)
                {
                    SetStatus("У вас установлена последняя версия.");
                }
                else
                {
                    _pendingRelease = release;
                    SetStatus("Доступна новая версия: v" + release.Version + ".");
                    _notesText.Text = release.ReleaseNotes;
                    _notesScroll.Visibility = Visibility.Visible;
                    _downloadButton.Content = "Скачать и установить v" + release.Version;
                    _downloadButton.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                SetStatus("Не удалось проверить обновления: " + ex.Message, true);
            }
            finally
            {
                _progress.IsIndeterminate = false;
                _progress.Visibility = Visibility.Collapsed;
                _checkButton.IsEnabled = true;
            }
        }

        // ------------------------------------------------------------------ скачивание

        private async void OnDownloadClick(object sender, RoutedEventArgs e)
        {
            ReleaseInfo release = _pendingRelease;
            if (release == null)
                return;

            _checkButton.IsEnabled = false;
            _downloadButton.IsEnabled = false;
            _downloadButton.Content = "Скачивание…";

            _progress.IsIndeterminate = false;
            _progress.Value = 0;
            _progress.Visibility = Visibility.Visible;

            try
            {
                var progress = new Progress<double>(value => _progress.Value = value * 100);
                await _updateService.DownloadAndStageAsync(release, progress);
                _updateService.LaunchUpdater();

                SetStatus("Обновление v" + release.Version + " скачано. " +
                          "Закройте Revit — оно применится автоматически при выходе.");
                _downloadButton.Visibility = Visibility.Collapsed;
                _notesScroll.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                SetStatus("Не удалось скачать обновление: " + ex.Message, true);
                _downloadButton.Content = "Скачать и установить v" + release.Version;
            }
            finally
            {
                _progress.Visibility = Visibility.Collapsed;
                _checkButton.IsEnabled = true;
                _downloadButton.IsEnabled = true;
            }
        }

        // ------------------------------------------------------------------ страница релизов

        private void OnOpenReleasesClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = UpdateService.ReleasesPage,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                SetStatus("Не удалось открыть браузер: " + ex.Message, true);
            }
        }

        private void SetStatus(string text, bool isError = false)
        {
            _statusText.Text = text;
            _statusText.Foreground = isError ? Theme.AccentPressed : Theme.Text;
        }
    }
}
