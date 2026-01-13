using System.Text.Json;
using System.Diagnostics;
using AutoPilotAgent.Automation.UIA;
using AutoPilotAgent.Automation.Win32;
using AutoPilotAgent.Core.Interfaces;
using AutoPilotAgent.Core.Models;
using Microsoft.Extensions.Logging;

namespace AutoPilotAgent.Automation.Services;

public sealed class ActionExecutor : IActionExecutor
{
    private readonly InputSender _input;
    private readonly WindowManager _windows;
    private readonly UIAutomationHelper _uia;
    private readonly ILogger<ActionExecutor> _logger;
    private readonly ScreenTextFinder _textFinder;

    public ActionExecutor(InputSender input, WindowManager windows, UIAutomationHelper uia, ILogger<ActionExecutor> logger)
    {
        _input = input;
        _windows = windows;
        _uia = uia;
        _logger = logger;
        _textFinder = new ScreenTextFinder();
    }

    public async Task<ExecutionResult> ExecuteAsync(ActionModel action)
    {
        try
        {
            // ClickText needs async for OCR
            if (action.ActionType == ActionType.ClickText)
            {
                return await ClickTextAsync(action.Parameters);
            }
            return ExecuteInternal(action);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Action execution threw.");
            return new ExecutionResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private ExecutionResult ExecuteInternal(ActionModel action)
    {
        return action.ActionType switch
        {
            ActionType.FocusWindow => FocusWindow(action.Parameters),
            ActionType.ClickCoordinates => ClickCoordinates(action.Parameters),
            ActionType.ClickGrid => ClickGrid(action.Parameters),
            ActionType.ClickAndType => ClickAndType(action.Parameters),
            ActionType.ClickUia => ClickUia(action.Parameters),
            ActionType.TypeText => TypeText(action.Parameters),
            ActionType.Hotkey => Hotkey(action.Parameters),
            ActionType.Wait => Wait(action.Parameters),
            ActionType.Verify => new ExecutionResult { Success = true },
            ActionType.NavigateUrl => NavigateUrl(action.Parameters),
            ActionType.Scroll => Scroll(action.Parameters),
            ActionType.RunCommand => RunCommand(action.Parameters),
            ActionType.WinRun => WinRun(action.Parameters),
            ActionType.ClickText => new ExecutionResult { Success = false, ErrorMessage = "Use async path" },
            _ => new ExecutionResult { Success = false, ErrorMessage = $"Unsupported action type: {action.ActionType}" }
        };
    }

    private ExecutionResult FocusWindow(JsonElement parameters)
    {
        var title = GetString(parameters, "title");
        var process = GetString(parameters, "process");

        var ok = _windows.FocusByTitleOrProcess(title, process);
        if (!ok && !string.IsNullOrWhiteSpace(process))
        {
            try
            {
                _ = Process.Start(new ProcessStartInfo
                {
                    FileName = process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? process : $"{process}.exe",
                    UseShellExecute = true
                });

                Thread.Sleep(500);
                ok = _windows.FocusByTitleOrProcess(title, process);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to start process {Process}", process);
            }
        }

        return ok
            ? new ExecutionResult { Success = true }
            : new ExecutionResult { Success = false, ErrorMessage = "Failed to focus or start target window." };
    }

    private ExecutionResult ClickCoordinates(JsonElement parameters)
    {
        // Multi-stroke path:
        //   {"path":[{"rx":0.2,"ry":0.3,"lift":true},{"rx":0.3,"ry":0.3}, ...]}
        // A point with lift=true indicates starting a new stroke at that point.
        if (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("path", out var pathEl) && pathEl.ValueKind == JsonValueKind.Array)
        {
            return ExecutePath(pathEl, parameters);
        }

        // Supports absolute:
        //   {"x":100,"y":100}
        // Supports drag absolute:
        //   {"x":100,"y":100,"to_x":200,"to_y":200}
        // Supports window-relative 0..1 (foreground window):
        //   {"rx":0.5,"ry":0.5}
        // Supports drag relative:
        //   {"rx":0.3,"ry":0.3,"to_rx":0.7,"to_ry":0.7}

        double rx;
        double ry;
        double toRx;
        double toRy;

        var hasRelX = TryGetDouble(parameters, "rx", out rx);
        var hasRelY = TryGetDouble(parameters, "ry", out ry);
        var hasRel = hasRelX && hasRelY;

        var hasRelToX = TryGetDouble(parameters, "to_rx", out toRx);
        var hasRelToY = TryGetDouble(parameters, "to_ry", out toRy);
        var hasRelTo = hasRelToX && hasRelToY;

        var hasAbs = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("x", out _) && parameters.TryGetProperty("y", out _);
        var x = GetInt(parameters, "x");
        var y = GetInt(parameters, "y");
        var toX = GetInt(parameters, "to_x");
        var toY = GetInt(parameters, "to_y");
        var hasAbsTo = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("to_x", out _) && parameters.TryGetProperty("to_y", out _);

        var dragSteps = GetInt(parameters, "drag_steps");
        var dragDelayMs = GetInt(parameters, "drag_delay_ms");
        if (dragSteps <= 0) dragSteps = 24;
        if (dragDelayMs <= 0) dragDelayMs = 5;

        if (hasRel)
        {
            if (!_windows.TryGetForegroundClientRectScreen(out var rect))
            {
                return new ExecutionResult { Success = false, ErrorMessage = "Unable to get foreground window rect." };
            }

            var w = Math.Max(1, rect.Right - rect.Left);
            var h = Math.Max(1, rect.Bottom - rect.Top);

            var fromX = rect.Left + (int)Math.Round(Math.Clamp(rx, 0, 1) * w);
            var fromY = rect.Top + (int)Math.Round(Math.Clamp(ry, 0, 1) * h);

            if (hasRelTo)
            {
                var endX = rect.Left + (int)Math.Round(Math.Clamp(toRx, 0, 1) * w);
                var endY = rect.Top + (int)Math.Round(Math.Clamp(toRy, 0, 1) * h);
                _input.DragAbsolute(fromX, fromY, endX, endY, dragSteps, dragDelayMs);
                return new ExecutionResult { Success = true };
            }

            _input.ClickAbsolute(fromX, fromY);
            return new ExecutionResult { Success = true };
        }

        if (hasAbs)
        {
            if (hasAbsTo)
            {
                _input.DragAbsolute(x, y, toX, toY, dragSteps, dragDelayMs);
                return new ExecutionResult { Success = true };
            }

            _input.ClickAbsolute(x, y);
            return new ExecutionResult { Success = true };
        }

        return new ExecutionResult { Success = false, ErrorMessage = "click_coordinates missing coordinates (x/y or rx/ry)." };
    }

    private ExecutionResult ExecutePath(JsonElement pathEl, JsonElement parameters)
    {
        if (!_windows.TryGetForegroundClientRectScreen(out var rect))
        {
            return new ExecutionResult { Success = false, ErrorMessage = "Unable to get foreground window rect." };
        }

        var w = Math.Max(1, rect.Right - rect.Left);
        var h = Math.Max(1, rect.Bottom - rect.Top);

        var moveDelayMs = GetInt(parameters, "move_delay_ms");
        if (moveDelayMs <= 0) moveDelayMs = 3;

        var penDown = false;
        var havePoint = false;

        foreach (var p in pathEl.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var lift = false;
            if (p.TryGetProperty("lift", out var liftEl) && liftEl.ValueKind == JsonValueKind.True)
            {
                lift = true;
            }

            if (!TryResolvePoint(p, rect, w, h, out var px, out var py))
            {
                continue;
            }

            havePoint = true;

            if (lift && penDown)
            {
                _input.LeftUp();
                penDown = false;
                Thread.Sleep(5);
            }

            if (!penDown)
            {
                _input.MoveMouseAbsolute(px, py);
                Thread.Sleep(10);
                _input.LeftDown();
                penDown = true;
                Thread.Sleep(10);
                continue;
            }

            _input.MoveMouseAbsolute(px, py);
            if (moveDelayMs > 0)
            {
                Thread.Sleep(moveDelayMs);
            }
        }

        if (!havePoint)
        {
            return new ExecutionResult { Success = false, ErrorMessage = "Empty path." };
        }

        if (penDown)
        {
            Thread.Sleep(10);
            _input.LeftUp();
        }

        return new ExecutionResult { Success = true };
    }

    private static bool TryResolvePoint(JsonElement point, (int Left, int Top, int Right, int Bottom) rect, int w, int h, out int x, out int y)
    {
        // Square min-dimension coordinates (mrx/mry) in [0..1], centered inside the client rect.
        if (point.TryGetProperty("mrx", out var mrxEl) && point.TryGetProperty("mry", out var mryEl) &&
            mrxEl.ValueKind == JsonValueKind.Number && mryEl.ValueKind == JsonValueKind.Number &&
            mrxEl.TryGetDouble(out var mrx) && mryEl.TryGetDouble(out var mry))
        {
            var size = Math.Max(1, Math.Min(w, h));
            var offsetX = rect.Left + (w - size) / 2;
            var offsetY = rect.Top + (h - size) / 2;

            x = offsetX + (int)Math.Round(Math.Clamp(mrx, 0, 1) * size);
            y = offsetY + (int)Math.Round(Math.Clamp(mry, 0, 1) * size);
            return true;
        }

        // Prefer rx/ry.
        if (point.TryGetProperty("rx", out var rxEl) && point.TryGetProperty("ry", out var ryEl) &&
            rxEl.ValueKind == JsonValueKind.Number && ryEl.ValueKind == JsonValueKind.Number &&
            rxEl.TryGetDouble(out var rx) && ryEl.TryGetDouble(out var ry))
        {
            x = rect.Left + (int)Math.Round(Math.Clamp(rx, 0, 1) * w);
            y = rect.Top + (int)Math.Round(Math.Clamp(ry, 0, 1) * h);
            return true;
        }

        if (point.TryGetProperty("x", out var xEl) && point.TryGetProperty("y", out var yEl) &&
            xEl.TryGetInt32(out x) && yEl.TryGetInt32(out y))
        {
            return true;
        }

        x = 0;
        y = 0;
        return false;
    }

    private ExecutionResult ClickUia(JsonElement parameters)
    {
        var automationId = GetString(parameters, "automation_id");
        var name = GetString(parameters, "name");

        var element = _uia.FindByAutomationIdOrName(automationId, name);
        if (element is null)
        {
            return new ExecutionResult { Success = false, ErrorMessage = "UIA element not found." };
        }

        if (_uia.IsPasswordElement(element))
        {
            return new ExecutionResult { Success = false, ErrorMessage = "Password field interaction blocked." };
        }

        var ok = _uia.TryInvoke(element);
        if (!ok)
        {
            try
            {
                element.SetFocus();
                ok = true;
            }
            catch
            {
                ok = false;
            }
        }

        return ok ? new ExecutionResult { Success = true } : new ExecutionResult { Success = false, ErrorMessage = "UIA click failed." };
    }

    private ExecutionResult TypeText(JsonElement parameters)
    {
        var text = GetString(parameters, "text") ?? string.Empty;
        
        if (string.IsNullOrEmpty(text))
        {
            return new ExecutionResult { Success = false, ErrorMessage = "No text to type" };
        }

        // Check if our own UI is foreground - if so, we can't type into the target app
        if (_windows.TryGetForegroundProcessName(out var fgProcess) && 
            fgProcess.Contains("AutoPilotAgent", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("TypeText: Our UI is foreground, cannot type into target app");
            return new ExecutionResult { Success = false, ErrorMessage = "Agent UI is foreground - need to focus target app first" };
        }

        // Optional safety: if caller specifies a UIA element, check IsPassword before typing.
        var automationId = GetString(parameters, "uia_automation_id");
        var name = GetString(parameters, "uia_name");
        if (!string.IsNullOrWhiteSpace(automationId) || !string.IsNullOrWhiteSpace(name))
        {
            var element = _uia.FindByAutomationIdOrName(automationId, name);
            if (element is not null)
            {
                if (_uia.IsPasswordElement(element))
                {
                    return new ExecutionResult { Success = false, ErrorMessage = "Password typing blocked." };
                }

                try
                {
                    element.SetFocus();
                }
                catch
                {
                    // Ignore focus failure; still attempt typing.
                }
            }
        }

        _logger.LogInformation("TypeText: Typing '{Text}' ({Length} chars)", text, text.Length);
        _input.TypeText(text);
        Thread.Sleep(100);
        return new ExecutionResult { Success = true, Details = $"Typed: {text}" };
    }

    private ExecutionResult Hotkey(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("keys", out var keysEl) || keysEl.ValueKind != JsonValueKind.Array)
        {
            return new ExecutionResult { Success = false, ErrorMessage = "Missing keys array." };
        }

        var keys = new List<ushort>();
        foreach (var k in keysEl.EnumerateArray())
        {
            if (k.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            keys.Add(VirtualKey.FromName(k.GetString()!));
        }

        if (keys.Count == 0)
        {
            return new ExecutionResult { Success = false, ErrorMessage = "No keys provided." };
        }

        _input.Hotkey(keys.ToArray());
        return new ExecutionResult { Success = true };
    }

    private static ExecutionResult Wait(JsonElement parameters)
    {
        var ms = GetInt(parameters, "ms");
        Thread.Sleep(Math.Clamp(ms, 0, 60_000));
        return new ExecutionResult { Success = true };
    }

    private ExecutionResult NavigateUrl(JsonElement parameters)
    {
        var url = GetString(parameters, "url");
        if (string.IsNullOrWhiteSpace(url))
        {
            return new ExecutionResult { Success = false, ErrorMessage = "Missing url parameter." };
        }

        var browser = GetString(parameters, "browser");

        try
        {
            // If a specific browser is requested, try to use it
            if (!string.IsNullOrWhiteSpace(browser))
            {
                var browserExe = browser.ToLowerInvariant() switch
                {
                    "firefox" => "firefox.exe",
                    "chrome" => "chrome.exe",
                    "edge" or "msedge" => "msedge.exe",
                    _ => $"{browser}.exe"
                };

                Process.Start(new ProcessStartInfo
                {
                    FileName = browserExe,
                    Arguments = url,
                    UseShellExecute = true
                });
            }
            else
            {
                // Use default browser via shell
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }

            Thread.Sleep(1500);
            return new ExecutionResult { Success = true };
        }
        catch (Exception ex)
        {
            return new ExecutionResult { Success = false, ErrorMessage = $"Failed to navigate: {ex.Message}" };
        }
    }

    private ExecutionResult ClickGrid(JsonElement parameters)
    {
        var cell = GetInt(parameters, "cell");
        if (cell < 1 || cell > ObservationService.TotalCells)
        {
            return new ExecutionResult { Success = false, ErrorMessage = $"Invalid grid cell: {cell}. Must be 1-{ObservationService.TotalCells}." };
        }

        var (x, y, cellInfo) = GetGridCellScreenPositionWithInfo(cell);
        if (x < 0)
        {
            return new ExecutionResult { Success = false, ErrorMessage = "Unable to get foreground window rect." };
        }

        _logger.LogInformation("ClickGrid cell {Cell} ({CellInfo}) -> screen ({X}, {Y})", cell, cellInfo, x, y);
        _input.ClickAbsolute(x, y);
        Thread.Sleep(100);

        return new ExecutionResult { Success = true, Details = $"Clicked cell {cell} at ({x}, {y})" };
    }

    private ExecutionResult ClickAndType(JsonElement parameters)
    {
        var cell = GetInt(parameters, "cell");
        var text = GetString(parameters, "text") ?? "";

        if (cell < 1 || cell > ObservationService.TotalCells)
        {
            return new ExecutionResult { Success = false, ErrorMessage = $"Invalid grid cell: {cell}. Must be 1-{ObservationService.TotalCells}." };
        }

        var (x, y, cellInfo) = GetGridCellScreenPositionWithInfo(cell);
        if (x < 0)
        {
            return new ExecutionResult { Success = false, ErrorMessage = "Unable to get foreground window rect." };
        }

        _logger.LogInformation("ClickAndType cell {Cell} ({CellInfo}) -> ({X}, {Y}), typing: \"{Text}\"", cell, cellInfo, x, y, text);
        
        // Single click to focus, then triple-click to select
        _input.ClickAbsolute(x, y);
        Thread.Sleep(100);
        _input.ClickAbsolute(x, y);
        Thread.Sleep(50);
        _input.ClickAbsolute(x, y);
        Thread.Sleep(50);
        _input.ClickAbsolute(x, y);
        Thread.Sleep(150);
        
        // Type the text (replaces selected content)
        _input.TypeText(text);
        Thread.Sleep(300);

        return new ExecutionResult { Success = true, Details = $"Typed \"{text}\" in cell {cell} at ({x}, {y})" };
    }

    private (int x, int y, string info) GetGridCellScreenPositionWithInfo(int cell)
    {
        if (!_windows.TryGetForegroundClientRectScreen(out var rect))
        {
            return (-1, -1, "no window");
        }

        var width = Math.Max(1, rect.Right - rect.Left);
        var height = Math.Max(1, rect.Bottom - rect.Top);

        var cols = ObservationService.GridCols;
        var rows = ObservationService.GridRows;

        var zeroIndex = cell - 1;
        var row = zeroIndex / cols;
        var col = zeroIndex % cols;

        var cellWidth = width / (float)cols;
        var cellHeight = height / (float)rows;

        var x = rect.Left + (int)(col * cellWidth + cellWidth / 2);
        var y = rect.Top + (int)(row * cellHeight + cellHeight / 2);

        var info = $"row {row + 1}/{rows}, col {col + 1}/{cols}";
        return (x, y, info);
    }

    private ExecutionResult Scroll(JsonElement parameters)
    {
        // direction: "up" or "down", amount: pixels or "page"
        var direction = GetString(parameters, "direction") ?? "down";
        var amountStr = GetString(parameters, "amount") ?? "300";
        
        var isUp = direction.Equals("up", StringComparison.OrdinalIgnoreCase);
        
        int scrollAmount;
        if (amountStr.Equals("page", StringComparison.OrdinalIgnoreCase))
        {
            scrollAmount = 500;
        }
        else
        {
            scrollAmount = int.TryParse(amountStr, out var a) ? a : 300;
        }

        // Use mouse wheel scroll (positive = up, negative = down)
        var delta = isUp ? scrollAmount : -scrollAmount;
        _input.MouseWheel(delta);
        
        Thread.Sleep(300);
        return new ExecutionResult { Success = true };
    }

    private ExecutionResult WinRun(JsonElement parameters)
    {
        var command = GetString(parameters, "command") ?? "";
        if (string.IsNullOrWhiteSpace(command))
        {
            return new ExecutionResult { Success = false, ErrorMessage = "No command specified for WinRun" };
        }

        _logger.LogInformation("WinRun: Opening Run dialog and executing '{Command}'", command);

        // Press Win+R to open Run dialog
        _input.Hotkey(VirtualKey.LWIN, VirtualKey.FromName("R"));
        Thread.Sleep(500);

        // Type the command
        _input.TypeText(command);
        Thread.Sleep(200);

        // Press Enter to execute
        _input.Hotkey(VirtualKey.RETURN);
        Thread.Sleep(1000);

        return new ExecutionResult { Success = true, Details = $"WinRun: {command}" };
    }

    private async Task<ExecutionResult> ClickTextAsync(JsonElement parameters)
    {
        var text = GetString(parameters, "text") ?? "";
        var index = GetInt(parameters, "index"); // 0 = first match, 1 = second, etc.
        
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ExecutionResult { Success = false, ErrorMessage = "No text specified for click_text" };
        }

        if (!_windows.TryGetForegroundClientRectScreen(out var rect))
        {
            return new ExecutionResult { Success = false, ErrorMessage = "Cannot get foreground window" };
        }

        var screenRect = new System.Drawing.Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        
        _logger.LogInformation("ClickText: Searching for '{Text}' (index {Index})...", text, index);
        
        var matches = await _textFinder.FindTextOnScreenAsync(screenRect, text);
        
        if (matches.Count == 0)
        {
            _logger.LogWarning("ClickText: No matches found for '{Text}'", text);
            return new ExecutionResult { Success = false, ErrorMessage = $"Text '{text}' not found on screen" };
        }

        if (index >= matches.Count)
        {
            index = 0;
        }

        var match = matches[index];
        var center = match.Center;
        
        _logger.LogInformation("ClickText: Found '{FoundText}' at ({X}, {Y}) - clicking", match.Text, center.X, center.Y);
        
        _input.ClickAbsolute(center.X, center.Y);
        Thread.Sleep(100);

        return new ExecutionResult { Success = true, Details = $"Clicked on '{match.Text}' at ({center.X}, {center.Y})" };
    }

    private ExecutionResult RunCommand(JsonElement parameters)
    {
        var command = GetString(parameters, "command") ?? "";
        var shell = GetString(parameters, "shell") ?? "powershell";

        if (string.IsNullOrWhiteSpace(command))
        {
            return new ExecutionResult { Success = false, ErrorMessage = "No command specified" };
        }

        _logger.LogInformation("RunCommand [{Shell}]: {Command}", shell, command);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = shell,
                Arguments = shell.Contains("powershell", StringComparison.OrdinalIgnoreCase) 
                    ? $"-Command \"{command}\"" 
                    : $"/c {command}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                return new ExecutionResult { Success = false, ErrorMessage = "Failed to start process" };
            }

            proc.WaitForExit(10000);
            var output = proc.StandardOutput.ReadToEnd();
            var error = proc.StandardError.ReadToEnd();

            if (proc.ExitCode != 0 && !string.IsNullOrWhiteSpace(error))
            {
                return new ExecutionResult { Success = false, ErrorMessage = error, Details = output };
            }

            return new ExecutionResult { Success = true, Details = output.Length > 200 ? output[..200] + "..." : output };
        }
        catch (Exception ex)
        {
            return new ExecutionResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private static string? GetString(JsonElement obj, string name)
    {
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String)
        {
            return el.GetString();
        }
        return null;
    }

    private static int GetInt(JsonElement obj, string name)
    {
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var el) && el.TryGetInt32(out var v))
        {
            return v;
        }
        return 0;
    }

    private static bool TryGetDouble(JsonElement obj, string name, out double value)
    {
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var el))
        {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }
}
