namespace AutoPilotAgent.OpenAI;

internal static class ContentBuilder
{
    internal static object[] BuildContent(string prompt, string? screenshotDataUrl)
    {
        if (string.IsNullOrWhiteSpace(screenshotDataUrl))
        {
            return new object[]
            {
                new { type = "input_text", text = prompt }
            };
        }

        return new object[]
        {
            new { type = "input_text", text = prompt },
            new { type = "input_image", image_url = screenshotDataUrl }
        };
    }
}
