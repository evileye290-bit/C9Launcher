using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HtmlAgilityPack;

namespace C9Launcher
{
    public partial class MainWindow : Window
    {
        private readonly string bannersFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Banners");
        private readonly string localVersionFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version.txt");

        // URL server
        private readonly string remoteVersionUrl = "http://127.0.0.1/api/get_version.php";
        private readonly string patchDownloadUrl = "http://127.0.0.1/patchC9/latest_patch.zip";
        private readonly string hashApiUrl = "http://127.0.0.1/api/get_hashes.php";
        private readonly string downloadBaseUrl = "http://127.0.0.1/patchC9/";
        private readonly string newsPageUrl = "https://c9-hd.com/news.php";

        private string currentVersion = "1.0.0";
        private string remoteLatestVersion = "";

        private readonly List<NewsItem> newsItems = new();
        private int currentNewsIndex = 0;
        private DispatcherTimer? newsTimer;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadBannerImage();
            await CheckVersionAsync();
            await LoadNewsSliderAsync();
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            if (newsTimer != null)
            {
                newsTimer.Stop();
                newsTimer.Tick -= NewsTimer_Tick;
            }
        }

        private class NewsItem
        {
            public string Category { get; set; } = "";
            public string Title { get; set; } = "";
            public string DateText { get; set; } = "";
            public string Summary { get; set; } = "";
            public string ImageUrl { get; set; } = "";
            public string LinkUrl { get; set; } = "";
        }

        // --- 1. โหลดรูปภาพพื้นหลัง/Banner ---
        private void LoadBannerImage()
        {
            try
            {
                if (!Directory.Exists(bannersFolder))
                    Directory.CreateDirectory(bannersFolder);

                var images = Directory.GetFiles(bannersFolder, "*.*")
                    .Where(s => s.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                s.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (images.Length > 0)
                {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(images[0]);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    BannerImage.Source = bitmap;
                }
            }
            catch
            {
            }
        }

        // --- 2. เช็กเวอร์ชันเกม ---
        private async Task CheckVersionAsync()
        {
            if (File.Exists(localVersionFile))
                currentVersion = File.ReadAllText(localVersionFile).Trim();
            else
                File.WriteAllText(localVersionFile, currentVersion);

            TxtVersion.Text = $"Version: {currentVersion}";

            try
            {
                using HttpClient client = new HttpClient();
                remoteLatestVersion = (await client.GetStringAsync(remoteVersionUrl)).Trim();

                if (currentVersion != remoteLatestVersion)
                {
                    TxtVersion.Text = $"Version: {currentVersion} (Update: {remoteLatestVersion})";
                    BtnStart.Content = "UPDATE";
                    BtnStart.Background = new SolidColorBrush(Colors.Orange);
                }
            }
            catch
            {
                TxtVersion.Text = $"Version: {currentVersion} (Offline)";
            }
        }

        // --- 3. โหลดข่าวและเริ่ม slider ---
        private async Task LoadNewsSliderAsync()
        {
            try
            {
                using HttpClient client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");

                string html = await client.GetStringAsync(newsPageUrl);
                List<NewsItem> parsed = ParseNewsFromHtml(html);

                newsItems.Clear();
                newsItems.AddRange(parsed.Take(5));

                if (newsItems.Count == 0)
                {
                    TxtNewsCategory.Text = "NEWS";
                    TxtNewsTitle.Text = "ไม่พบข่าว";
                    TxtNewsDate.Text = "";
                    TxtNewsSummary.Text = "";
                    TxtNewsIndex.Text = "";
                    NewsImage.Source = null;
                    return;
                }

                currentNewsIndex = 0;
                ShowNews(newsItems[currentNewsIndex]);
                UpdateNewsIndicator();

                if (newsItems.Count > 1)
                {
                    newsTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(5)
                    };
                    newsTimer.Tick += NewsTimer_Tick;
                    newsTimer.Start();
                }
            }
            catch (Exception ex)
            {
                TxtNewsCategory.Text = "NEWS";
                TxtNewsTitle.Text = "โหลดข่าวไม่สำเร็จ";
                TxtNewsDate.Text = "";
                TxtNewsSummary.Text = ex.Message;
                TxtNewsIndex.Text = "";
                NewsImage.Source = null;
            }
        }

