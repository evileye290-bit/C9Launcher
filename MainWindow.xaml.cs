using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace C9Launcher
{
    public partial class MainWindow : Window
    {
        private readonly string bannersFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Banners");
        private readonly string localVersionFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version.txt");

        // ตัวแปรสำหรับอ่าน Config
        private readonly IConfiguration _config;
        private readonly string remoteVersionUrl;
        private readonly string patchDownloadUrl;
        private readonly string hashApiUrl;
        private readonly string downloadBaseUrl;
        private readonly string newsApiUrl;
        private readonly string newsPageUrl;

        private string currentVersion = "1.0.0";
        private string remoteLatestVersion = "";

        private readonly List<NewsItem> newsItems = new();
        private int currentNewsIndex = 0;
        private DispatcherTimer? newsTimer;

        public MainWindow()
        {
            InitializeComponent();

            // 1. โหลดการตั้งค่าจาก appsettings.json
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            _config = builder.Build();

            remoteVersionUrl = _config["ApiConfig:RemoteVersionUrl"] ?? "";
            patchDownloadUrl = _config["ApiConfig:PatchDownloadUrl"] ?? "";
            hashApiUrl = _config["ApiConfig:HashApiUrl"] ?? "";
            downloadBaseUrl = _config["ApiConfig:DownloadBaseUrl"] ?? "";
            newsApiUrl = _config["ApiConfig:NewsApiUrl"] ?? "";
            newsPageUrl = _config["ApiConfig:NewsPageUrl"] ?? "https://c9-hd.com/";

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

        // --- ส่วนควบคุม UI หน้าต่างไร้ขอบ ---
        private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                this.DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;

        private class NewsItem
        {
            public string Category { get; set; } = "NEWS";
            public string Title { get; set; } = "";
            public string DateText { get; set; } = "";
            public string Summary { get; set; } = "";
            public string ImageUrl { get; set; } = "";
            public string LinkUrl { get; set; } = "";
        }

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
            catch (Exception ex)
            {
                Debug.WriteLine($"Banner Load Error: {ex.Message}");
            }
        }

        private async Task CheckVersionAsync()
        {
            if (File.Exists(localVersionFile))
                currentVersion = await File.ReadAllTextAsync(localVersionFile);
            else
                await File.WriteAllTextAsync(localVersionFile, currentVersion);

            currentVersion = currentVersion.Trim();
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

        // --- ระบบข่าวสารผ่าน JSON API ---
        private async Task LoadNewsSliderAsync()
        {
            try
            {
                using HttpClient client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");

                // ดึงข้อมูลข่าวจาก API แบบ JSON
                string jsonResponse = await client.GetStringAsync(newsApiUrl);
                var items = JsonSerializer.Deserialize<List<NewsItem>>(jsonResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                newsItems.Clear();
                if (items != null) newsItems.AddRange(items.Take(5));

                if (newsItems.Count == 0)
                {
                    ShowEmptyNews();
                    return;
                }

                currentNewsIndex = 0;
                ShowNews(newsItems[currentNewsIndex]);
                UpdateNewsIndicator();

                if (newsItems.Count > 1)
                {
                    newsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                    newsTimer.Tick += NewsTimer_Tick;
                    newsTimer.Start();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"News API Error: {ex.Message}");
                ShowEmptyNews();
                TxtNewsSummary.Text = "ไม่สามารถเชื่อมต่อเซิร์ฟเวอร์ข่าวสารได้";
            }
        }

        private void ShowEmptyNews()
        {
            TxtNewsCategory.Text = "NEWS";
            TxtNewsTitle.Text = "ไม่มีข่าวสารใหม่";
            TxtNewsDate.Text = "";
            TxtNewsSummary.Text = "";
            TxtNewsIndex.Text = "";
            NewsImage.Source = null;
        }

        private void NewsTimer_Tick(object? sender, EventArgs e)
        {
            if (newsItems.Count == 0) return;

            currentNewsIndex++;
            if (currentNewsIndex >= newsItems.Count) currentNewsIndex = 0;

            ShowNews(newsItems[currentNewsIndex]);
            UpdateNewsIndicator();
        }

        private void ShowNews(NewsItem item)
        {
            TxtNewsCategory.Text = string.IsNullOrWhiteSpace(item.Category) ? "NEWS" : item.Category;
            TxtNewsTitle.Text = item.Title;
            TxtNewsDate.Text = item.DateText;
            TxtNewsSummary.Text = item.Summary;
            NewsCard.Tag = string.IsNullOrWhiteSpace(item.LinkUrl) ? newsPageUrl : item.LinkUrl;

            NewsImage.Source = null;
            if (!string.IsNullOrWhiteSpace(item.ImageUrl))
            {
                _ = LoadNewsImageAsync(item.ImageUrl);
            }
        }

        private async Task LoadNewsImageAsync(string url)
        {
            try
            {
                using HttpClient client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
                byte[] imageBytes = await client.GetByteArrayAsync(url);

                using MemoryStream stream = new MemoryStream(imageBytes);
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();

                NewsImage.Source = bitmap;
            }
            catch { NewsImage.Source = null; }
        }

        private void UpdateNewsIndicator() => TxtNewsIndex.Text = newsItems.Count == 0 ? "" : $"{currentNewsIndex + 1}/{newsItems.Count}";

        private void BtnPrevNews_Click(object sender, RoutedEventArgs e)
        {
            if (newsItems.Count == 0) return;
            currentNewsIndex = (currentNewsIndex - 1 + newsItems.Count) % newsItems.Count;
            ShowNews(newsItems[currentNewsIndex]);
            UpdateNewsIndicator();
            RestartNewsTimer();
        }

        private void BtnNextNews_Click(object sender, RoutedEventArgs e)
        {
            if (newsItems.Count == 0) return;
            currentNewsIndex = (currentNewsIndex + 1) % newsItems.Count;
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

        private void NewsCard_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            string url = NewsCard.Tag?.ToString() ?? newsPageUrl;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { MessageBox.Show("ไม่สามารถเปิดหน้าเว็บได้", "Error"); }
        }

        private void NewsCard_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => e.Handled = true;

        private void BtnSocial_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string url && !string.IsNullOrWhiteSpace(url))
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        private void BtnOption_Click(object sender, RoutedEventArgs e)
        {
            string optionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "C9ConfigGameOn.exe");
            if (File.Exists(optionPath)) Process.Start(new ProcessStartInfo(optionPath) { UseShellExecute = true });
            else MessageBox.Show("ไม่พบไฟล์ C9ConfigGameOn.exe ในโฟลเดอร์เกม", "Error");
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            // ตรวจสอบว่าเกมเปิดอยู่แล้วหรือไม่ ป้องกันการเปิดซ้ำซ้อน
            if (Process.GetProcessesByName("c9").Length > 0)
            {
                MessageBox.Show("เกมกำลังทำงานอยู่ กรุณาตรวจสอบที่ Task Manager", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

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

                if (!File.Exists(tempZipPath)) throw new Exception("ดาวน์โหลดแพทช์ไม่สำเร็จ");

                BtnStart.Content = "EXTRACTING...";
                TxtProgress.Text = "กำลังติดตั้งแพทช์...";

                await Task.Run(() =>
                {
                    // ป้องกันการแตกไฟล์ทับไฟล์ที่กำลังถูกใช้งานอยู่
                    ZipFile.ExtractToDirectory(tempZipPath, AppDomain.CurrentDomain.BaseDirectory, true);
                });

                File.Delete(tempZipPath);
                await File.WriteAllTextAsync(localVersionFile, remoteLatestVersion);
                currentVersion = remoteLatestVersion;
                TxtVersion.Text = $"Version: {currentVersion}";

                string newLauncherPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "C9Launcher_New.exe");
                if (File.Exists(newLauncherPath))
                {
                    UpdateLauncherSelf();
                    return;
                }

                ResetStartButton();
                MessageBox.Show("อัปเดตเกมเสร็จสมบูรณ์!", "Success");
            }
            catch (IOException ioEx)
            {
                MessageBox.Show($"ไฟล์บางส่วนถูกใช้งานอยู่ กรุณาปิดเกมหรือโปรแกรมอื่นที่เกี่ยวข้องแล้วลองอีกครั้ง\n\nรายละเอียด: {ioEx.Message}", "Extraction Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ResetStartButton("UPDATE FAILED");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Patch Error");
                ResetStartButton("UPDATE FAILED");
            }
        }

        private void ResetStartButton(string text = "START GAME")
        {
            UpdateProgressBar.Visibility = Visibility.Hidden;
            TxtProgress.Visibility = Visibility.Hidden;
            BtnStart.Content = text;
            BtnStart.Background = text == "START GAME" ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF007ACC")) : new SolidColorBrush(Colors.Red);
            BtnStart.IsEnabled = true;
            if (BtnRepair != null) BtnRepair.IsEnabled = true;
        }

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

        private async void BtnRepair_Click(object sender, RoutedEventArgs e)
        {
            BtnStart.IsEnabled = false;
            if (BtnRepair != null) BtnRepair.IsEnabled = false;

            await VerifyGameFilesAsync();

            BtnStart.IsEnabled = true;
            if (BtnRepair != null) BtnRepair.IsEnabled = true;
        }

        // --- ระบบซ่อมแซมไฟล์ความเร็วสูง (Parallel Repair) ---
        private async Task VerifyGameFilesAsync()
        {
            try
            {
                UpdateProgressBar.Visibility = Visibility.Visible;
                TxtProgress.Visibility = Visibility.Visible;
                TxtProgress.Text = "กำลังตรวจสอบไฟล์เกม...";

                using HttpClient client = new HttpClient();
                string jsonResponse = await client.GetStringAsync(hashApiUrl);

                var serverFiles = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonResponse);
                if (serverFiles == null || serverFiles.Count == 0) return;

                int count = 0;

                // ตรวจสอบหลายไฟล์พร้อมกันเพื่อความรวดเร็ว
                await Parallel.ForEachAsync(serverFiles, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (item, cancellationToken) =>
                {
                    string fileName = item.Key;
                    string serverHash = item.Value;
                    string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);

                    string? directory = Path.GetDirectoryName(localPath);
                    if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                        Directory.CreateDirectory(directory);

                    string localHash = CalculateMD5(localPath);

                    if (localHash != serverHash)
                    {
                        Dispatcher.Invoke(() => TxtProgress.Text = $"กำลังดาวน์โหลด: {fileName}");
                        byte[] fileBytes = await client.GetByteArrayAsync(downloadBaseUrl + fileName.Replace("\\", "/"), cancellationToken);
                        await File.WriteAllBytesAsync(localPath, fileBytes, cancellationToken);
                    }

                    int currentCount = Interlocked.Increment(ref count);
                    Dispatcher.Invoke(() => UpdateProgressBar.Value = ((double)currentCount / serverFiles.Count) * 100);
                });

                TxtProgress.Text = "ไฟล์สมบูรณ์ 100%";
                MessageBox.Show("ซ่อมแซมไฟล์เกมเรียบร้อยแล้ว", "Repair Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"เกิดข้อผิดพลาดในการตรวจสอบไฟล์: {ex.Message}", "Repair Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                UpdateProgressBar.Visibility = Visibility.Hidden;
                TxtProgress.Visibility = Visibility.Hidden;
            }
        }

        private string CalculateMD5(string filePath)
        {
            if (!File.Exists(filePath)) return "";
            try
            {
                using var md5 = MD5.Create();
                using var stream = File.OpenRead(filePath);
                byte[] hash = md5.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
            catch
            {
                return ""; // ถ้าไฟล์ถูกใช้อยู่ให้ตีว่า Hash ไม่ตรง จะได้โหลดใหม่หรือขึ้น Error
            }
        }
    }
}