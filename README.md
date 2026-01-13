# AutoPilot Agent

An AI-powered desktop automation agent for Windows that uses vision and natural language to complete tasks on your computer.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![Windows](https://img.shields.io/badge/Platform-Windows%2010%2F11-0078D6)
![License](https://img.shields.io/badge/License-MIT-green)

## Features

- **Natural Language Tasks** - Describe what you want to do in plain English
- **Vision-Based Automation** - Uses screenshots and OCR to understand the screen
- **AI-Powered Planning** - Breaks down complex tasks into executable steps
- **Smart Click Detection** - OCR-based text clicking for precise interactions
- **Safety Controls** - Policy enforcement, confirmation dialogs, and process allowlists
- **Real-Time Feedback** - Watch the agent work with live status updates

## Demo

Ask the agent to:
- "Open Notepad, type 'Hello World', and save it as test.txt"
- "Open Firefox and search for funny cat videos on YouTube"
- "Open Settings and change the wallpaper color to blue"

## Requirements

- Windows 10/11 (64-bit)
- [OpenAI API Key](https://platform.openai.com/api-keys) (GPT-4 Vision or GPT-5.2)

## Installation

### Option 1: Download Release
Download the latest `AutoPilotAgent-Setup-x.x.x.exe` from [Releases](../../releases).

### Option 2: Build from Source

```powershell
# Clone the repository
git clone https://github.com/yourusername/autopc.git
cd autopc

# Build
dotnet build -c Release

# Run
dotnet run --project src/AutoPilotAgent.UI
```

### Option 3: Create Standalone Executable

```powershell
# Publish self-contained single-file executable
dotnet publish src/AutoPilotAgent.UI -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

## Configuration

1. Launch AutoPilot Agent
2. Enter your OpenAI API key in the settings
3. (Optional) Configure the process allowlist for additional apps

## Architecture

```
AutoPilotAgent/
├── AutoPilotAgent.UI          # WPF user interface
├── AutoPilotAgent.Core        # Core models and orchestration
├── AutoPilotAgent.OpenAI      # OpenAI API integration
├── AutoPilotAgent.Automation  # Windows automation (input, OCR, screenshots)
├── AutoPilotAgent.Policy      # Safety policies and enforcement
├── AutoPilotAgent.Storage     # Settings persistence
└── AutoPilotAgent.Logging     # Structured logging
```

### Key Components

| Component | Description |
|-----------|-------------|
| `AgentOrchestrator` | Main execution loop that coordinates planning and actions |
| `OpenAIPlanService` | Generates step-by-step plans from natural language goals |
| `OpenAIActionService` | Determines next action based on screenshots and context |
| `ActionExecutor` | Executes actions (clicks, typing, hotkeys, etc.) |
| `ScreenTextFinder` | OCR-based text detection using Windows.Media.Ocr |
| `PolicyEngine` | Enforces safety rules and process allowlists |

## Supported Actions

| Action | Description |
|--------|-------------|
| `win_run` | Open apps via Win+R (notepad, calc, ms-settings:) |
| `click_text` | Click on visible text using OCR |
| `click_grid` | Click on screen grid cell (16x12 grid) |
| `type_text` | Type text into focused field |
| `hotkey` | Press keyboard shortcuts |
| `focus_window` | Focus an application window |
| `navigate_url` | Open URL in browser |
| `scroll` | Scroll up/down |
| `run_command` | Execute PowerShell commands |

## Safety Features

- **Process Allowlist** - Only interacts with approved applications
- **Confirmation Dialogs** - High-risk actions require user approval
- **Loop Detection** - Prevents infinite action loops
- **Password Protection** - Blocks typing in password fields
- **Foreground Check** - Verifies target app has focus before typing

## Development

### Prerequisites
- .NET 8 SDK
- Visual Studio 2022 or VS Code
- Windows 10 SDK (10.0.19041.0)

### Building

```powershell
# Restore and build
dotnet build

# Run tests
dotnet test

# Create installer (requires Inno Setup)
.\build-installer.ps1
```

### Project Structure

```
src/
├── AutoPilotAgent.UI/           # Main WPF application
│   ├── App.xaml                 # Application entry
│   ├── MainWindow.xaml          # Main UI
│   └── ViewModels/              # MVVM view models
├── AutoPilotAgent.Core/         # Core business logic
│   ├── Models/                  # Data models
│   ├── Services/                # Core services
│   └── Interfaces/              # Service contracts
├── AutoPilotAgent.OpenAI/       # AI integration
│   ├── OpenAIPlanService.cs     # Plan generation
│   └── OpenAIActionService.cs   # Action generation
├── AutoPilotAgent.Automation/   # Windows automation
│   ├── Services/                # Automation services
│   └── Win32/                   # Native Windows APIs
├── AutoPilotAgent.Policy/       # Safety policies
└── AutoPilotAgent.Storage/      # Persistence
```

## Troubleshooting

### Agent won't type in target app
- Ensure the target app has focus (use `focus_window` first)
- Check that the app is in the process allowlist

### OCR not finding text
- Ensure text is clearly visible on screen
- Try using `click_grid` as a fallback

### Agent keeps looping
- The loop detector will stop after 3 repeated actions
- Check if the step completion criteria are being met

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- [OpenAI](https://openai.com/) for GPT vision models
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) for MVVM infrastructure
- [Serilog](https://serilog.net/) for structured logging