        private void NewsTimer_Tick(object? sender, EventArgs e)
        {
            if (newsItems.Count == 0)
                return;

            currentNewsIndex++;
            if (currentNewsIndex >= newsItems.Count)
                currentNewsIndex = 0;

            ShowNews(newsItems[currentNewsIndex]);
            UpdateNewsIndicator();
        }

        // --- 4. Parse ข่าวจาก HTML ของ news.php ---
        private List<NewsItem> ParseNewsFromHtml(string html)
        {
            List<NewsItem> items = new List<NewsItem>();

            HtmlDocument doc = new HtmlDocument();
            doc.LoadHtml(html);

            var newsNodes = doc.DocumentNode.SelectNodes("//div[contains(@class,'news-item')]");
            if (newsNodes == null)
                return items;

            foreach (var node in newsNodes)
            {
                string category = "";
                string title = "";
                string dateText = "";
                string summary = "";
                string imageUrl = "";

                var categoryNode = node.SelectSingleNode(".//div[contains(@class,'news-badge')]");
                if (categoryNode != null)
                    category = WebUtility.HtmlDecode(categoryNode.InnerText.Trim());

                var titleNode = node.SelectSingleNode(".//h3[contains(@class,'news-title')]");
                if (titleNode != null)
                    title = WebUtility.HtmlDecode(titleNode.InnerText.Trim());

                var dateNode = node.SelectSingleNode(".//div[contains(@class,'news-date')]");
                if (dateNode != null)
                {
                    dateText = WebUtility.HtmlDecode(dateNode.InnerText.Trim());
                    dateText = dateText.Replace("", "").Trim();
                }

                var summaryNode = node.SelectSingleNode(".//div[contains(@class,'news-desc')]");
                if (summaryNode != null)
                {
                    summary = WebUtility.HtmlDecode(summaryNode.InnerText.Trim());
                    summary = summary.Replace("\r", " ").Replace("\n", " ").Trim();
                }

                var imageNode = node.SelectSingleNode(".//div[contains(@class,'news-img-box')]//img");
                if (imageNode != null)
                {
                    string src = imageNode.GetAttributeValue("src", "").Trim();
                    if (!string.IsNullOrWhiteSpace(src))
                        imageUrl = MakeAbsoluteUrl(newsPageUrl, src);
                }

                if (string.IsNullOrWhiteSpace(title))
                    continue;

                items.Add(new NewsItem
                {
                    Category = string.IsNullOrWhiteSpace(category) ? "NEWS" : category.ToUpperInvariant(),
                    Title = title,
                    DateText = dateText,
                    Summary = summary,
                    ImageUrl = imageUrl,
                    LinkUrl = newsPageUrl
                });
            }

            return items;
        }

        private static string MakeAbsoluteUrl(string baseUrl, string inputUrl)
        {
            if (string.IsNullOrWhiteSpace(inputUrl))
                return "";

            if (Uri.TryCreate(inputUrl, UriKind.Absolute, out Uri? absoluteUri))
                return absoluteUri.ToString();

            Uri baseUri = new Uri(baseUrl);
            return new Uri(baseUri, inputUrl).ToString();
        }

