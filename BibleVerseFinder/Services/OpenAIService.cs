namespace BibleVerseFinder.Services
{
    using BibleVerseFinder.Models;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;

    public class OpenAIService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;

        public OpenAIService(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _config = config;
        }

        public async Task<(List<BibleVerse>, string)> GetBibleVersesAsync(string topic)
        {
            string apiKey = _config["OpenAI:ApiKey"];

            var prompt = $@"
A user is struggling with '{topic}'.
Return 10 Bible verses that relate to this topic.

Return ONLY valid JSON in this format:
{{
  ""verses"": [
    {{
      ""verse"": ""Philippians 4:6-7"",
      ""text"": ""..."",
      ""note"": ""...""
    }}
  ],
  ""encouragement"": ""A short encouraging message""
}}
";

            var requestData = new
            {
                model = "gpt-5-mini",
                input = new[]
                {
                    new { role = "system", content = "You are a helpful Bible assistant." },
                    new { role = "user", content = prompt }
                },
                max_output_tokens = 1500,
                text = new
                {
                    format = new
                    {
                        type = "json_object"
                    }
                }
            };

            var requestJson = JsonSerializer.Serialize(requestData);

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            // 🔍 Debug (keep this while testing)
            Console.WriteLine("RAW RESPONSE:");
            Console.WriteLine(body);

            if (!response.IsSuccessStatusCode)
            {
                return (new List<BibleVerse>
                {
                    new BibleVerse
                    {
                        Verse = "Error",
                        Text = $"OpenAI request failed: {response.StatusCode}",
                        Note = body
                    }
                }, "");
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            // ✅ SAFE error handling
            if (root.TryGetProperty("error", out JsonElement errorElement) &&
                errorElement.ValueKind == JsonValueKind.Object &&
                errorElement.TryGetProperty("message", out JsonElement msgEl) &&
                msgEl.ValueKind == JsonValueKind.String)
            {
                return (new List<BibleVerse>
                {
                    new BibleVerse
                    {
                        Verse = "OpenAI Error",
                        Text = "API responded with an error.",
                        Note = msgEl.GetString()
                    }
                }, "");
            }

            // ✅ SAFE extraction
            string jsonText = "";

            if (root.TryGetProperty("output", out JsonElement outputArray) &&
                outputArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var outputItem in outputArray.EnumerateArray())
                {
                    if (outputItem.ValueKind != JsonValueKind.Object)
                        continue;

                    if (outputItem.TryGetProperty("content", out JsonElement contentArray) &&
                        contentArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var contentItem in contentArray.EnumerateArray())
                        {
                            if (contentItem.ValueKind != JsonValueKind.Object)
                                continue;

                            if (contentItem.TryGetProperty("type", out var typeEl) &&
                                typeEl.ValueKind == JsonValueKind.String &&
                                typeEl.GetString() == "output_text" &&
                                contentItem.TryGetProperty("text", out var textEl) &&
                                textEl.ValueKind == JsonValueKind.String)
                            {
                                jsonText += textEl.GetString();
                            }
                        }
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(jsonText))
            {
                return (new List<BibleVerse>
                {
                    new BibleVerse
                    {
                        Verse = "No Response",
                        Text = "Model returned empty or unexpected output.",
                        Note = "Check raw API response."
                    }
                }, "");
            }

            // ✅ SAFE deserialization
            try
            {
                var parsed = JsonSerializer.Deserialize<BibleResponse>(jsonText,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                return (
                    parsed?.Verses ?? new List<BibleVerse>(),
                    parsed?.Encouragement ?? ""
                );
            }
            catch (Exception ex)
            {
                return (new List<BibleVerse>
                {
                    new BibleVerse
                    {
                        Verse = "Parse Error",
                        Text = "Failed to parse AI response.",
                        Note = ex.Message
                    }
                }, "");
            }
        }
    }

    public class BibleResponse
    {
        public List<BibleVerse> Verses { get; set; } = new();
        public string Encouragement { get; set; } = "";
    }
}
