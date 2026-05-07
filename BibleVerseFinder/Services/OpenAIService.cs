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
            // First attempt
            var result = await CallOpenAI(topic);

            if (result.verses.Count > 0)
                return result;

            // 🔁 Retry once if parsing failed
            return await CallOpenAI(topic);
        }

        private async Task<(List<BibleVerse> verses, string encouragement)> CallOpenAI(string topic)
        {
            string apiKey = _config["OpenAI:ApiKey"];

            var prompt = $@"
A user is struggling with '{topic}'.
Return 10 Bible verses that relate to this topic.

Return ONLY valid JSON.
Do NOT include trailing commas.
Ensure all arrays and objects are fully closed.

Format:
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
                model = "gpt-5.3",
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

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            request.Content = new StringContent(
                JsonSerializer.Serialize(requestData),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

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

            // ✅ Safe error handling
            if (root.TryGetProperty("error", out var errorEl) &&
                errorEl.ValueKind == JsonValueKind.Object &&
                errorEl.TryGetProperty("message", out var msgEl) &&
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

            // ✅ Extract text safely
            string jsonText = ExtractText(root);

            if (string.IsNullOrWhiteSpace(jsonText))
            {
                return (new List<BibleVerse>
                {
                    new BibleVerse
                    {
                        Verse = "No Response",
                        Text = "Model returned empty output.",
                        Note = "Check raw API response."
                    }
                }, "");
            }

            // ✅ Fix JSON if needed
            jsonText = FixJson(jsonText);

            // ✅ Deserialize safely
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
            catch
            {
                return (new List<BibleVerse>(), "");
            }
        }

        private string ExtractText(JsonElement root)
        {
            string result = "";

            if (root.TryGetProperty("output", out var outputArr) &&
                outputArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var outputItem in outputArr.EnumerateArray())
                {
                    if (outputItem.ValueKind != JsonValueKind.Object)
                        continue;

                    if (outputItem.TryGetProperty("content", out var contentArr) &&
                        contentArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in contentArr.EnumerateArray())
                        {
                            if (item.ValueKind != JsonValueKind.Object)
                                continue;

                            if (item.TryGetProperty("type", out var typeEl) &&
                                typeEl.GetString() == "output_text" &&
                                item.TryGetProperty("text", out var textEl) &&
                                textEl.ValueKind == JsonValueKind.String)
                            {
                                result += textEl.GetString();
                            }
                        }
                    }
                }
            }

            return result;
        }

        private string FixJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return "{}";

            json = json.Trim()
                       .Replace("```json", "")
                       .Replace("```", "");

            int openBraces = json.Count(c => c == '{');
            int closeBraces = json.Count(c => c == '}');
            int openBrackets = json.Count(c => c == '[');
            int closeBrackets = json.Count(c => c == ']');

            json += new string('}', Math.Max(0, openBraces - closeBraces));
            json += new string(']', Math.Max(0, openBrackets - closeBrackets));

            return json;
        }
    }

    public class BibleResponse
    {
        public List<BibleVerse> Verses { get; set; } = new();
        public string Encouragement { get; set; } = "";
    }
}
