using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using cmdrix.Models;

namespace cmdrix.Services
{
    public class CommandProcessor
    {
        private readonly HttpClient _httpClient;
        private readonly string _geminiApiKey;
        private readonly ConfigService _configService;

        public CommandProcessor()
        {
            _httpClient = new HttpClient();
            _configService = new ConfigService();
            var config = _configService.LoadConfig();
            _geminiApiKey = config.GeminiApiKey;

            if (string.IsNullOrEmpty(_geminiApiKey))
            {
                throw new InvalidOperationException("Gemini API key not configured. Please set it in config.json");
            }
        }

        public async Task<CommandPreview> GenerateCommandPreview(string userQuery)
        {
            var currentDir = Directory.GetCurrentDirectory();
            var files = Directory.GetFiles(currentDir).Select(Path.GetFileName).ToList();
            var directories = Directory.GetDirectories(currentDir).Select(Path.GetFileName).ToList();

            var prompt = $@"You are a command line assistant for windows os. Generate a window shell command based on the user's request.
Current directory: {currentDir}
Files: {string.Join(", ", files)}
Directories: {string.Join(", ", directories)}

User request: {userQuery}

Available tools: ffmpeg, imagemagick (magick command)

Respond with ONLY a JSON object in this format:
{{
    ""command"": ""the actual command to run"",
    ""description"": ""brief description of what it does"",
    ""safetyLevel"": ""safe"" | ""warning"" | ""danger""
}}

Examples:
- ""safe"": read-only operations, listing files, showing info
- ""warning"": file modifications, renames, conversions
- ""danger"": deletions, system changes, bulk operations

Respond with ONLY the JSON, no markdown, no extra text.";

            var response = await CallGeminiAPI(prompt);
            return ParseCommandPreview(response);
        }

        public async Task<string> ChatWithAI(string userMessage)
        {
            var prompt = $@"You are cmdrix, a helpful terminal assistant. 
The user can use commands like:
- /command [query] - to generate terminal commands
- /notes [content] - to save notes
- /screenshot [question] - to capture and analyze screenshots

User message: {userMessage}

Respond naturally and helpfully. Keep responses concise.";

            return await CallGeminiAPI(prompt);
        }

        public async Task<string> ExecuteCommand(CommandPreview preview)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {preview.Command}",
                    WorkingDirectory = preview.WorkingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process == null)
                {
                    return "❌ Failed to start process";
                }

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    return $"❌ Command failed:\n{error}";
                }

                return string.IsNullOrEmpty(output) ? "✓ Command completed successfully" : output;
            }
            catch (Exception ex)
            {
                return $"❌ Error executing command: {ex.Message}";
            }
        }

        private async Task<string> CallGeminiAPI(string prompt)
        {
            var request = new GeminiRequest
            {
                Contents = new List<GeminiContent>
                {
                    new GeminiContent
                    {
                        Parts = new List<GeminiPart>
                        {
                            new GeminiPart { Text = prompt }
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

        private CommandPreview ParseCommandPreview(string jsonResponse)
        {
            try
            {
                // Clean up response if it has markdown code blocks
                var cleaned = jsonResponse.Trim();
                if (cleaned.StartsWith("```json"))
                {
                    cleaned = cleaned.Substring(7);
                }
                if (cleaned.StartsWith("```"))
                {
                    cleaned = cleaned.Substring(3);
                }
                if (cleaned.EndsWith("```"))
                {
                    cleaned = cleaned.Substring(0, cleaned.Length - 3);
                }
                cleaned = cleaned.Trim();

                var json = JsonSerializer.Deserialize<Dictionary<string, string>>(cleaned);
                if (json == null) throw new Exception("Failed to parse JSON");

                var safetyLevel = SafetyLevel.Warning;
                if (json.TryGetValue("safetyLevel", out var safety))
                {
                    safetyLevel = safety.ToLower() switch
                    {
                        "safe" => SafetyLevel.Safe,
                        "warning" => SafetyLevel.Warning,
                        "danger" => SafetyLevel.Danger,
                        _ => SafetyLevel.Warning
                    };
                }

                return new CommandPreview
                {
                    Command = json.GetValueOrDefault("command", ""),
                    Description = json.GetValueOrDefault("description", ""),
                    SafetyLevel = safetyLevel,
                    WorkingDirectory = Directory.GetCurrentDirectory()
                };
            }
            catch (Exception ex)
            {
                return new CommandPreview
                {
                    Command = "echo Error parsing command",
                    Description = $"Failed to parse AI response: {ex.Message}",
                    SafetyLevel = SafetyLevel.Danger,
                    WorkingDirectory = Directory.GetCurrentDirectory()
                };
            }
        }
    }
}