        // --- 5. แสดงข่าวบนการ์ด ---
        private void ShowNews(NewsItem item)
        {
            TxtNewsCategory.Text = string.IsNullOrWhiteSpace(item.Category) ? "NEWS" : item.Category;
            TxtNewsTitle.Text = item.Title;
            TxtNewsDate.Text = item.DateText;
            TxtNewsSummary.Text = item.Summary;
            NewsCard.Tag = item.LinkUrl;

            NewsImage.Source = null;

            if (!string.IsNullOrWhiteSpace(item.ImageUrl))
            {
                try
                {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(item.ImageUrl, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    NewsImage.Source = bitmap;
                }
                catch
                {
                    NewsImage.Source = null;
                }
            }
        }

        private void UpdateNewsIndicator()
        {
            if (newsItems.Count == 0)
            {
                TxtNewsIndex.Text = "";
                return;
            }

            TxtNewsIndex.Text = $"{currentNewsIndex + 1}/{newsItems.Count}";
        }

        private void NewsCard_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            string url = NewsCard.Tag?.ToString() ?? newsPageUrl;
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        private void BtnPrevNews_Click(object sender, RoutedEventArgs e)
        {
            if (newsItems.Count == 0)
                return;

            currentNewsIndex--;
            if (currentNewsIndex < 0)
                currentNewsIndex = newsItems.Count - 1;

            ShowNews(newsItems[currentNewsIndex]);
            UpdateNewsIndicator();
            RestartNewsTimer();
        }

        private void BtnNextNews_Click(object sender, RoutedEventArgs e)
        {
            if (newsItems.Count == 0)
                return;

            currentNewsIndex++;
            if (currentNewsIndex >= newsItems.Count)
                currentNewsIndex = 0;

            ShowNews(newsItems[currentNewsIndex]);
            UpdateNewsIndicator();
            RestartNewsTimer();
        }

        private void RestartNewsTimer()
        {
            if (newsTimer != null)
            {
                newsTimer.Stop();
                newsTimer.Start();
            }
        }

        // --- 6. ปุ่ม social ---
        private void BtnSocial_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn &&
                btn.Tag is string url &&
                !string.IsNullOrWhiteSpace(url))
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
        }

        // --- 7. ปุ่ม options ---
        private void BtnOption_Click(object sender, RoutedEventArgs e)
        {
            string optionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "C9ConfigGameOn.exe");

            if (File.Exists(optionPath))
                Process.Start(new ProcessStartInfo(optionPath) { UseShellExecute = true });
            else
                MessageBox.Show("ไม่พบไฟล์ C9ConfigGameOn.exe ในโฟลเดอร์เกม", "Error");
        }

        // --- 8. ปุ่ม start / update ---
        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if ((BtnStart.Content?.ToString() ?? "") == "UPDATE")
            {
                BtnStart.IsEnabled = false;
                if (BtnRepair != null) BtnRepair.IsEnabled = false;

                await DownloadAndApplyPatchAsync();
                return;
            }

            string gamePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "c9.exe");

            if (File.Exists(gamePath))
            {
                Process.Start(new ProcessStartInfo(gamePath) { UseShellExecute = true });
                Close();
            }
            else
            {
                MessageBox.Show("ไม่พบตัวเข้าเกม (c9.exe)", "Error");
            }
        }

        // --- 9. ดาวน์โหลดแพตช์ ZIP ---
        private async Task DownloadAndApplyPatchAsync()
        {
            string tempZipPath = Path.Combine(Path.GetTempPath(), "C9_patch.zip");

            try
            {
                UpdateProgressBar.Visibility = Visibility.Visible;
                TxtProgress.Visibility = Visibility.Visible;
                UpdateProgressBar.Value = 0;
                BtnStart.Content = "DOWNLOADING...";

                using (HttpClient client = new HttpClient())
                using (HttpResponseMessage response = await client.GetAsync(patchDownloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    long? totalBytes = response.Content.Headers.ContentLength;

                    using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                    using (FileStream fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        byte[] buffer = new byte[8192];
                        long totalRead = 0;
                        int read;

                        while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, read);
                            totalRead += read;

                            if (totalBytes.HasValue && totalBytes.Value > 0)
                            {
                                double progress = (double)totalRead / totalBytes.Value * 100;
                                UpdateProgressBar.Value = progress;
                                TxtProgress.Text = $"{progress:F1}%";
                            }
                        }
                    }
                }

                if (!File.Exists(tempZipPath))
                    throw new Exception("ดาวน์โหลดแพทช์ไม่สำเร็จ");

                BtnStart.Content = "EXTRACTING...";
                TxtProgress.Text = "กำลังติดตั้งแพทช์...";

                await Task.Run(() =>
                {
                    ZipFile.ExtractToDirectory(tempZipPath, AppDomain.CurrentDomain.BaseDirectory, true);
                });

                File.Delete(tempZipPath);
                File.WriteAllText(localVersionFile, remoteLatestVersion);
                currentVersion = remoteLatestVersion;
                TxtVersion.Text = $"Version: {currentVersion}";

                string newLauncherPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "C9Launcher_New.exe");
                if (File.Exists(newLauncherPath))
                {
                    UpdateLauncherSelf();
                    return;
                }

                UpdateProgressBar.Visibility = Visibility.Hidden;
                TxtProgress.Visibility = Visibility.Hidden;
                BtnStart.Content = "START GAME";
                BtnStart.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF007ACC"));
                BtnStart.IsEnabled = true;
                if (BtnRepair != null) BtnRepair.IsEnabled = true;

                MessageBox.Show("อัปเดตเกมเสร็จสมบูรณ์!", "Success");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Patch Error");
                BtnStart.Content = "UPDATE FAILED";
                BtnStart.IsEnabled = true;
                if (BtnRepair != null) BtnRepair.IsEnabled = true;
            }
        }

        // --- 10. อัปเดต launcher ตัวเอง ---
        private void UpdateLauncherSelf()
        {
            string currentExe = AppDomain.CurrentDomain.FriendlyName;
            string newExe = "C9Launcher_New.exe";
            string batPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "updater.bat");

            string batScript = $@"@echo off
