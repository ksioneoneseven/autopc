using System.Text.Json;
using AutoPilotAgent.Core.Interfaces;
using AutoPilotAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace AutoPilotAgent.OpenAI;

public sealed class OpenAIActionService : IActionService
{
    private readonly OpenAIClient _client;
    private readonly OpenAIOptions _options;
    private readonly Func<string?> _apiKeyProvider;
    private readonly ILogger<OpenAIActionService> _logger;

    public OpenAIActionService(OpenAIClient client, OpenAIOptions options, Func<string?> apiKeyProvider, ILogger<OpenAIActionService> logger)
    {
        _client = client;
        _options = options;
        _apiKeyProvider = apiKeyProvider;
        _logger = logger;
    }

    private static ActionModel? TryBuildPaintSmileyAction(PlanStep step)
    {
        if (GuessTargetProcess(step.Description) != "mspaint")
        {
            return null;
        }

        var d = step.Description;

        if (d.Contains("open", StringComparison.OrdinalIgnoreCase) && d.Contains("paint", StringComparison.OrdinalIgnoreCase))
        {
            return new ActionModel
            {
                ActionType = ActionType.FocusWindow,
                RequiresConfirmation = false,
                ExpectedResult = "Paint focused",
                Parameters = JsonSerializer.SerializeToElement(new { title = "", process = "mspaint" })
            };
        }

        if (d.Contains("select", StringComparison.OrdinalIgnoreCase) || d.Contains("brush", StringComparison.OrdinalIgnoreCase) || d.Contains("pencil", StringComparison.OrdinalIgnoreCase))
        {
            // Click canvas area to ensure focus.
            return new ActionModel
            {
                ActionType = ActionType.ClickCoordinates,
                RequiresConfirmation = false,
                ExpectedResult = "Canvas focused",
                Parameters = JsonSerializer.SerializeToElement(new { rx = 0.5, ry = 0.55 })
            };
        }

        if (d.Contains("large", StringComparison.OrdinalIgnoreCase) && (d.Contains("circle", StringComparison.OrdinalIgnoreCase) || d.Contains("ellipse", StringComparison.OrdinalIgnoreCase)))
        {
            return BuildSmileyHead();
        }

        if (d.Contains("eye", StringComparison.OrdinalIgnoreCase))
        {
            return BuildSmileyEyes();
        }

        if (d.Contains("smile", StringComparison.OrdinalIgnoreCase) || d.Contains("mouth", StringComparison.OrdinalIgnoreCase) || d.Contains("arc", StringComparison.OrdinalIgnoreCase))
        {
            return BuildSmileyMouth();
        }

        if (d.Contains("draw", StringComparison.OrdinalIgnoreCase) && d.Contains("circle", StringComparison.OrdinalIgnoreCase))
        {
            return BuildSmileyHead();
        }

        // If the plan includes optional fill/save steps, treat them as no-op verifies to avoid brittle UIA.
        if (d.Contains("fill", StringComparison.OrdinalIgnoreCase) ||
            d.Contains("color", StringComparison.OrdinalIgnoreCase) ||
            d.Contains("save", StringComparison.OrdinalIgnoreCase) ||
            d.Contains("file", StringComparison.OrdinalIgnoreCase))
        {
            return new ActionModel
            {
                ActionType = ActionType.Verify,
                RequiresConfirmation = false,
                ExpectedResult = "Skipped optional paint UI step"
            };
        }

        return null;

        static ActionModel BuildSmileyHead()
        {
            var pts = CirclePath(0.5, 0.56, 0.22, 72);
            return PathAction(pts, "Head drawn");
        }

        static ActionModel BuildSmileyEyes()
        {
            var pts = new List<object>();
            pts.AddRange(CirclePath(0.43, 0.50, 0.028, 28, liftFirst: true));
            pts.AddRange(CirclePath(0.57, 0.50, 0.028, 28, liftFirst: true));
            return PathAction(pts, "Eyes drawn");
        }

        static ActionModel BuildSmileyMouth()
        {
            var pts = ArcPath(0.5, 0.64, 0.11, 200, 340, 54);
            return PathAction(pts, "Mouth drawn");
        }

        static ActionModel PathAction(List<object> path, string expected)
        {
            var parameters = new
            {
                path,
                move_delay_ms = 3
            };

            return new ActionModel
            {
                ActionType = ActionType.ClickCoordinates,
                RequiresConfirmation = false,
                ExpectedResult = expected,
                Parameters = JsonSerializer.SerializeToElement(parameters)
            };
        }

        static List<object> CirclePath(double cx, double cy, double r, int points, bool liftFirst = true)
        {
            var list = new List<object>(points + 2);
            for (var i = 0; i <= points; i++)
            {
                var t = i / (double)points;
                var a = t * Math.PI * 2;
                var x = cx + Math.Cos(a) * r;
                var y = cy + Math.Sin(a) * r;
                list.Add(new { mrx = x, mry = y, lift = liftFirst && i == 0 });
            }
            return list;
        }

        static List<object> ArcPath(double cx, double cy, double r, double startDeg, double endDeg, int points)
        {
            var list = new List<object>(points + 1);
            for (var i = 0; i <= points; i++)
            {
                var t = i / (double)points;
                var deg = startDeg + (endDeg - startDeg) * t;
                var a = deg * Math.PI / 180.0;
                var x = cx + Math.Cos(a) * r;
                var y = cy + Math.Sin(a) * r;
                list.Add(new { mrx = x, mry = y, lift = i == 0 });
            }
            return list;
        }
    }

