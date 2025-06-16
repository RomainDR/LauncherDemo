using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Core;
using LauncherDemo;
using LauncherDemo.Core;
using LauncherDemo.Core.API;
using Newtonsoft.Json.Linq;

namespace LauncherDemo
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private LauncherCore core;
        private ApiCore api;

        public MainWindow()
        {
            InitializeComponent();

            api = new ApiCore();
            core = new LauncherCore(this, api);
            core.InitializeLauncher();
        }

        private void ButtonLaunch_OnClick(object sender, RoutedEventArgs e)
        {
            core.Launch();
        }
    }


    namespace Core.API
    {
        public class ApiFile
        {
            private readonly string _baseUrl = "https://practical-hypatia.185-229-202-10.plesk.page";
            private readonly string? _name;
            private readonly long _size;
            private readonly string? _downloadUrl;


            public ApiFile()
            {

            }

            public string? Name { get; set; }
            public long Size { get; set; }
            public string? DownloadUrl { get; set; }
        }

        public class ApiCore
        {
            private Dictionary<string, long> LocalFiles = new();
            private long _downloaded = 0;
            private long _totalDownload = 0;

            public Action<float> PercentageUpdated { get; set; }

            public async Task<bool> IsConnected()
            {
                try
                {
                    string url = "https://practical-hypatia.185-229-202-10.plesk.page/api/status";
                    string? json = await GetJsonFromUrl(url);

                    if (json == null) return false;
                    // Parse and use the JSON data
                    JObject data = JObject.Parse(json);
                    int? status = data["code_return"]?.Value<int>();
                    return status == 200;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error: {ex.Message}");
                    return false;
                }
            }

            private static async Task<string?> GetJsonFromUrl(string url)
            {
                try
                {
                    using var client = new HttpClient();
                    var response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();
                    var responseBody = await response.Content.ReadAsStringAsync();
                    return responseBody;
                }
                catch (HttpRequestException ex)
                {
                    Console.WriteLine($"Error fetching data: {ex.Message}");
                    return null;
                }
            }

            private async Task<long> GetDirectorySize(string folderPath)
            {
                if (!Directory.Exists(folderPath))
                    return 0;

                long totalSize = 0;
                var files = Directory.GetFiles(folderPath);

                foreach (var file in files)
                {
                    FileInfo fileInfo = new FileInfo(file);
                    totalSize += fileInfo.Length;
                }

                var subDirectories = Directory.GetDirectories(folderPath);
                foreach (var dir in subDirectories)
                {
                    totalSize += await GetDirectorySize(dir);
                }

                return totalSize;
            }

            private async Task<Dictionary<string, long>> GetAllItemsWithSize(string folderPath)
            {
                var items = new Dictionary<string, long>();

                if (!Directory.Exists(folderPath))
                    return items;

                foreach (var file in Directory.GetFiles(folderPath))
                {
                    FileInfo fi = new FileInfo(file);
                    items.Add(file, fi.Length);
                }

                foreach (var dir in Directory.GetDirectories(folderPath))
                {
                    long dirSize = await GetDirectorySize(dir);
                    items.Add(dir, dirSize);
                }

                return items;
            }

            public async Task<bool> IsUpdated()
            {
                string url = "https://practical-hypatia.185-229-202-10.plesk.page/api/files";
                string localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

                string folder = Path.Combine(localAppDataPath, ".LauncherDemo");

                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                bool isUpdated = false;

                LocalFiles = await GetAllItemsWithSize(folder);
                foreach (var keyValuePair in LocalFiles)
                    Console.WriteLine($"{keyValuePair.Key}");

                var jsonStr = await GetJsonFromUrl(url);
                if (jsonStr == null) return isUpdated;

                using JsonDocument doc = JsonDocument.Parse(jsonStr);
                JsonElement root = doc.RootElement;
                isUpdated = LocalFiles.Sum(x => x.Value) ==
                            root.GetProperty("statistics").GetProperty("total_size").GetInt64();

                if (!isUpdated)
                    Console.WriteLine(
                        $"Not equals. Sum {LocalFiles.Sum(x => x.Value)}/Total: {root.GetProperty("statistics").GetProperty("total_size").GetInt64()}");
                return isUpdated;
            }

            public async Task UpdateGame()
            {
                string url = "https://practical-hypatia.185-229-202-10.plesk.page/api/files";
                string baseUrl = "https://practical-hypatia.185-229-202-10.plesk.page";
                
                string localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string folder = Path.Combine(localAppDataPath, ".LauncherDemo");

                
                var jsonStr = await GetJsonFromUrl(url);
                if (jsonStr == null) return;

                using JsonDocument doc = JsonDocument.Parse(jsonStr);
                JsonElement root = doc.RootElement;
                List<ApiFile> apiFiles = root.GetProperty("files")
                    .EnumerateArray()
                    .Select(fileElement =>
                    {
                        var fullDownloadUrl = fileElement.GetProperty("download_url").GetString();
                        var shortUrl = fullDownloadUrl?[(fullDownloadUrl.IndexOf('/')+1)..];

                        return new ApiFile
                        {
                            Name = fileElement.GetProperty("name").GetString(),
                            Size = fileElement.GetProperty("size").GetInt64(),
                            DownloadUrl = baseUrl + "/" + shortUrl
                        };
                    })
                    .ToList();


                foreach (var keyValuePair in from keyValuePair in LocalFiles
                         let match = apiFiles.Find(f => Path.GetFileName(keyValuePair.Key) == f.Name)
                         where match == null
                         select keyValuePair)
                {
                    Console.WriteLine(
                        $"File {keyValuePair.Key.Substring(keyValuePair.Key.LastIndexOf("\\", StringComparison.Ordinal))} isn't not valid in folder of game. Deleting file...");
                    File.Delete(keyValuePair.Key);
                }
                
                apiFiles = apiFiles.Where(apiFile =>
                {
                    if (apiFile.Name == null) return false;
                    string localPath = Path.Combine(folder, apiFile.Name);
                    return !File.Exists(localPath) || new FileInfo(localPath).Length != apiFile.Size;
                }).ToList();

                _downloaded = 0;
                _totalDownload = apiFiles.Sum(f => f.Size);
                
                foreach (var apiFile in apiFiles)
                {
                    if (apiFile.DownloadUrl != null) 
                        await DownloadFileWithProgress(apiFile.DownloadUrl, folder + "/" + apiFile.Name);
                }
            }

            
            
            private async Task DownloadFileWithProgress(string url, string destinationPath)
            {
                try
                {
                    Console.WriteLine($"Download: {url}");
                    using HttpClient client = new HttpClient();

                    using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    await using var contentStream = await response.Content.ReadAsStreamAsync();
                    await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write,
                        FileShare.None, 8192, true);

                    var buffer = new byte[8192];
                    int read;

                    while ((read = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, read));
                        _downloaded += read;
                            PercentageUpdated.Invoke(_downloaded / (float)_totalDownload * 100f);
                    }

                    Console.WriteLine("\nTéléchargement terminé.");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Failed to fetch data: {e.Message}");
                }
            }
        }

    }
}


