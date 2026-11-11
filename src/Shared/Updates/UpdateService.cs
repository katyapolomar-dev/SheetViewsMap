using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SheetViewsMap.Updates
{
    /// <summary>Файл обновления в релизе: год Revit и адрес скачивания.</summary>
    public class ReleaseAsset
    {
        public int Year { get; set; }
        public string Url { get; set; }
        public long Size { get; set; }
    }

    /// <summary>Найденный релиз, более новый, чем установленная версия.</summary>
    public class ReleaseInfo
    {
        public string Version { get; set; }
        public string TagName { get; set; }
        public string ReleaseNotes { get; set; }
        public List<ReleaseAsset> Assets { get; set; }
    }

    /// <summary>
    /// Проверка обновлений и подготовка файлов обновления.
    ///
    /// Схема: GitHub Releases → скачивание zip по годам во staging-папку →
    /// маркер-файл → внешний апдейтер (SheetViewsMap.Updater.exe) дожидается
    /// закрытия Revit и подменяет DLL в %LOCALAPPDATA%\SheetViewsMap\&lt;год&gt;\.
    /// Подменить файлы изнутри плагина нельзя — Revit держит их заблокированными.
    /// </summary>
    internal class UpdateService
    {
        private const string GitHubOwner = "katyapolomar-dev";
        private const string GitHubRepo = "SheetViewsMap";
        private const string ReleasesApiUrl =
            "https://api.github.com/repos/" + GitHubOwner + "/" + GitHubRepo + "/releases?per_page=20";

        /// <summary>Ассет обновления: SheetViewsMap-2021.zip, SheetViewsMap-2025.zip и т.д.</summary>
        private static readonly Regex AssetNameRegex =
            new Regex(@"^SheetViewsMap-(\d{4})\.zip$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private const string UpdaterFileName = "SheetViewsMap.Updater.exe";

        private static readonly int[] SupportedYears = { 2021, 2025 };

        private static readonly string RootDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SheetViewsMap");
        private static readonly string StagingDir = Path.Combine(RootDir, "staging");
        private static readonly string MarkerPath = Path.Combine(RootDir, "update-pending.txt");

        private readonly HttpClient _httpClient;

        public UpdateService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "SheetViewsMap-UpdateService");
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        }

        /// <summary>Адрес страницы релизов на GitHub.</summary>
        internal static string ReleasesPage
        {
            get { return "https://github.com/" + GitHubOwner + "/" + GitHubRepo + "/releases"; }
        }

        // ------------------------------------------------------------------ версия

        /// <summary>Версия плагина из сборки (задаётся в Version.txt при сборке).</summary>
        public string GetCurrentVersion()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            AssemblyInformationalVersionAttribute informational =
                assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (informational != null && !string.IsNullOrWhiteSpace(informational.InformationalVersion))
                return informational.InformationalVersion.Split('+')[0].Trim();

            Version assemblyVersion = assembly.GetName().Version;
            return assemblyVersion == null
                ? "0.0.0"
                : string.Format("{0}.{1}.{2}", assemblyVersion.Major, assemblyVersion.Minor, assemblyVersion.Build);
        }

        /// <summary>Год Revit, из папки которого загружен плагин (...SheetViewsMap\2021\...). 0 — не удалось определить.</summary>
        public static int GetRevitYear()
        {
            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string name = Path.GetFileName((dir ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar));

                int year;
                if (name != null && name.Length == 4 && int.TryParse(name, out year))
                    return year;
            }
            catch (ArgumentException)
            {
            }

            return 0;
        }

        // ------------------------------------------------------------------ проверка

        /// <summary>
        /// Ищет самый новый стабильный релиз (без draft/prerelease) с версией
        /// выше установленной. null — обновлений нет.
        /// </summary>
        public Task<ReleaseInfo> CheckForUpdateAsync()
        {
            return Task.Run(async () =>
            {
                string currentVersion = GetCurrentVersion();

                using (HttpResponseMessage response = await _httpClient.GetAsync(ReleasesApiUrl).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        throw BuildApiException(response);

                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return ParseLatestStableRelease(json, currentVersion);
                }
            });
        }

        private static Exception BuildApiException(HttpResponseMessage response)
        {
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                IEnumerable<string> remaining;
                if (response.Headers.TryGetValues("X-RateLimit-Remaining", out remaining) &&
                    remaining.FirstOrDefault() == "0")
                {
                    return new InvalidOperationException(
                        "Превышен лимит обращений к GitHub. Попробуйте позже (примерно через час).");
                }

                return new InvalidOperationException("GitHub отклонил запрос (403). Попробуйте позже.");
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
                return new InvalidOperationException("Релизы не найдены: " + GitHubOwner + "/" + GitHubRepo + ".");

            return new InvalidOperationException(
                "GitHub вернул ошибку " + (int)response.StatusCode + " " + response.StatusCode + ".");
        }

        // ------------------------------------------------------------------ скачивание

        /// <summary>
        /// Скачивает zip-ассеты для всех установленных годов плагина,
        /// распаковывает их во staging и создаёт маркер для апдейтера.
        /// </summary>
        public async Task DownloadAndStageAsync(ReleaseInfo release, IProgress<double> progress)
        {
            List<ReleaseAsset> downloads = CollectTargetAssets(release);

            if (Directory.Exists(StagingDir))
            {
                try
                {
                    Directory.Delete(StagingDir, true);
                }
                catch (IOException)
                {
                }
            }
            Directory.CreateDirectory(StagingDir);

            long totalBytes = downloads.Sum(a => Math.Max(a.Size, 0));
            long downloadedBytes = 0;
            var buffer = new byte[81920];

            foreach (ReleaseAsset asset in downloads)
            {
                string zipPath = Path.Combine(StagingDir, asset.Year + ".zip");

                using (HttpResponseMessage fileResponse =
                       await _httpClient.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    fileResponse.EnsureSuccessStatusCode();

                    using (Stream contentStream = await fileResponse.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (FileStream fileStream = File.Create(zipPath))
                    {
                        int read;
                        while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                            downloadedBytes += read;
                            if (progress != null && totalBytes > 0)
                                progress.Report((double)downloadedBytes / totalBytes);
                        }
                    }
                }

                string extractDir = Path.Combine(StagingDir, asset.Year.ToString());
                if (Directory.Exists(extractDir))
                    Directory.Delete(extractDir, true);

                ZipFile.ExtractToDirectory(zipPath, extractDir);
                File.Delete(zipPath);
            }

            WriteMarker(release, downloads);
        }

        private List<ReleaseAsset> CollectTargetAssets(ReleaseInfo release)
        {
            List<int> years = CollectTargetYears();
            var downloads = new List<ReleaseAsset>();

            foreach (int year in years)
            {
                ReleaseAsset asset = release.Assets.FirstOrDefault(a => a.Year == year);
                if (asset == null)
                    throw new InvalidOperationException(
                        "В релизе v" + release.Version + " нет файла обновления для Revit " + year +
                        " (SheetViewsMap-" + year + ".zip).");

                downloads.Add(asset);
            }

            return downloads;
        }

        /// <summary>
        /// Годы, которые нужно обновить: год запущенного Revit плюс все годы,
        /// для которых плагин уже установлен в %LOCALAPPDATA%\SheetViewsMap.
        /// </summary>
        private List<int> CollectTargetYears()
        {
            var years = new List<int>();

            int currentYear = GetRevitYear();
            if (currentYear > 0 && SupportedYears.Contains(currentYear))
                years.Add(currentYear);

            try
            {
                if (Directory.Exists(RootDir))
                {
                    foreach (string dir in Directory.GetDirectories(RootDir))
                    {
                        string name = Path.GetFileName(dir);

                        int year;
                        if (name != null && name.Length == 4 && int.TryParse(name, out year) &&
                            SupportedYears.Contains(year) && !years.Contains(year) &&
                            File.Exists(Path.Combine(dir, "SheetViewsMap" + year + ".dll")))
                        {
                            years.Add(year);
                        }
                    }
                }
            }
            catch (IOException)
            {
            }

            if (years.Count == 0)
                throw new InvalidOperationException(
                    "Не удалось определить ни одной установленной версии плагина для обновления.");

            return years.OrderBy(y => y).ToList();
        }

        private void WriteMarker(ReleaseInfo release, List<ReleaseAsset> downloads)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# SheetViewsMap pending update");
            builder.AppendLine("Version=" + release.Version);

            foreach (ReleaseAsset asset in downloads)
            {
                string staging = Path.Combine(StagingDir, asset.Year.ToString());
                if (!Directory.Exists(staging))
                    continue;

                builder.AppendLine("[Artifact]");
                builder.AppendLine("StagingDir=" + staging);
                builder.AppendLine("TargetDir=" + Path.Combine(RootDir, asset.Year.ToString()));
            }

            Directory.CreateDirectory(RootDir);
            File.WriteAllText(MarkerPath, builder.ToString(), new UTF8Encoding(false));
        }

        // ------------------------------------------------------------------ апдейтер

        /// <summary>Путь к SheetViewsMap.Updater.exe: в корне папки плагина или рядом с DLL.</summary>
        public string FindUpdaterExe()
        {
            string installedPath = Path.Combine(RootDir, UpdaterFileName);
            if (File.Exists(installedPath))
                return installedPath;

            try
            {
                string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string rootFromPlugin = Path.GetDirectoryName((pluginDir ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar));
                if (rootFromPlugin != null)
                {
                    string candidate = Path.Combine(rootFromPlugin, UpdaterFileName);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
            catch (ArgumentException)
            {
            }

            return null;
        }

        /// <summary>Запускает апдейтер: он применит обновление после выхода Revit.</summary>
        public void LaunchUpdater()
        {
            string updaterPath = FindUpdaterExe();
            if (updaterPath == null)
                throw new InvalidOperationException(
                    "Не найден файл " + UpdaterFileName + ". Переустановите плагин установщиком.");

            Process.Start(new ProcessStartInfo
            {
                FileName = updaterPath,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }

        // ------------------------------------------------------------------ разбор ответа GitHub

        private ReleaseInfo ParseLatestStableRelease(string json, string currentVersion)
        {
            List<GitHubRelease> parsed;

            try
            {
                // Newtonsoft сопоставляет имена полей GitHub API со свойствами
                // без учёта регистра - атрибуты не нужны.
                parsed = JsonConvert.DeserializeObject<List<GitHubRelease>>(json) ?? new List<GitHubRelease>();
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Не удалось разобрать ответ GitHub: " + ex.Message, ex);
            }

            GitHubRelease[] releases = parsed.ToArray();

            string latestVersion = null;
            GitHubRelease latest = null;

            foreach (GitHubRelease release in releases)
            {
                if (release == null || release.draft || release.prerelease)
                    continue;

                string version = (release.tag_name ?? string.Empty).Trim().TrimStart('v', 'V');
                if (version.Length == 0 || PluginVersion.Compare(version, currentVersion) <= 0)
                    continue;

                if (latestVersion == null || PluginVersion.Compare(version, latestVersion) > 0)
                {
                    latestVersion = version;
                    latest = release;
                }
            }

            if (latest == null)
                return null;

            var assets = new List<ReleaseAsset>();
            if (latest.assets != null)
            {
                foreach (GitHubAsset asset in latest.assets)
                {
                    if (asset == null || string.IsNullOrEmpty(asset.name) || string.IsNullOrEmpty(asset.browser_download_url))
                        continue;

                    Match match = AssetNameRegex.Match(asset.name);
                    if (!match.Success)
                        continue;

                    int year;
                    if (!int.TryParse(match.Groups[1].Value, out year))
                        continue;

                    assets.Add(new ReleaseAsset
                    {
                        Year = year,
                        Url = asset.browser_download_url,
                        Size = asset.size
                    });
                }
            }

            return new ReleaseInfo
            {
                Version = latestVersion,
                TagName = string.IsNullOrEmpty(latest.tag_name) ? "v" + latestVersion : latest.tag_name,
                ReleaseNotes = string.IsNullOrWhiteSpace(latest.body)
                    ? "(описание релиза отсутствует)"
                    : latest.body.Trim(),
                Assets = assets
            };
        }

        /// <summary>Имена полей совпадают с ответом GitHub API — JavaScriptSerializer сопоставляет их без учёта регистра.</summary>
        internal class GitHubRelease
        {
            public string tag_name { get; set; }
            public bool draft { get; set; }
            public bool prerelease { get; set; }
            public string body { get; set; }
            public GitHubAsset[] assets { get; set; }
        }

        internal class GitHubAsset
        {
            public string name { get; set; }
            public long size { get; set; }
            public string browser_download_url { get; set; }
        }
    }
}