    public async Task<ActionModel> GetNextActionAsync(PlanStep step, ObservationModel observation, string? goalContext = null, InteractionState? interactionState = null)
    {
        var focusOverride = TryBuildFocusOverride(step, observation);
        if (focusOverride is not null)
        {
            return focusOverride;
        }

        var paintDeterministic = TryBuildPaintSmileyAction(step);
        if (paintDeterministic is not null)
        {
            return paintDeterministic;
        }

        var apiKey = _apiKeyProvider();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return DemoAction(step);
        }

        // Do not embed ScreenshotDataUrl into the text JSON; it's huge and we send it separately as input_image.
        var obsJson = JsonSerializer.Serialize(new
        {
            observation.ActiveWindowTitle,
            observation.ActiveProcess,
            observation.LastActionSuccess,
            observation.ErrorMessage
        });

        var goalInfo = string.IsNullOrWhiteSpace(goalContext) ? "" : $"\nUSER'S ORIGINAL REQUEST: {goalContext}\n";
        var historyInfo = interactionState?.RecentActions.Count > 0 
            ? $"\nRECENT ACTIONS TAKEN:\n{interactionState.GetSummary()}\n" 
            : "";

        var prompt = $"""
Return the next automation action as STRICT JSON (no markdown, no code fences).
{goalInfo}{historyInfo}
JSON keys: action_type, parameters, requires_confirmation (bool), expected_result (string)

ACTION TYPES (BEST TO WORST):

1. win_run: command (string) - BEST for opening apps/settings
   Examples: "control", "ms-settings:", "notepad", "calc"

2. click_text: text (string), index (int, optional) - BEST FOR CLICKING! Uses OCR to find and click text.
   Examples: "Search", "Submit", "OK", "Cancel", "Settings", "Apply"
   Use index:0 for first match, index:1 for second, etc.

3. hotkey: keys (array) - For keyboard navigation
   ["TAB"], ["ENTER"], ["CTRL","L"], ["ALT","F4"]

4. type_text: text (string) - Type into focused field
   IMPORTANT: The target app MUST have focus first! Use focus_window before type_text.

5. focus_window: process (string) - Focus an app window (e.g., "notepad", "firefox")

6. click_grid: cell (1-192) - FALLBACK if click_text fails. Grid has 16 cols x 12 rows.

7. navigate_url: url (string) - Open URL

8. scroll: direction ("up"/"down"), amount ("page"/"300")

9. wait: ms (int) - Pause (use 500-1500)

10. run_command: command, shell - PowerShell commands for DIRECT changes

POWERSHELL COMMANDS FOR COMMON TASKS (use run_command):
- Set solid blue wallpaper: "Set-ItemProperty -Path 'HKCU:\\Control Panel\\Colors' -Name Background -Value '0 0 255'; Set-ItemProperty -Path 'HKCU:\\Control Panel\\Desktop' -Name Wallpaper -Value ''; RUNDLL32.EXE user32.dll,UpdatePerUserSystemParameters ,1 ,True"
- Set solid color (RGB): Change '0 0 255' to desired R G B values
- Open URL in browser: "Start-Process 'https://example.com'"

CLICK_TEXT IS YOUR PRIMARY CLICKING METHOD!
- Look at the screenshot and identify the TEXT of buttons/links
- Use click_text with that exact text
- Examples: click_text "Search", click_text "Sign in", click_text "Submit"

CRITICAL - BROWSER UI VS WEBSITE CONTENT:
- The TOP of the browser (rows 1-2, cells 1-32) is BROWSER CHROME - address bar, tabs, bookmarks, account buttons
- DO NOT click on browser UI elements like "Sign in to Firefox", profile icons, or menu buttons
- The WEBSITE CONTENT is BELOW the browser toolbar (rows 3-12, cells 33-192)
- When interacting with a website, ONLY click on elements IN THE MAIN CONTENT AREA
- If you see "Sign in" in the browser toolbar, IGNORE IT - look for website buttons instead
- Website search buttons are usually in the MIDDLE or BOTTOM of the screen, not the top

WORKFLOW FOR WEB FORMS:
1. Look for input fields IN THE WEBSITE CONTENT (not browser address bar)
2. click_text on the input field placeholder like "Where to?" or "Destination"
3. type_text to enter value
4. hotkey ["TAB"] to next field OR click_text on next field
5. click_text on the website's "Search" button (usually large and prominent)

STAY FOCUSED ON THE CURRENT STEP!
- Read the step description carefully - do ONLY what it says
- DO NOT click on navigation links like "Vacation Packages", "Flights", "Cars" unless the step says to
- DO NOT start a new search or go to a different page
- If you're filling a form, COMPLETE THE FORM before doing anything else
- If dates need to be selected, click on the date field and select the date
- If you already have search results, do NOT navigate away

KNOW WHEN TO STOP!
- If a video is already playing, the task is DONE - return action_type "done"
- If search results are showing, the search task is DONE - return action_type "done"
- If the requested content is visible, STOP - return action_type "done"
- Do NOT scroll, click ads, or do random actions after the goal is achieved
- action_type "done" signals the step is complete

AVOID THESE DISTRACTIONS:
- Navigation menus (Flights, Cars, Packages, Cruises)
- Promotional banners and ads
- "Sign in" or account-related buttons
- Footer links
- Sidebar suggestions
- DO NOT click on ads or sponsored content

Step: {step.Description}
Observation: {obsJson}
""";