namespace Core
{
    public class LauncherCore(MainWindow window, ApiCore api)
    {

        private const string Available = "Connected";
        private const string Unavailable = "Disconnected";
        private readonly SolidColorBrush _greenBrush = new(Color.FromRgb(0, 255, 0));
        private readonly SolidColorBrush _redBrush = new(Color.FromRgb(255, 0, 0));

        public async void InitializeLauncher()
        {
            try
            {
                bool isConnected = await api.IsConnected();
                if (isConnected)
                {
                    window.AvailableStatus.Text = Available;
                    window.AvailableStatus.Foreground = _greenBrush;
                }
                else
                {
                    window.AvailableStatus.Text = Unavailable;
                    window.AvailableStatus.Foreground = _redBrush;
                }
                window.PercentageUpdatedText.Visibility = Visibility.Hidden;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error fetching data: {e.Message}");
            }
        }

        public async Task Launch()
        {
            bool isUpdated = await api.IsUpdated();
            if (!isUpdated)
            {
                api.PercentageUpdated += UpdateProgressbar;
                await api.UpdateGame();
            }
        }

        private void UpdateProgressbar(float percentage)
        {
            Console.WriteLine($"{percentage}");
            window.PercentageUpdatedText.Visibility = Visibility.Visible;
            window.LaunchButton.Visibility = Visibility.Collapsed;
            window.ProgressBarLaunch.Visibility = Visibility.Visible;
            
            window.ProgressBarLaunch.Value = percentage;
            window.PercentageUpdatedText.Text = Math.Round(percentage, 1).ToString("0.0") + "%";
        }
    }
}