timeout /t 2 /nobreak > NUL
del ""{currentExe}""
ren ""{newExe}"" ""{currentExe}""
start """" ""{currentExe}""
del ""%~f0""
";
            File.WriteAllText(batPath, batScript);

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = batPath,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);

            Application.Current.Shutdown();
        }

        // --- 11. ปุ่ม repair ---
        private async void BtnRepair_Click(object sender, RoutedEventArgs e)
        {
            BtnStart.IsEnabled = false;
            if (BtnRepair != null) BtnRepair.IsEnabled = false;

            await VerifyGameFilesAsync();

            BtnStart.IsEnabled = true;
            if (BtnRepair != null) BtnRepair.IsEnabled = true;
        }

        // --- 12. ตรวจสอบไฟล์ด้วย MD5 ---
        private async Task VerifyGameFilesAsync()
        {
            try
            {
                UpdateProgressBar.Visibility = Visibility.Visible;
                TxtProgress.Visibility = Visibility.Visible;
                TxtProgress.Text = "กำลังตรวจสอบไฟล์เกม...";

                using HttpClient client = new HttpClient();
                string jsonResponse = await client.GetStringAsync(hashApiUrl);

                if (!jsonResponse.Trim().StartsWith("{"))
                {
                    MessageBox.Show($"เซิร์ฟเวอร์ตอบกลับมาผิดพลาด (ไม่ใช่ JSON):\n\n{jsonResponse}", "API Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var serverFiles = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonResponse);
                int count = 0;

                if (serverFiles != null)
                {
                    foreach (var item in serverFiles)
                    {
                        count++;
                        string fileName = item.Key;
                        string serverHash = item.Value;
                        string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);

                        string? directory = Path.GetDirectoryName(localPath);
                        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                            Directory.CreateDirectory(directory);

                        UpdateProgressBar.Value = ((double)count / serverFiles.Count) * 100;
                        string localHash = CalculateMD5(localPath);

                        if (localHash != serverHash)
                        {
                            TxtProgress.Text = $"กำลังซ่อมแซมไฟล์: {fileName}";
                            byte[] fileBytes = await client.GetByteArrayAsync(downloadBaseUrl + fileName.Replace("\\", "/"));
                            File.WriteAllBytes(localPath, fileBytes);
                        }
                    }
                }

                TxtProgress.Text = "ไฟล์สมบูรณ์ 100%";
                MessageBox.Show("ซ่อมแซมไฟล์เกมเรียบร้อยแล้ว", "Repair Complete");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Repair Error");
            }
            finally
            {
                UpdateProgressBar.Visibility = Visibility.Hidden;
                TxtProgress.Visibility = Visibility.Hidden;
            }
        }

        // --- 13. คำนวณ MD5 ---
        private string CalculateMD5(string filePath)
        {
            if (!File.Exists(filePath))
                return "";

            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hash = md5.ComputeHash(stream);

            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }
}