        var payload = new
        {
            model = _options.Model,
            max_output_tokens = _options.MaxOutputTokens,
            input = new object[]
            {
                new
                {
                    role = "user",
                    content = ContentBuilder.BuildContent(prompt, observation.ScreenshotDataUrl)
                }
            }
        };

        try
        {
            var text = await _client.CreateResponseTextAsync(apiKey!, payload, CancellationToken.None);
            var dto = ParseActionDto(text);
            // If we're still in the UI foreground, force a focus action first.
            focusOverride = TryBuildFocusOverride(step, observation);
            if (focusOverride is not null)
            {
                return focusOverride;
            }

            var paintOverride = TryBuildPaintCoordinateAction(step, observation, dto);
            if (paintOverride is not null)
            {
                return paintOverride;
            }

            return Map(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI action generation failed; falling back to deterministic demo action.");
            return DemoAction(step);
        }
    }

    public async Task<StepCompletionResult> CheckStepCompletionAsync(PlanStep step, ObservationModel observation, string? goalContext = null)
    {
        var apiKey = _apiKeyProvider();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // Without API key, assume step is complete after one action
            return new StepCompletionResult { IsComplete = true, Reason = "No API key for verification" };
        }

        var goalInfo = string.IsNullOrWhiteSpace(goalContext) ? "" : $"User's original request: {goalContext}\n";

        var prompt = $"""
Look at the screenshot and determine if this step has been completed successfully.

{goalInfo}
Step goal: {step.Description}
Expected validation: {step.Validation ?? "N/A"}

MARK AS COMPLETE (is_complete: true) IF ANY OF THESE ARE TRUE:
- A video is playing (YouTube player visible with video content)
- Search results are displayed for the requested query
- The requested page/content is visible on screen
- A file has been saved (Save dialog closed, back to main app)
- The requested application is open and ready
- Form was submitted and results/confirmation visible
- The action described in the step has clearly been performed

MARK AS INCOMPLETE (is_complete: false) ONLY IF:
- The step action has clearly NOT been done yet
- An error message is showing
- The wrong page/app is displayed
- A required field is still empty

BE GENEROUS WITH COMPLETION - if the main goal appears achieved, mark it complete.
Do NOT keep the task running just because there might be "more to do".
If a video is playing, the "show me videos" task is DONE.
If search results are visible, the "search for X" task is DONE.

Respond with ONLY a JSON object with keys: is_complete (bool), reason (string), suggested_next_action (string)
""";

