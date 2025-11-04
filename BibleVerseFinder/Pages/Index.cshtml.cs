
using BibleVerseFinder.Models;
using BibleVerseFinder.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

public class IndexModel : PageModel
{
    private readonly OpenAIService _openAIService;

    public IndexModel(OpenAIService openAIService)
    {
        _openAIService = openAIService;
    }

    [BindProperty]
    public string Topic { get; set; }
    public string LastTopic { get; set; }


    public List<BibleVerse> Verses { get; set; } = new();
    public string Encouragement { get; set; }

    public async Task OnPostAsync()
    {
        if (!string.IsNullOrWhiteSpace(Topic))
        {
            (Verses, Encouragement) = await _openAIService.GetBibleVersesAsync(Topic);
            LastTopic = Topic;
        }
    }
}
