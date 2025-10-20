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
            var body = await response.Content.ReadAsStringAsync();

            var message = JsonDocument.Parse(body)
                .RootElement.GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

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
        ? message.Substring(endIndex + 1).Trim()
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
