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

Format the response as JSON. Each item should include:
- ""verse"": the reference (e.g., ""Philippians 4:6-7"")
- ""text"": the NIV verse text
- ""note"": a one-sentence explanation
- Respond only with valid JSON, no markdown or commentary.

Then include a short, encouraging message after the JSON (e.g., ""Take heart, God is near."")
";

            var requestData = new
            {
                model = "gpt-4",
                messages = new[]
                {
            new { role = "system", content = "You are a helpful Bible assistant." },
            new { role = "user", content = prompt }
        },
                temperature = 0.7,
                max_tokens = 1500
            };

            var requestJson = JsonSerializer.Serialize(requestData);
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
            request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return (new List<BibleVerse>
    {
        new BibleVerse
        {
            Verse = "Error",
            Text = $"OpenAI request failed with status code {response.StatusCode}.",
            Note = "Please try again later."
        }
    }, "");
            }

            var body = await response.Content.ReadAsStringAsync();

            string message;
           


            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            // Handle OpenAI error response
            if (root.TryGetProperty("error", out JsonElement errorElement))
            {
                var errorMessage = errorElement.GetProperty("message").GetString();
                return (new List<BibleVerse>
    {
        new BibleVerse
        {
            Verse = "OpenAI Error",
            Text = "API responded with an error.",
            Note = errorMessage
        }
    }, "Please try again later.");
            }

            // Check choices and extract content safely
            if (root.TryGetProperty("choices", out JsonElement choices) &&
                choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("message", out JsonElement msgElement) &&
                msgElement.TryGetProperty("content", out JsonElement contentElement))
            {
                message = contentElement.GetString();
            }
            else
            {
                return (new List<BibleVerse>
    {
        new BibleVerse
        {
            Verse = "No Response",
            Text = "Could not find a valid response from OpenAI.",
            Note = "Please check your input or try again shortly."
        }
    }, "");
            }


            // 🧹 Clean the GPT output
            message = message.Replace("```json", "")
                             .Replace("```", "")
                             .Trim();

            // 🧠 Extract only JSON part
            int startIndex = message.IndexOf("[");
            int endIndex = message.LastIndexOf("]");
            string jsonPart = startIndex >= 0 && endIndex > startIndex
                ? message.Substring(startIndex, endIndex - startIndex + 1)
                : "[]";

            // Extract encouragement text
            string encouragement = (endIndex + 1 < message.Length)
     ? message.Substring(endIndex + 1).TrimStart('}', '\n', '\r', ' ', '.', '-').Trim()
     : "";


            List<BibleVerse> verses = new();

            try
            {
                verses = JsonSerializer.Deserialize<List<BibleVerse>>(jsonPart,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new List<BibleVerse>();
            }
            catch (Exception ex)
            {
                // Log or display an error if needed
                verses = new List<BibleVerse>
        {
            new BibleVerse
            {
                Verse = "Error",
                Text = "Could not parse response.",
                Note = ex.Message
            }
        };
            }

            return (verses, encouragement);
        }

    }
}
