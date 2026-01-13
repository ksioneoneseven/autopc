using System.Text.Json;
using AutoPilotAgent.Core.Interfaces;
using AutoPilotAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace AutoPilotAgent.OpenAI;

public sealed class OpenAIPlanService : IPlanService
{
    private readonly OpenAIClient _client;
    private readonly OpenAIOptions _options;
    private readonly Func<string?> _apiKeyProvider;
    private readonly ILogger<OpenAIPlanService> _logger;

    public OpenAIPlanService(OpenAIClient client, OpenAIOptions options, Func<string?> apiKeyProvider, ILogger<OpenAIPlanService> logger)
    {
        _client = client;
        _options = options;
        _apiKeyProvider = apiKeyProvider;
        _logger = logger;
    }

    public async Task<PlanModel> GeneratePlanAsync(string goal)
    {
        var apiKey = _apiKeyProvider();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("No API key configured; using demo plan.");
            return DemoPlan(goal);
        }

        var schema = JsonDocument.Parse(Schemas.PlanSchema).RootElement;

        var detailedPrompt = $"""
Generate a DETAILED step-by-step execution plan for this goal: {goal}

IMPORTANT RULES FOR PLAN GENERATION:
1. Break down into MANY small, atomic steps (10-20 steps is normal)
2. Each step should be ONE specific action (not multiple actions combined)
3. Include explicit focus/click steps before any typing
4. Be very specific about WHAT to click, WHERE to type, WHAT keys to press

STEP TYPES TO USE:
- "Open [app] using Win+R" - to launch applications
- "Focus on [app] window" - ALWAYS before typing or clicking in an app
- "Click on [specific text/button]" - be exact about what to click
- "Type '[exact text]'" - specify the exact text to type
- "Press [keys]" - for keyboard shortcuts like Ctrl+S, Enter, Tab
- "Wait for [condition]" - when UI needs time to load
- "Navigate to [URL]" - for browser navigation

EXAMPLE FOR "Open Notepad, type Hello, save as test.txt":
1. Open Notepad using Win+R and typing 'notepad'
2. Wait for Notepad window to appear
3. Focus on Notepad window
4. Click in the text editing area
5. Type 'Hello from AutoPilot Agent'
6. Press Ctrl+S to open Save dialog
7. Wait for Save dialog to appear
8. Click on the filename field
9. Type 'test.txt'
10. Click on Documents in the sidebar (or navigate to Documents folder)
11. Click Save button

EXAMPLE FOR "Search hotels on Expedia":
1. Open Firefox browser using Win+R
2. Wait for Firefox to load
3. Focus on Firefox window
4. Click on address bar (or press Ctrl+L)
5. Type 'https://www.expedia.com'
6. Press Enter to navigate
7. Wait for Expedia homepage to load
8. Click on 'Stays' or 'Hotels' tab
9. Click on destination input field
10. Type the destination city
11. Click on check-in date field
12. Select check-in date
13. Click on check-out date field  
14. Select check-out date
15. Click Search button

Generate the plan with this level of detail. Each step must be actionable.
""";

        var payload = new
        {
            model = _options.Model,
            max_output_tokens = 2000,
            input = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "input_text",
                            text = detailedPrompt
                        }
                    }
                }
            },
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "plan_schema",
                    strict = true,
                    schema
                }
            }
        };

        var dto = await _client.CreateResponseStructuredAsync<PlanDto>(apiKey!, payload, CancellationToken.None);
        return Map(dto);
    }

    private static PlanModel DemoPlan(string goal)
    {
        if (goal.Contains("notepad", StringComparison.OrdinalIgnoreCase))
        {
            return new PlanModel
            {
                Goal = goal,
                RequiredApps = new List<string> { "notepad" },
                Steps = new List<PlanStep>
                {
                    new() { Id = 1, Description = "Open Notepad", RiskLevel = RiskLevel.Low, RequiresConfirmation = false, Validation = "active_window_title contains \"Notepad\"" },
                    new() { Id = 2, Description = "Type the message", RiskLevel = RiskLevel.Low, RequiresConfirmation = false, Validation = "last_action_success" },
                    new() { Id = 3, Description = "Save: Press Ctrl+S", RiskLevel = RiskLevel.Medium, RequiresConfirmation = true, Validation = "last_action_success" },
                    new() { Id = 4, Description = "Save: Type filename test.txt", RiskLevel = RiskLevel.Low, RequiresConfirmation = false, Validation = "last_action_success" },
                    new() { Id = 5, Description = "Save: Press Enter", RiskLevel = RiskLevel.Medium, RequiresConfirmation = false, Validation = "last_action_success" }
                }
            };
        }

        return new PlanModel
        {
            Goal = goal,
            Steps = new List<PlanStep>
            {
                new() { Id = 1, Description = "Unable to auto-generate a plan without an API key.", RiskLevel = RiskLevel.High, RequiresConfirmation = true, Validation = "last_action_success" }
            }
        };
    }

    private static PlanModel Map(PlanDto dto)
    {
        return new PlanModel
        {
            Goal = dto.Goal,
            ClarifyingQuestions = dto.ClarifyingQuestions ?? new List<string>(),
            RequiredApps = dto.RequiredApps ?? new List<string>(),
            Steps = dto.Steps.Select(s => new PlanStep
            {
                Id = s.Id,
                Description = s.Description,
                RiskLevel = ParseRisk(s.RiskLevel),
                RequiresConfirmation = s.RequiresConfirmation,
                Validation = s.Validation
            }).ToList()
        };
    }

    private static RiskLevel ParseRisk(string risk)
    {
        return risk.Trim().ToLowerInvariant() switch
        {
            "low" => RiskLevel.Low,
            "medium" => RiskLevel.Medium,
            "high" => RiskLevel.High,
            _ => RiskLevel.Medium
        };
    }
}