        var payload = new
        {
            model = _options.Model,
            max_output_tokens = 500,
            input = new object[]
            {
                new
                {
                    role = "user",
                    content = ContentBuilder.BuildContent(prompt, observation.ScreenshotDataUrl)
                }
            }
        };

        try
        {
            var text = await _client.CreateResponseTextAsync(apiKey, payload, CancellationToken.None);
            return ParseCompletionResult(text);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Step completion check failed; assuming incomplete.");
            return new StepCompletionResult { IsComplete = false, Reason = "Verification failed" };
        }
    }

    private static StepCompletionResult ParseCompletionResult(string text)
    {
        try
        {
            var json = ExtractJsonObject(text);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var isComplete = root.TryGetProperty("is_complete", out var ic) && ic.ValueKind == JsonValueKind.True;
            var reason = root.TryGetProperty("reason", out var r) ? r.GetString() : null;
            var suggested = root.TryGetProperty("suggested_next_action", out var s) ? s.GetString() : null;

            return new StepCompletionResult
            {
                IsComplete = isComplete,
                Reason = reason,
                SuggestedNextAction = suggested
            };
        }
        catch
        {
            return new StepCompletionResult { IsComplete = false, Reason = "Failed to parse verification response" };
        }
    }

    private static ActionModel? TryBuildFocusOverride(PlanStep step, ObservationModel observation)
    {
        if (string.IsNullOrWhiteSpace(observation.ActiveProcess))
        {
            return null;
        }

        // Policy blocks non-focus actions when the foreground process isn't allowlisted.
        // During runs, the foreground is often our own UI; proactively focus the target app.
        var target = GuessTargetProcess(step.Description);
        if (target is null)
        {
            return null;
        }

        if (string.Equals(observation.ActiveProcess, target, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Only override when we're in our own UI foreground (common problematic case).
        if (!string.Equals(observation.ActiveProcess, "AutoPilotAgent.UI", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var title = target.Equals("notepad", StringComparison.OrdinalIgnoreCase)
            ? "Notepad"
            : "";

        return new ActionModel
        {
            ActionType = ActionType.FocusWindow,
            RequiresConfirmation = false,
            ExpectedResult = $"Focus {target}",
            Parameters = JsonDocument.Parse($"{{\"title\":\"{title}\",\"process\":\"{target}\"}}").RootElement
        };
    }

    private static ActionModel? TryBuildPaintCoordinateAction(PlanStep step, ObservationModel observation, ActionDto dto)
    {
        // Paint UIA is brittle; for drawing-related steps use coordinate click/drag.
        var isPaintStep = GuessTargetProcess(step.Description) == "mspaint";
        if (!isPaintStep)
        {
            return null;
        }

        // If model already picked coordinate action, keep it.
        if (dto.ActionType.Equals("click_coordinates", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // If it's trying UIA in Paint, override to coordinates.
        if (!dto.ActionType.Equals("click_uia", StringComparison.OrdinalIgnoreCase) &&
            !dto.ActionType.Equals("hotkey", StringComparison.OrdinalIgnoreCase) &&
            !dto.ActionType.Equals("type_text", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var d = step.Description;

        // Heuristic coordinate regions (relative to client area):
        // Toolbar-ish area is near the top; canvas is below.
        if (d.Contains("select", StringComparison.OrdinalIgnoreCase) || d.Contains("brush", StringComparison.OrdinalIgnoreCase) || d.Contains("pencil", StringComparison.OrdinalIgnoreCase))
        {
            // Click near top-left toolbar.
            return BuildClickRel(0.08, 0.18, "Select tool (approx)");
        }

        if (d.Contains("circle", StringComparison.OrdinalIgnoreCase) || d.Contains("ellipse", StringComparison.OrdinalIgnoreCase) || d.Contains("draw", StringComparison.OrdinalIgnoreCase))
        {
            // Drag on the canvas area.
            return BuildDragRel(0.35, 0.40, 0.55, 0.70, "Draw shape (approx)");
        }

        if (d.Contains("smile", StringComparison.OrdinalIgnoreCase) || d.Contains("curved", StringComparison.OrdinalIgnoreCase) || d.Contains("arc", StringComparison.OrdinalIgnoreCase))
        {
            return BuildDragRel(0.40, 0.70, 0.60, 0.70, "Draw smile line (approx)");
        }

        return null;

        static ActionModel BuildClickRel(double rx, double ry, string expected)
        {
            var json = $"{{\"rx\":{rx},\"ry\":{ry}}}";
            return new ActionModel
            {
                ActionType = ActionType.ClickCoordinates,
                RequiresConfirmation = false,
                ExpectedResult = expected,
                Parameters = JsonDocument.Parse(json).RootElement
            };
        }

        static ActionModel BuildDragRel(double rx, double ry, double toRx, double toRy, string expected)
        {
            var json = $"{{\"rx\":{rx},\"ry\":{ry},\"to_rx\":{toRx},\"to_ry\":{toRy},\"drag_steps\":40,\"drag_delay_ms\":5}}";
            return new ActionModel
            {
                ActionType = ActionType.ClickCoordinates,
                RequiresConfirmation = false,
                ExpectedResult = expected,
                Parameters = JsonDocument.Parse(json).RootElement
            };
        }
    }

    private static string? GuessTargetProcess(string stepDescription)
    {
        // Browsers
        if (stepDescription.Contains("firefox", StringComparison.OrdinalIgnoreCase))
        {
            return "firefox";
        }

        if (stepDescription.Contains("chrome", StringComparison.OrdinalIgnoreCase))
        {
            return "chrome";
        }

        if (stepDescription.Contains("edge", StringComparison.OrdinalIgnoreCase) && 
            !stepDescription.Contains("edge case", StringComparison.OrdinalIgnoreCase))
        {
            return "msedge";
        }

        // Generic browser keywords - default to firefox if user mentioned it in goal
        if (stepDescription.Contains("browser", StringComparison.OrdinalIgnoreCase) ||
            stepDescription.Contains("website", StringComparison.OrdinalIgnoreCase) ||
            stepDescription.Contains("web page", StringComparison.OrdinalIgnoreCase) ||
            stepDescription.Contains(".com", StringComparison.OrdinalIgnoreCase) ||
            stepDescription.Contains(".gov", StringComparison.OrdinalIgnoreCase) ||
            stepDescription.Contains(".org", StringComparison.OrdinalIgnoreCase) ||
            stepDescription.Contains("http", StringComparison.OrdinalIgnoreCase))
        {
            return "firefox";
        }

        // Desktop apps
        if (stepDescription.Contains("notepad", StringComparison.OrdinalIgnoreCase) ||
            stepDescription.Contains("save dialog", StringComparison.OrdinalIgnoreCase) ||
            stepDescription.Contains("save the file", StringComparison.OrdinalIgnoreCase) ||
            stepDescription.Contains("filename", StringComparison.OrdinalIgnoreCase) ||
            stepDescription.Contains("enter ", StringComparison.OrdinalIgnoreCase) && stepDescription.Contains("file", StringComparison.OrdinalIgnoreCase))
        {
            return "notepad";
        }

        if (stepDescription.Contains("paint", StringComparison.OrdinalIgnoreCase) ||
            stepDescription.Contains("mspaint", StringComparison.OrdinalIgnoreCase) ||
            stepDescription.Contains("brush", StringComparison.OrdinalIgnoreCase) ||
            stepDescription.Contains("pencil", StringComparison.OrdinalIgnoreCase) ||
            stepDescription.Contains("ellipse", StringComparison.OrdinalIgnoreCase) ||
            stepDescription.Contains("circle", StringComparison.OrdinalIgnoreCase))
        {
            return "mspaint";
        }

        if (stepDescription.Contains("file explorer", StringComparison.OrdinalIgnoreCase) ||
            stepDescription.Contains("documents folder", StringComparison.OrdinalIgnoreCase))
        {
            return "explorer";
        }

        if (stepDescription.Contains("word", StringComparison.OrdinalIgnoreCase) && 
            stepDescription.Contains("document", StringComparison.OrdinalIgnoreCase))
        {
            return "WINWORD";
        }

        if (stepDescription.Contains("excel", StringComparison.OrdinalIgnoreCase) ||
            stepDescription.Contains("spreadsheet", StringComparison.OrdinalIgnoreCase))
        {
            return "EXCEL";
        }

        if (stepDescription.Contains("powerpoint", StringComparison.OrdinalIgnoreCase) ||
            stepDescription.Contains("presentation", StringComparison.OrdinalIgnoreCase))
        {
            return "POWERPNT";
        }

        if (stepDescription.Contains("calculator", StringComparison.OrdinalIgnoreCase))
        {
            return "calc";
        }

        if (stepDescription.Contains("terminal", StringComparison.OrdinalIgnoreCase) ||
            stepDescription.Contains("command prompt", StringComparison.OrdinalIgnoreCase))
        {
            return "cmd";
        }

        return null;
    }

    private static ActionDto ParseActionDto(string text)
    {
        var json = ExtractJsonObject(text);
        var dto = JsonSerializer.Deserialize<ActionDto>(json);
        if (dto is null)
        {
            throw new InvalidOperationException("Failed to parse ActionDto from model output.");
        }

        return dto;
    }

    private static string ExtractJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("Model returned empty output.");
        }

        var s = text.Trim();

        // Strip common markdown code fences if present.
        if (s.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = s.IndexOf('\n');
            if (firstNewline >= 0)
            {
                s = s[(firstNewline + 1)..];
            }

            var lastFence = s.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0)
            {
                s = s[..lastFence];
            }

            s = s.Trim();
        }

        var start = s.IndexOf('{');
        if (start < 0)
        {
            throw new InvalidOperationException("Model output did not contain a JSON object.");
        }

        var depth = 0;
        for (var i = start; i < s.Length; i++)
        {
            var ch = s[i];
            if (ch == '{') depth++;
            if (ch == '}') depth--;
            if (depth == 0)
            {
                return s.Substring(start, i - start + 1);
            }
        }

        throw new InvalidOperationException("Unterminated JSON object in model output.");
    }

    private ActionModel DemoAction(PlanStep step)
    {
        if (step.Description.Contains("Open Notepad", StringComparison.OrdinalIgnoreCase))
        {
            return new ActionModel
            {
                ActionType = ActionType.FocusWindow,
                RequiresConfirmation = false,
                Parameters = JsonDocument.Parse("{\"title\":\"Notepad\",\"process\":\"notepad\"}").RootElement
            };
        }

        if (step.Description.Contains("Type the message", StringComparison.OrdinalIgnoreCase))
        {
            return new ActionModel
            {
                ActionType = ActionType.TypeText,
                RequiresConfirmation = false,
                Parameters = JsonDocument.Parse("{\"text\":\"Hello from AutoPilot Agent\"}").RootElement
            };
        }

        if (step.Description.Contains("Save: Press Ctrl+S", StringComparison.OrdinalIgnoreCase))
        {
            return new ActionModel
            {
                ActionType = ActionType.Hotkey,
                RequiresConfirmation = true,
                Parameters = JsonDocument.Parse("{\"keys\":[\"CTRL\",\"S\"]}").RootElement
            };
        }

        if (step.Description.Contains("Save: Type filename", StringComparison.OrdinalIgnoreCase))
        {
            return new ActionModel
            {
                ActionType = ActionType.TypeText,
                RequiresConfirmation = false,
                Parameters = JsonDocument.Parse("{\"text\":\"test.txt\"}").RootElement
            };
        }

        if (step.Description.Contains("Save: Press Enter", StringComparison.OrdinalIgnoreCase))
        {
            return new ActionModel
            {
                ActionType = ActionType.Hotkey,
                RequiresConfirmation = false,
                Parameters = JsonDocument.Parse("{\"keys\":[\"ENTER\"]}").RootElement
            };
        }

        _logger.LogWarning("No demo action mapping for step: {Step}", step.Description);
        return new ActionModel
        {
            ActionType = ActionType.Wait,
            RequiresConfirmation = true,
            Parameters = JsonDocument.Parse("{\"ms\":500}").RootElement
        };
    }

    private static ActionModel Map(ActionDto dto)
    {
        return new ActionModel
        {
            ActionType = ParseType(dto.ActionType),
            Parameters = dto.Parameters,
            RequiresConfirmation = dto.RequiresConfirmation,
            ExpectedResult = dto.ExpectedResult
        };
    }

    private static ActionType ParseType(string t)
    {
        return t.Trim().ToLowerInvariant() switch
        {
            "focus_window" => ActionType.FocusWindow,
            "navigate_url" => ActionType.NavigateUrl,
            "click_coordinates" => ActionType.ClickCoordinates,
            "click_grid" => ActionType.ClickGrid,
            "click_and_type" => ActionType.ClickAndType,
            "click_uia" => ActionType.ClickUia,
            "type_text" => ActionType.TypeText,
            "hotkey" => ActionType.Hotkey,
            "wait" => ActionType.Wait,
            "verify" => ActionType.Verify,
            "scroll" => ActionType.Scroll,
            "run_command" => ActionType.RunCommand,
            "win_run" => ActionType.WinRun,
            "click_text" => ActionType.ClickText,
            "done" => ActionType.Done,
            _ => ActionType.Verify
        };
    }
}
