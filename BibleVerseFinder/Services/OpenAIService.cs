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

Format the response strictly as JSON with this structure:
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
                temperature = 0.7,
                max_output_tokens = 1500,
                format = new
                {
                    type = "json_object"
                }
            };

            var requestJson = JsonSerializer.Serialize(requestData);

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);

            var body = await response.Content.ReadAsStringAsync();

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

            // Handle OpenAI error format
            if (root.TryGetProperty("error", out JsonElement errorElement))
            {
                return (new List<BibleVerse>
                {
                    new BibleVerse
                    {
                        Verse = "OpenAI Error",
                        Text = "API responded with an error.",
                        Note = errorElement.GetProperty("message").GetString()
                    }
                }, "");
            }

            // ✅ Extract text from Responses API
            string jsonText = "";

            if (root.TryGetProperty("output", out JsonElement outputArray) &&
                outputArray.GetArrayLength() > 0)
            {
                var firstOutput = outputArray[0];

                if (firstOutput.TryGetProperty("content", out JsonElement contentArray))
                {
                    foreach (var item in contentArray.EnumerateArray())
                    {
                        if (item.TryGetProperty("type", out var typeElement) &&
                            typeElement.GetString() == "output_text")
                        {
                            jsonText += item.GetProperty("text").GetString();
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
                        Text = "Could not extract response text.",
                        Note = "Unexpected API format."
                    }
                }, "");
            }

            // ✅ Deserialize clean structured JSON
            try
            {
                var parsed = JsonSerializer.Deserialize<BibleResponse>(jsonText,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                return (parsed?.Verses ?? new List<BibleVerse>(), parsed?.Encouragement ?? "");
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

    // ✅ Strongly typed response model
    public class BibleResponse
    {
        public List<BibleVerse> Verses { get; set; } = new();
        public string Encouragement { get; set; } = "";
    }
}
