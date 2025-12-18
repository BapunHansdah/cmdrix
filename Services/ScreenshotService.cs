using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using cmdrix.Models;
using cmdrix.UI;

namespace cmdrix.Services
{
    public class ScreenshotService
    {
        private readonly HttpClient _httpClient;
        private readonly string _geminiApiKey;
        private readonly ConfigService _configService;

        public ScreenshotService()
        {
            _httpClient = new HttpClient();
            _configService = new ConfigService();
            var config = _configService.LoadConfig();
            _geminiApiKey = config.GeminiApiKey;
        }

        public async Task<string> CaptureAndAnalyze(string input)
        {
            try
            {
                // Parse input for -s flag and question
                bool selectArea = false;
                string question = input;

                if (input.StartsWith("-s"))
                {
                    selectArea = true;
                    question = input.Substring(2).Trim();
                }

                Bitmap screenshot;

                if (selectArea)
                {
                    // Show selection window
                    screenshot = await CaptureSelectedArea();

                    if (screenshot == null)
                    {
                        return "❌ Screenshot cancelled";
                    }
                }
                else
                {
                    // Capture full screen
                    screenshot = CaptureScreen();
                }

                var base64Image = ConvertToBase64(screenshot);

                // If no question, just save the screenshot
                if (string.IsNullOrWhiteSpace(question))
                {
                    var filename = SaveScreenshot(screenshot);
                    screenshot.Dispose();
                    return $"✓ Screenshot saved: {filename}";
                }

                // Analyze with AI
                var analysis = await AnalyzeScreenshot(base64Image, question);

                // Save screenshot
                var savedFilename = SaveScreenshot(screenshot);
                screenshot.Dispose();

                return $"{analysis}\n\n📁 Screenshot saved: {savedFilename}";
            }
            catch (Exception ex)
            {
                return $"❌ Screenshot error: {ex.Message}";
            }
        }

        private async Task<Bitmap> CaptureSelectedArea()
        {
            // Use Dispatcher to ensure UI operations happen on the UI thread
            return await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var selectionWindow = new ScreenshotSelectionWindow();
                selectionWindow.ShowDialog();

                if (selectionWindow.WasCancelled)
                {
                    return null;
                }

                return selectionWindow.SelectedScreenshot;
            });
        }

        private Bitmap CaptureScreen()
        {
            // Get primary screen dimensions using platform-specific approach
            int screenWidth = 1920;  // Default fallback
            int screenHeight = 1080;

            // For Windows, use P/Invoke
            if (OperatingSystem.IsWindows())
            {
                screenWidth = GetSystemMetrics(0);  // SM_CXSCREEN
                screenHeight = GetSystemMetrics(1); // SM_CYSCREEN
            }

            var bitmap = new Bitmap(screenWidth, screenHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(0, 0, 0, 0, new System.Drawing.Size(screenWidth, screenHeight), CopyPixelOperation.SourceCopy);
            }

            return bitmap;
        }

        // P/Invoke for Windows screen dimensions
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private string ConvertToBase64(Bitmap bitmap)
        {
            using (var memoryStream = new MemoryStream())
            {
                bitmap.Save(memoryStream, ImageFormat.Jpeg);
                var imageBytes = memoryStream.ToArray();
                return Convert.ToBase64String(imageBytes);
            }
        }

        private string SaveScreenshot(Bitmap bitmap)
        {
            var screenshotsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "cmdrix_screenshots"
            );
            Directory.CreateDirectory(screenshotsPath);

            var filename = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            var fullPath = Path.Combine(screenshotsPath, filename);

            bitmap.Save(fullPath, ImageFormat.Png);
            return fullPath;
        }

        private async Task<string> AnalyzeScreenshot(string base64Image, string question)
        {
            var request = new GeminiRequest
            {
                Contents = new List<GeminiContent>
                {
                    new GeminiContent
                    {
                        Parts = new List<GeminiPart>
                        {
                            new GeminiPart { Text = question },
                            new GeminiPart
                            {
                                InlineData = new GeminiInlineData
                                {
                                    MimeType = "image/jpeg",
                                    Data = base64Image
                                }
                            }
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var modelName = "gemini-flash-latest";
            var response = await _httpClient.PostAsync(
                $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={_geminiApiKey}",
                content
            );

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"API call failed: {error}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(responseJson,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            return geminiResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text
                ?? "No response from AI";
        }
    }
}