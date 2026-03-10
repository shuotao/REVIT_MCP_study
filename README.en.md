<h2 align="center"><font color="#8B0000"> Friendly Reminder: This project uses Port 8964 by default. Please ensure this port is not occupied. </font></h2>

# Revit MCP - AI-Powered Revit Control

<div align="right">

[English](README.en.md) | [繁體中文](README.md)

</div>

<p align="center">
  <img src="https://img.shields.io/badge/Revit-2022--2026-blue" alt="Revit 2022-2026">
  <img src="https://img.shields.io/badge/Node.js-LTS-green" alt="Node.js">
  <img src="https://img.shields.io/badge/.NET-4.8%20%7C%208.0-purple" alt=".NET 4.8 | 8.0">
  <img src="https://img.shields.io/badge/MCP-1.0-orange" alt="MCP Protocol">
</p>

Enable AI language models to directly control Autodesk Revit via Model Context Protocol (MCP), achieving AI-driven BIM workflows.

**🎥 Demo Video: [Revit MCP - AI-Powered BIM Workflow Demonstration](https://youtu.be/YpAYF-GxrhA)**

---

> [!IMPORTANT]
> ## 🌍 Deployers Must Read: Configure Global AI Boundaries
> 
> If you use **Google Antigravity** or **Gemini CLI**, to ensure that AI **does not bypass** the project's implementation boundaries (such as L1-L5 restrictions) when entering this project, you must set the highest priority for project-level files in your global rules.
> 
> 1. Please create or open the following file using your editor (create the directory first if it doesn't exist):
>    - Mac/Linux: `~/.gemini/GEMINI.md`
>    - Windows: `%USERPROFILE%\.gemini\GEMINI.md`
> 2. Copy and add the following minimal "Global Prerequisite" template:
> 
> ```markdown
> # Global Agent Rules
> 1. Before executing any task, ALWAYS check the current workspace root for `GEMINI.md`, `CLAUDE.md`, or `.agents/rules/`.
> 2. If present, MUST READ them first. These project-level files act as your absolute constitution. 
> 3. Their instructions and capability boundaries STRICTLY OVERRIDE any default behaviors or global rules.
> ```
> *(Following the official logic: keep global rules minimal, serving purely as a "guide for AI to find the project constitution" entry point, avoiding excessive instructions that may cause hallucinations.)*

---

> [!TIP]
> ## AI Collaboration Guide
> 
> **Before starting collaboration, please follow these guidelines:**
> 
> 1. **Human Developer**:
>    - Please read [**GEMINI.md**](./GEMINI.md) thoroughly to understand the project's AI collaboration role definitions and behavioral guidelines.
>    - Before executing tasks, ensure the AI understands the project structure in `GEMINI.md`.
> 
> 2. **AI Assistant / Agent**:
>    - **Context Loading**: Must load [**GEMINI.md**](./GEMINI.md) at startup to obtain the project map and technical guidance.
>    - **Workflow Compliance**: Before executing tasks, must check the `domain/` directory for defined workflows (e.g., coloring tasks).
>    - **Safety First**: All Revit operations must be reversible, using Transaction to ensure recoverability.
> 
> ---

> [!CAUTION]
> ## After Git Pull: Rebuild Revit Add-in Required
> 
> If you ran `git pull` to update the project, and the update includes **C# code changes** (`MCP/*.cs` files), **you must rebuild and redeploy the Revit Add-in DLL**, otherwise new features won't work!
> 
> **Quick Steps:**
> 1. **Close Revit** (otherwise DLL cannot be overwritten)
> 2. Build (choose your Revit version):
>    ```powershell
>    cd "your-project-path/MCP"
>    # Choose the configuration for your Revit version:
>    dotnet build -c Release.R22   # Revit 2022
>    dotnet build -c Release.R23   # Revit 2023
>    dotnet build -c Release.R24   # Revit 2024
>    dotnet build -c Release.R25   # Revit 2025
>    dotnet build -c Release.R26   # Revit 2026
>    ```
> 3. Copy DLL to Revit Addins folder:
>    ```powershell
>    Copy-Item "bin/Release/RevitMCP.dll" "C:\ProgramData\Autodesk\Revit\Addins\2024\RevitMCP\" -Force
>    ```
> 4. Restart Revit
> 
> | Update Type | Need to Rebuild DLL? | Need to Restart Revit? |
> |-------------|:-------------------:|:---------------------:|
> | C# Code (`MCP/*.cs`) | ✅ Yes | ✅ Yes |
> | MCP Server (`MCP-Server/*.ts`) | ❌ No | ❌ No (just restart MCP Server) |
> | Config Files (`*.json`, `*.addin`) | ❌ No | ⚠️ Depends |

## 🎯 Key Features

- **Direct AI Control of Revit** - Operate Revit through natural language commands
- **Multi-Platform AI Support** - Claude Desktop, Gemini CLI, VS Code Copilot, Google Antigravity
- **Rich Revit Tools** - Create walls, floors, doors, windows, query elements, and more
- **Real-time Bidirectional Communication** - WebSocket real-time connection

## 📁 Project Structure

```
REVIT-MCP/
├── MCP/                    # Revit Add-in (C#)
│   ├── Application.cs           # Main entry point
│   ├── ConnectCommand.cs        # Connection command
│   ├── RevitMCP.addin           # Add-in configuration
│   ├── RevitMCP.csproj          # Unified project file (Revit 2022–2026, Nice3point SDK)
│   ├── RevitMCP.2024.csproj     # Legacy project file (Revit 2024 only, deprecated)
│   ├── Core/                    # Core functionality
│   │   ├── SocketService.cs     # WebSocket service
│   │   ├── CommandExecutor.cs   # Command executor
│   │   ├── RevitCompatibility.cs # Cross-version compatibility layer (ElementId int→long)
│   │   └── ExternalEventManager.cs
│   ├── Models/                  # Data models
│   └── Configuration/           # Configuration management
├── MCP-Server/             # MCP Server (Node.js/TypeScript)
│   ├── src/
│   │   ├── index.ts                 # MCP Server main program
│   │   ├── socket.ts                # Socket client
│   │   └── tools/
│   │       └── revit-tools.ts       # Revit tool definitions
│   ├── build/                       # Build output
│   ├── package.json
│   └── tsconfig.json
└── README.md
```

## 🔧 System Requirements

| Item | Requirement |
|------|------|
| **Operating System** | Windows 10 or later |
| **Revit** | Autodesk Revit 2022 / 2023 / 2024 / 2025 / 2026 |
| **.NET** | .NET Framework 4.8 (Revit 2022–2024) / .NET 8 (Revit 2025–2026) |
| **Node.js** | LTS version (20.x or later) |

> 💡 **Important Note**: This tutorial uses Revit 2022 as an example, but applies to versions 2022, 2023, 2024, 2025, and 2026.  
> When installing, adjust the folder names according to your Revit version (see version mapping table in each step below).
> Revit 2025/2026 uses .NET 8, please ensure you have the corresponding .NET SDK installed.

## ⚠️ First-Time Setup for Git Clone Users

If you obtained this project via `git clone`, **you must complete the following steps first**, otherwise MCP Server will not work:

> [!IMPORTANT]
> The following files are **NOT included in the Git repository** (excluded by `.gitignore`):
> - `MCP-Server/build/` - MCP Server build output
> - `MCP-Server/node_modules/` - Node.js dependencies
> - `MCP/bin/` - Revit Add-in build output

### Required Steps

#### 1️⃣ Install Node.js (if not already installed)

```powershell
# Check if installed
node --version

# If not installed, go to https://nodejs.org to download LTS version
```

#### 2️⃣ Build MCP Server

```powershell
# Enter MCP-Server folder
cd "your-project-path/MCP-Server"

# Install dependencies
npm install

# Build TypeScript
npm run build
```

#### 3️⃣ Configure AI Platform Settings

Paths in configuration files need to be modified according to your environment:

- **Gemini CLI** (`MCP-Server/gemini_mcp_config.json`):
  ```json
  "args": ["your-actual-path/MCP-Server/build/index.js"]
  ```

- **Claude Desktop**: Manually configure the path in the application

- **VS Code / Antigravity** (`.vscode/mcp.json`):
  Uses `${workspaceFolder}` variable, **no modification needed**

#### 4️⃣ Build Revit Add-in

```powershell
# Enter MCP project folder
cd "your-project-path/MCP"

# Choose the configuration for your Revit version:
dotnet build -c Release.R22   # Revit 2022
dotnet build -c Release.R23   # Revit 2023
dotnet build -c Release.R24   # Revit 2024
dotnet build -c Release.R25   # Revit 2025
dotnet build -c Release.R26   # Revit 2026
```

> 💡 **Tip**: You can also run `scripts/install-addon.ps1`, which will automatically detect your Revit version, build, and copy files to the Revit Addins folder.

---

## 📦 Installation Steps

#### Step 1: Install Revit Add-in (Just Copy Files)

**In simple terms: We need to put a file into Revit's specific folder.**

⚠️ **Important: Before you start, please confirm your Revit version**  
- Open Revit
- Click "Autodesk Revit 202X" in the top left (X is your version number)
- Then click "Help" → "About Autodesk Revit"
- Check the version number and remember it (for example: 2022, 2023, 2024, 2025, or 2026)

#### Method A: Use Automated Script (Recommended)

**The easiest way: Run the automatic installation script**

1. **Go to the scripts folder**
   - Open File Explorer, navigate to the project's `scripts/` folder

2. **Run the installation script**
   - Right-click `install-addon.ps1`
   - Select "Run with PowerShell"
   
   > ⚠️ **If you encounter permission issues**:
   > Open PowerShell as Administrator and run:
   > ```powershell
   > Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
   > .\install-addon.ps1
   > ```

3. **The script will automatically**
   - Build the C# project
   - Detect your Revit version (2022, 2023, 2024)
   - Copy DLL and .addin files to the correct location

4. **Done!**
   - When you see the "Installation successful" message, it's complete
   - Restart Revit

> 💡 **Tip**: If the script fails, make sure you have .NET SDK installed and the `dotnet` command is available.

#### Method B: Build the Program Yourself (For Developers)

If you understand code or want to learn how to build:

1. **Confirm .NET SDK is installed**
   ```powershell
   # Check .NET SDK
   dotnet --version
   
   # If not installed, go to https://dotnet.microsoft.com/download
   ```

2. **Build the project**
   ```powershell
   # Enter project directory
   cd "your-project-path\MCP"
   
   # Choose the configuration for your Revit version
   dotnet build -c Release.R22   # Revit 2022
   dotnet build -c Release.R23   # Revit 2023
   dotnet build -c Release.R24   # Revit 2024
   dotnet build -c Release.R25   # Revit 2025
   dotnet build -c Release.R26   # Revit 2026
   ```
   
   After successful build, the DLL file will be generated in the `bin\Release` folder.

3. **Copy files to Revit Addins folder**
   ```powershell
   # Open Revit Addins folder (change 2022 to your version)
   explorer %APPDATA%\Autodesk\Revit\Addins\2022
   
   # Or use commands to copy
   Copy-Item "bin\Release\RevitMCP.dll" "%APPDATA%\Autodesk\Revit\Addins\2022\" -Force
   Copy-Item "RevitMCP.addin" "%APPDATA%\Autodesk\Revit\Addins\2022\" -Force
   ```
   
   > 💡 **Version Mapping**: Revit 2022 → `Addins\2022` | Revit 2023 → `Addins\2023` | Revit 2024 → `Addins\2024` | Revit 2025 → `Addins\2025` | Revit 2026 → `Addins\2026`

4. **Restart Revit**

### Step 2: Install MCP Server (The "Translator" Between AI and Revit)

**In simple terms: We need to install some software tools so AI can communicate with Revit.**

#### Pre-setup: Check if Node.js is Already Installed

MCP Server needs Node.js to run. First, check if it's installed:

1. **Open Command Prompt**
   - Press `Win + R`
   - Type `cmd`, press Enter

2. **Check Node.js**
   - In Command Prompt, type: `node --version`
   - If you see a version number (like v20.0.0), it's installed, **skip the download step**
   - If you see "command not found", it's not installed, follow the steps below to download

3. **Download and Install Node.js** (if needed)
   - Open your browser, visit https://nodejs.org
   - Click the "LTS" button on the left (recommended version)
   - Download the Windows installer (`.msi` file)
   - Run the downloaded installer and click "Next" until finished
   - Restart your computer

#### Installation Steps

1. **Open Command Prompt**
   - Press `Win + R`
   - Type `cmd`, press Enter

2. **Enter MCP Server folder**
   - Copy and paste the command below, press Enter (**Note: Change the username in the path to your account name**):
     ```
     cd C:\Users\YourUsername\Desktop\MCP\REVIT_MCP_study\MCP-Server
     ```
   - Hint: "YourUsername" is the account name you use to log into Windows
   
   > 💡 **Path different?**
   > - If you put the project folder in a different location (like C:\MCP), adjust the path accordingly
   > - To find your project folder: Right-click the MCP-Server folder → Properties → Location, copy that path

3. **Install software dependencies**
   - In Command Prompt, type:
     ```
     npm install
     ```
   - This will automatically download and install required software
   - Wait for completion (may take 1-5 minutes)
   - You should see "added XXX packages" when done

4. **Build the program (Convert to executable)**
   - Type the following command:
     ```
     npm run build
     ```
   - Wait for completion
   - You should see a `build/` folder created

**Congratulations! You've completed the installation.** Now you can proceed to the next step of configuration.

### Step 3: Configure AI Platform

Please refer to the **[Multi-Platform AI Agent Configuration](#-multi-platform-ai-agent-configuration)** section below.

---

## 🚀 How to Start

### 1️⃣ Launch Revit and Enable MCP Service

1. Open Revit 2022
2. Load or create a project
3. In the "MCP Tools" panel, click the "**MCP Service (On/Off)**" button
4. Confirm you see "WebSocket Server Started, Listening: localhost:8964"

> 💡 **About Port Numbers**:
> - `8964` is the default port for MCP Server
> - Port numbers are arbitrary and can be occupied by other programs
> - If you see "Port 8964 is in use" error, you need to manually adjust:
>   1. Open the configuration file `MCP-Server/src/index.ts`
>   2. Find the line with `PORT = 8964`
>   3. Change to another unused port, like `8766` or `9000`
>   4. Recompile: `npm run build`
>   5. Update the port number in all AI applications that use this MCP Server (to the same new port)

### 2️⃣ Connect via AI Platform

Depending on your chosen AI platform, refer to the setup instructions below.

---

## 🤖 Multi-Platform AI Agent Configuration

### Core Concept: MCP Clients and MCP Server

Before you start, you need to understand the core concepts of this architecture:

#### What is MCP Client?

**MCP Client** refers to AI applications that understand and use MCP tools. In simple terms:
- Claude Desktop
- Gemini CLI
- VS Code Copilot
- Google Antigravity

These applications have built-in "MCP Client" functionality, allowing them to read and call tools provided by the MCP Server.

#### What is MCP Server?

**MCP Server** is the Node.js application in this project (`MCP-Server/build/index.js`), which:
- Defines Revit operation tools (create_wall, query_elements, etc.)
- Communicates with Revit Add-in via WebSocket
- Converts AI commands to Revit API calls

---

### 4+1 Solution Architecture

This project provides **5 usage solutions**, divided into two categories:

#### External Invocation Solutions (4 types)

These solutions follow the same architecture:

```
┌─────────────────┐
│  AI Application │  (Claude Desktop / Gemini CLI / VS Code / Antigravity)
│  (MCP Client)   │
└────────┬────────┘
         │ 1. Read MCP Server address
         │
┌────────▼────────┐
│   MCP Server    │  (Node.js - This project)
│  (Revit Tools)  │
└────────┬────────┘
         │ 2. WebSocket connection
         │
┌────────▼────────┐
│  Revit Add-in   │  (C# - RevitMCP.dll)
│  (WebSocket)    │
└────────┬────────┘
         │ 3. Revit API calls
         │
┌────────▼────────┐
│ Revit Application│
└─────────────────┘
```

**Features:**
- AI applications have built-in MCP support, no API Key needed
- MCP Server only handles tool definitions and communication
- All API Keys are managed by the AI application itself (like Claude Desktop has its own API Key)

---

#### Embedded Solution (1 type)

```
┌────────────────────────────────┐
│   Revit Application            │
├────────────────────────────────┤
│  Revit Add-in with AI Chat     │
│                                │
│  ┌──────────────────────────┐  │
│  │  Chat Window UI (WPF)    │  │
│  └──────────────────────────┘  │
│           │ Use API Key         │
│  ┌────────▼──────────────────┐  │
│  │  GeminiChatService        │  │
│  │  (C# calls Gemini)        │  │
│  └────────┬──────────────────┘  │
│           │                     │
└───────────┼─────────────────────┘
            │ HTTP request to Gemini API
            │
        ┌───▼──────┐
        │ Gemini   │
        │ API      │
        └──────────┘
```

**Features:**
- Runs completely inside Revit, no need to launch external applications
- Directly calls Gemini API, requires API Key
- Smoothest user experience (chat directly inside Revit)

---

### Why Only the Embedded Solution Needs API Key?

This is the key difference:

| Solution | Need API Key | Reason |
|------|------|------|
| Claude Desktop | ❌ No | Claude Desktop is bound to your Anthropic account and API Key |
| Gemini CLI | ❌ No | Gemini CLI is bound to your Google account |
| VS Code Copilot | ❌ No | GitHub Copilot is bound to your GitHub account and authorization |
| Antigravity | ❌ No | Antigravity is bound to your Google Cloud account |
| **Embedded Chat (Gemini API)** | **✅ Yes** | This **directly** calls Gemini API, not through an application intermediary |

In simple terms:
- **External 4 solutions**: AI application is already a "paying customer", you just use it directly
- **Embedded solution**: You yourself become a "paying customer" of Gemini API, need to provide API Key

---

### MCP Server's Role in Each Solution

No matter which solution you use, **MCP Server's role is the same**:

```
MCP Server's Responsibilities:
1. Define Revit tools (create_wall, query_elements, etc.)
2. Receive tool invocation requests from AI applications
3. Forward requests to Revit Add-in via WebSocket
4. Return execution results to AI applications
```

MCP Server **does not directly** communicate with any AI API, it's just a "translator".

---

### Solution Selection Guide

| Scenario | Recommended Solution | Reason |
|------|------|------|
| Daily use, simplest | Claude Desktop | No additional configuration, use pre-built app directly |
| Want to chat in Revit | Embedded Chat (Gemini API) | Smoothest user experience |
| Prefer Google | Gemini CLI | Use your own Google account |
| Software developers | VS Code Copilot | Seamless use in development environment |
| Advanced AI development | Antigravity | Multi-window and async agent execution |

---

### Solution 1️⃣: Gemini CLI

Gemini CLI is Google's command-line AI tool that lets you chat directly with Gemini 2.5 Flash in the terminal.

#### Step 1: Install Gemini CLI (Beginner-Friendly)

**What is Gemini CLI?** It's a tool that can run in Windows Command Prompt or PowerShell.

1. **Download Node.js** (if not already installed)
   - Go to https://nodejs.org
   - Click the "LTS" version download
   - Run the downloaded installer and click "Next" all the way to completion
   - Restart your computer

2. **Open PowerShell**
   - Press `Win + X`
   - Select "Windows PowerShell (Administrator)"
   - Copy and paste the command below, press Enter:
   ```powershell
   npm install -g @google/gemini-cli
   ```
   - Wait for installation to complete (you'll see a green checkmark)
   
   > ⚠️ **If you encounter "script execution is disabled" error**:
   > Run this command first to allow script execution, then retry installation:
   > ```powershell
   > Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
   > ```

#### Step 2: Configure MCP Server Connection

> [!IMPORTANT]
> **Gemini CLI uses `settings.json` for MCP configuration, NOT `--config` parameter!**
> 
> This is different from Claude Desktop and other tools. Gemini CLI reads the `~/.gemini/settings.json` file in the user's home directory.

**Configuration Method: Edit the `settings.json` file**

1. **Open the settings file location**

   **Windows**:
   - Press `Win + R`, enter the following path, press Enter:
     ```
     %USERPROFILE%\.gemini
     ```
   - If the folder doesn't exist, manually create the `.gemini` folder
   
   **macOS / Linux**:
   ```bash
   cd ~/.gemini
   # If it doesn't exist, create it first
   mkdir -p ~/.gemini
   ```
   
   - Find `settings.json` and open it with a text editor (create one if it doesn't exist)

2. **Add MCP Server configuration**
   
   Modify the file content as follows (if the file already has content, keep it and add the `mcpServers` section):
   ```json
   {
     "mcpServers": {
       "revit-mcp": {
         "command": "node",
         "args": [
           "C:\\your-path\\REVIT MCP\\MCP-Server\\build\\index.js"
         ],
         "env": {
           "REVIT_VERSION": "2022"
         }
       }
     }
   }
   ```
   
   > 💡 **Please change the path to your actual project location!**
   > 
   > Example: `C:\\Users\\YourName\\Desktop\\REVIT MCP\\MCP-Server\\build\\index.js`

3. **Save the file and restart Gemini CLI**

#### Step 3: Launch and Test

1. **First launch Revit**
   - Open Revit 2022
   - In the "MCP Tools" panel, click "**MCP Service (On/Off)**" button
   - You'll see "WebSocket server started" when successful

2. **Open Gemini CLI**
   - Open PowerShell
   - Run:
   ```powershell
   gemini
   ```

3. **Confirm MCP is connected**
   ```
   /mcp list
   ```
   - You should see the `revit-mcp` server

4. **Test conversation**
   ```
   > List all floors in the Revit project
   > Create a 5-meter-long wall for me
   ```
   ```

---

### Solution 2️⃣: VS Code (GitHub Copilot)

Chat directly with AI and control Revit in the VS Code code editor.

#### Step 1: Install VS Code

1. Go to https://code.visualstudio.com
2. Click the blue "Download" button
3. Run the downloaded installer
4. Click "Next" all the way to completion, restart your computer

#### Step 2.5: Use Pre-Configured Version from This Project (Recommended!)

**Good news: We've prepared the configuration file for you!**

1. **Open this project's folder**
   - Right-click the `c:\Users\User\Desktop\REVIT MCP` folder
   - Select "**Open with VS Code**"
   - Or in VS Code, click File → Open Folder, select this folder

2. **Configuration file is in `.vscode/mcp.json`**
   - The file is ready, you don't need to modify anything
   - The system will load this configuration automatically

#### Step 3: Launch (Beginner Version)

1. **Confirm Revit MCP service is running**
   - Open Revit 2022
   - Click "MCP Service (On/Off)"

2. **Open Copilot Chat in VS Code**
   - Press `Ctrl + Shift + I`
   - Or click the Copilot icon on the left
   
3. **Start asking**
   - In the chat box, type: "Find all columns in the Revit project"
   - AI will automatically execute Revit tools for you

---

### Solution 3️⃣: Claude Desktop (Recommended for Beginners!)

Anthropic's official desktop application, this is the **simplest way**.

#### Step 1: Install Claude Desktop

1. Go to https://claude.ai/download
2. Click "Download for Windows"
3. Run the downloaded `.exe` installer
4. After installation, restart your computer

#### Step 2.5: Add MCP Directly in Claude Desktop (Simplest!)

**No file copying needed! Configure directly in the application:**

1. **Open Claude Desktop application**

2. **Click the "⚙️ Settings" icon in the top right**
   - Or find "Settings" in the bottom left

3. **Find the "MCP Servers" option**

4. **Click "Add Server" or "New Server"**

5. **Fill in the following information**
   - **Name**: `revit-mcp`
   - **Command**: `node`
   - **Arguments**: `C:\Users\User\Desktop\REVIT MCP\MCP-Server\build\index.js`
   - **Environment Variables**:
     ```
     REVIT_VERSION: 2022
     ```
     
   > 💡 **Different version? Modify the environment variable**:
   > - Revit 2022: Change to `REVIT_VERSION: 2022`
   > - Revit 2023: Change to `REVIT_VERSION: 2023`
   > - Revit 2024: Change to `REVIT_VERSION: 2024`

6. **Click "Save"** - Done!

#### Step 3: Launch (Beginner Version)

1. **Launch Revit**
   - Open Revit 2022
   - Click "MCP Service (On/Off)"

2. **Use Claude Desktop**
   - Claude application will connect to Revit automatically
   - Type a conversation in the chat box, example:
   ```
   Create a 3m × 5m floor in Revit for me
   ```

3. **Claude will automatically execute the operation for you!**

---

### Solution 4️⃣: Google Antigravity

[Google Antigravity](https://antigravity.google/) is Google's "agent-first" development platform that brings IDE into the AI Agent era.

**Main Features:**
- Based on open-source VS Code, but radically changes the user experience
- Interface split into two main windows: **Editor** and **Agent Manager**
- Can **dispatch multiple agents** to handle different tasks simultaneously (non-linear, asynchronous execution)
- Built-in **Antigravity Browser** (browser sub-agent) for web testing and screen recording
- Agents generate "Artifacts" such as task plans, implementation plans, code diffs, screenshots, etc.
- Currently only available as a preview version for **personal Gmail accounts** (free to use)

#### Step 1: Install Google Antigravity

1. **Go to download page**
   - Open your browser, go to https://antigravity.google/download
   - Click the version for your operating system (Windows / Mac / Linux)
   - Run the installer and complete installation

2. **Launch Antigravity and complete setup**
   - Open the Antigravity application
   - Choose setup flow (can import from existing VS Code or Cursor settings, or start fresh)
   - Choose editor theme (dark/light)
   - Choose agent usage mode:
     - **Agent-Directed Development**: Agent operates autonomously, minimal human intervention
     - **Agent-Assisted Development** (recommended): Agent makes decisions and returns them for user approval
     - **Review-Driven Development**: Agent always requests review
     - **Custom Settings**: Fully customizable control

3. **Sign in with Google account**
   - Click "Sign in to Google"
   - Sign in with your personal Gmail account
   - System will create a new Chrome profile for this

#### Step 2: Configure Browser Agent (Antigravity Browser)

A major feature of Antigravity is the built-in browser sub-agent that lets AI directly operate web pages.

1. **Start conversation in Agent Manager**
   - Select `Playground` or any workspace
   - Type a command that needs browser (example: "Go to antigravity.google")

2. **Install Chrome Extension**
   - Agent will prompt to set up browser agent
   - Click `Setup`, follow instructions to install Chrome extension
   - After installation, agent can control browser to execute tasks

#### Step 3: Configure MCP Server to Connect to Revit

> ⚠️ **Note**: Antigravity runs locally, MCP Server also needs to run on the same Windows computer (because it needs to connect to Revit).

1. **Open workspace**
   - In Agent Manager, click `Workspaces`
   - Select this project's `MCP-Server` folder as the workspace

2. **Launch MCP connection via conversation**
   - In Agent Manager, start a new conversation
   - Tell Agent: "Please run node build/index.js to start MCP Server"
   - Or directly in the editor's terminal, run:
     ```
     cd C:\Users\YourUsername\Desktop\REVIT MCP\MCP-Server
     node build/index.js
     ```

3. **Start interacting with Revit**
   - Confirm Revit is launched and MCP service is on
   - In Agent Manager, type a command, example:
     ```
     Create a 5-meter-long wall in Revit for me
     ```

#### 🎯 Antigravity's Unique Advantages

| Feature | Description |
|------|------|
| **Multi-Agent Parallel Execution** | Can dispatch 5+ agents to handle different tasks simultaneously |
| **Artifacts** | Agents generate task plans, implementation plans, code diffs, screenshots, browser recordings, etc. |
| **Browser Integration** | Built-in Chrome browser sub-agent with click, scroll, input, read console capabilities |
| **Inbox** | Centrally track all conversations and task status |
| **Google Docs-style Comments** | Add comments to artifacts and code diffs, agents iterate based on feedback |

> 📚 **More Information**: See [Google Antigravity Official Tutorial](https://codelabs.developers.google.com/getting-started-google-antigravity?hl=en)

---

## 🛠️ Available MCP Tools

### Basic Modeling Tools

| Tool Name | Description |
|---------|------|
| `create_wall` | Create wall |
| `create_floor` | Create floor |
| `create_door` | Create door |
| `create_window` | Create window |
| `create_column` | Create column |
| `create_dimension` | Create dimension annotation |

### Information Query Tools

| Tool Name | Description |
|---------|------|
| `get_project_info` | Get project information |
| `query_elements` | Query elements (supports filter conditions) |
| `get_element_info` | Get element details |
| `get_all_levels` | Get all levels |
| `get_all_grids` | Get all grid lines (with coordinates, can calculate intersections) |
| `get_column_types` | Get column type list (with dimension info) |
| `get_furniture_types` | Get furniture type list |
| `get_room_info` | Get room details (center point, boundary range) |
| `get_rooms_by_level` | Get all rooms on a level (with area statistics) |
| `get_wall_info` | Get wall details |
| `query_walls_by_location` | Query walls by location |

### View & Navigation Tools

| Tool Name | Description |
|---------|------|
| `get_all_views` | Get all views list |
| `get_active_view` | Get current active view |
| `set_active_view` | Switch active view |
| `select_element` | Select element |
| `zoom_to_element` | Zoom to specified element |
| `measure_distance` | Measure distance between two points |

### Element Operation Tools

| Tool Name | Description |
|---------|------|
| `modify_element_parameter` | Modify element parameter |
| `delete_element` | Delete element |
| `place_furniture` | Place furniture |

### Visualization Tools

| Tool Name | Description |
|---------|------|
| `override_element_graphics` | Override element graphics (color, line style, etc.) |
| `clear_element_override` | Clear element graphics override |
| `unjoin_wall_joins` | Unjoin wall joins (before coloring) |
| `rejoin_wall_joins` | Rejoin wall joins (restore after coloring) |

---

## 🚀 Advanced Feature: Integrate AI API in Revit Add-in (Gemini 2.5 Flash)

### Feature Description

Allow Revit users to directly open a **chat window** in the Add-in to have interactive conversations with Gemini 2.5 Flash AI and control Revit. No need to launch external tools.

```
┌─────────────────────────────────────┐
│        Revit Window                 │
├─────────────────────────────────────┤
│  MCP Tools                          │
│  ┌──────────────────────────────┐  │
│  │ MCP Service (On/Off)         │  │
│  ├──────────────────────────────┤  │
│  │ MCP Settings                 │  │
│  ├──────────────────────────────┤  │
│  │ 🆕 AI Chat Assistant (New!)  │  │
│  └──────────────────────────────┘  │
│                                     │
│  ┌─────────────────────────────────┐│
│  │ AI Chat Window (WPF Dialog)     ││
│  ├─────────────────────────────────┤│
│  │ You: Create a 3mx5m floor       ││
│  │ AI: Floor created, ID: 123456   ││
│  │                                 ││
│  │ [Input Box] [Send Button]       ││
│  └─────────────────────────────────┘│
└─────────────────────────────────────┘
```

### Development Steps

#### Step 1: Get Gemini API Key

1. **Go to Google AI Studio**
   - Open browser, visit https://aistudio.google.com/apikey

2. **Sign in with your Google account**
   - If you don't have one, create one

3. **Click "Create API Key"**
   - Select "Create new secret key in new project"
   - Google will automatically create a free API Key

4. **Copy the API Key**
   - You'll see a long string, for example:
   ```
   AIzaSyDx...xyz123abc
   ```
   - **Keep this Key safe, do not share it with others!**

#### Step 2: Create AI Chat Service in C#

Create a new file `GeminiChatService.cs` in the `MCP/Core/` folder:

```csharp
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace RevitMCP.Core
{
    /// <summary>
    /// Gemini 2.5 Flash API Integration Service
    /// </summary>
    public class GeminiChatService
    {
        private readonly string _apiKey;
        private readonly string _apiUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent";
        private readonly HttpClient _httpClient;

        public GeminiChatService(string apiKey)
        {
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _httpClient = new HttpClient();
        }

        /// <summary>
        /// Interactive conversation with Gemini AI
        /// </summary>
        public async Task<string> ChatAsync(string userMessage, string context = "")
        {
            try
            {
                // Build request
                var requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new[]
                            {
                                new
                                {
                                    text = $"{context}\n\nUser question: {userMessage}"
                                }
                            }
                        }
                    },
                    generationConfig = new
                    {
                        temperature = 0.7,
                        maxOutputTokens = 1024
                    }
                };

                var jsonContent = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // Send request to Gemini API
                var response = await _httpClient.PostAsync(
                    $"{_apiUrl}?key={_apiKey}",
                    content
                );

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Gemini API Error: {response.StatusCode}");
                }

                // Parse response
                var responseContent = await response.Content.ReadAsStringAsync();
                dynamic result = JsonConvert.DeserializeObject(responseContent);
                
                string aiResponse = result.candidates[0].content.parts[0].text;
                return aiResponse;
            }
            catch (Exception ex)
            {
                return $"AI Service Error: {ex.Message}";
            }
        }
    }
}
```

#### Step 3: Create WPF Chat Window

Create `ChatCommand.cs` in `MCP/Commands/`:

```csharp
using System;
using Autodesk.Revit.UI;
using RevitMCP.Core;

namespace RevitMCP.Commands
{
    public class ChatCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // Read API Key from settings
                var apiKey = System.Environment.GetEnvironmentVariable("GEMINI_API_KEY");
                
                if (string.IsNullOrEmpty(apiKey))
                {
                    TaskDialog.Show("Configuration Error", 
                        "Please set the environment variable GEMINI_API_KEY\n\n" +
                        "In Windows:\n" +
                        "1. Press Win + Pause\n" +
                        "2. Advanced System Settings\n" +
                        "3. Environment Variables\n" +
                        "4. New: GEMINI_API_KEY = Your API Key");
                    return Result.Failed;
                }

                // Create chat service
                var chatService = new GeminiChatService(apiKey);

                // Open chat window
                var chatWindow = new ChatWindow(chatService, commandData.Application);
                chatWindow.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", $"Failed to open AI Chat: {ex.Message}");
                return Result.Failed;
            }
        }
    }
}
```

#### Step 4: Create WPF Window UI

Create `ChatWindow.xaml` in `MCP/`:

```xml
<Window x:Class="RevitMCP.ChatWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Revit AI Chat Assistant"
        Height="600"
        Width="500"
        Background="#F5F5F5">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Chat History -->
        <ListBox x:Name="ChatHistory"
                 Grid.Row="0"
                 Margin="10"
                 Background="White"
                 BorderThickness="1"
                 BorderBrush="#DDD">
            <ListBox.ItemTemplate>
                <DataTemplate>
                    <Border Margin="5" Padding="10" CornerRadius="5">
                        <TextBlock Text="{Binding}"
                                   TextWrapping="Wrap"
                                   Foreground="#333"/>
                    </Border>
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>

        <!-- Input Area -->
        <Grid Grid.Row="1" Margin="10" Background="White" Height="80">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>

            <TextBox x:Name="InputBox"
                     Grid.Column="0"
                     VerticalAlignment="Top"
                     Padding="10"
                     TextWrapping="Wrap"
                     AcceptsReturn="True"
                     PlaceholderText="Type your question..."/>

            <Button x:Name="SendButton"
                    Grid.Column="1"
                    Margin="5"
                    Padding="15,10"
                    Background="#007ACC"
                    Foreground="White"
                    Content="Send"
                    Click="SendButton_Click"/>
        </Grid>
    </Grid>
</Window>
```

#### Step 5: Code-Behind (ChatWindow.xaml.cs)

```csharp
using System.Collections.ObjectModel;
using System.Windows;
using Autodesk.Revit.UI;
using RevitMCP.Core;

namespace RevitMCP
{
    public partial class ChatWindow : Window
    {
        private readonly GeminiChatService _chatService;
        private readonly UIApplication _uiApp;
        private readonly ObservableCollection<string> _messages;

        public ChatWindow(GeminiChatService chatService, UIApplication uiApp)
        {
            InitializeComponent();
            _chatService = chatService;
            _uiApp = uiApp;
            _messages = new ObservableCollection<string>();
            ChatHistory.ItemsSource = _messages;

            _messages.Add(" AI Assistant is ready. Type your question to control Revit.");
            _messages.Add(" Example: Create a 5-meter-long wall");
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            string userInput = InputBox.Text.Trim();
            if (string.IsNullOrEmpty(userInput)) return;

            // Display user message
            _messages.Add($"You: {userInput}");
            InputBox.Clear();

            // Get AI response
            SendButton.IsEnabled = false;
            SendButton.Content = "Processing...";

            try
            {
                string context = $"You are a Revit BIM expert assistant. Available Revit commands include: " +
                    "create_wall, create_floor, query_elements, get_project_info, etc. " +
                    "Please respond concisely and explain your operations.";

                string response = await _chatService.ChatAsync(userInput, context);
                _messages.Add($" AI: {response}");
            }
            finally
            {
                SendButton.IsEnabled = true;
                SendButton.Content = "Send";
            }
        }
    }
}
```

#### Step 6: Register New Button in Add-in

Modify the `OnStartup` method in `Application.cs` to add the AI Chat button:

```csharp
public Result OnStartup(UIControlledApplication application)
{
    try
    {
        RibbonPanel panel = application.CreateRibbonPanel("MCP Tools");
        
        string assemblyPath = Assembly.GetExecutingAssembly().Location;

        // Existing buttons...
        
        // 🆕 Add AI Chat button
        PushButtonData chatButtonData = new PushButtonData(
            "MCPChat",
            "AI Chat\nAssistant",
            assemblyPath,
            "RevitMCP.Commands.ChatCommand");
        chatButtonData.ToolTip = "Open AI Chat Assistant for interactive Revit control with Gemini 2.5 Flash";
        PushButton chatButton = panel.AddItem(chatButtonData) as PushButton;

        return Result.Succeeded;
    }
    catch (Exception ex)
    {
        TaskDialog.Show("Error", "Failed to load MCP Tools: " + ex.Message);
        return Result.Failed;
    }
}
```

#### Step 7: Set Environment Variable (For End Users)

1. **Press `Win + Pause` or `Win + X` → System**

2. **Click "Advanced system settings"**

3. **Click "Environment Variables" button**

4. **Under "System variables", click "New"**

5. **Fill in the following**
   - Variable name: `GEMINI_API_KEY`
   - Variable value: `The API Key you copied from Step 1`

6. **Click "OK" and restart Revit**

### Step 8: Build and Test

1. **Build the C# project**
   ```powershell
   cd MCP
   dotnet build -c Release
   ```

2. **Copy DLL to Revit Add-in directory**
   ```powershell
   $target = "$env:APPDATA\Autodesk\Revit\Addins\2022"
   Copy-Item "bin\Release\RevitMCP.dll" $target
   ```

3. **Restart Revit**

4. **Click the "AI Chat Assistant" button**
   - You should see the chat window
   - Start chatting with AI!

### Usage Example

```
👤 User: I want to create 3 square floors on Level 2, all 5m × 5m

 AI: I can help you create 3 square floors. I'll create them at these locations:
- Floor 1: (0, 0) to (5, 5)
- Floor 2: (6, 0) to (11, 5)  
- Floor 3: (12, 0) to (17, 5)

Creating now... Done! Created 3 floors with IDs 123456, 123457, 123458

👤 User: Please change Floor 1's height to 4m

 AI: I've changed Floor 1's height to 4m. Modification complete!
```

---

## 🔒 Security Considerations

⚠️ **Important Security Reminders**:

1. **Port Management** - MCP Server listens on `localhost:8964` by default, access limited to this machine only
2. **Firewall** - Not recommended to open ports to external networks
3. **Code Review** - Confirm code source is trustworthy before running
4. **Backup** - Backup your Revit project before operating
5. **API Key Protection** - Never submit API Keys to GitHub, use environment variables to manage

## 📝 FAQ

### Q: Revit doesn't show MCP Tools panel?
A: Confirm `RevitMCP.addin` is correctly placed in the Add-in directory and restart Revit.

### Q: MCP Server can't connect to Revit?
A: 
1. Confirm you clicked "MCP Service (On/Off)" in Revit to start the service
2. Confirm Port 8964 is not occupied by other programs
3. Check firewall settings

### Q: AI says it can't find Revit tools?
A: Confirm MCP Server configuration file path is correct and restart the AI application.

---

## 📖 Appendix: Technical Supplementary Notes

> 💡 The content below is advanced technical information. General users can skip this section.

### A. What is WebSocket?

This project uses **WebSocket** as the communication protocol between MCP Server and Revit Add-in.

**WebSocket** is a networking communication standard (not created by this project) with the following characteristics:

| Feature | Description |
|------|------|
| **Bidirectional Communication** | Server and client can send messages to each other anytime |
| **Low Latency** | Connection stays open, no need to reconnect each time |
| **Real-time** | Suitable for operations needing quick response (like real-time Revit control) |

**Simple Analogy:**
- Traditional HTTP = Call phone each time, hang up after talking
- WebSocket = Keep call connected, both can talk anytime

### B. Why Choose WebSocket?

Reasons for this project choosing WebSocket:

1. **Real-time Requirement** - Revit operations need immediate response
2. **Persistent Connection** - Multiple AI commands are sent continuously, single connection is more efficient
3. **Bidirectional Communication** - Revit sometimes needs to actively notify (like progress updates, error messages)
4. **Cross-Language Support** - Both Node.js and C# have native support
5. **MCP Standard** - Model Context Protocol officially uses WebSocket

### C. Comparison of Other Communication Technologies

If you're interested in other technical options:

| Technology | Latency | Bidirectional | Ease of Use | Use Case |
|------|------|------|--------|----------|
| **WebSocket** ✅ | Low | ✅ | ⭐⭐⭐⭐ | This project uses it |
| HTTP REST | High | ❌ | ⭐⭐⭐⭐⭐ | Simple queries |
| gRPC | Lowest | ✅ | ⭐⭐ | High-performance scenarios |
| Named Pipes | Lowest | ✅ | ⭐⭐ | Local-only communication |
| SignalR | Low | ✅ | ⭐⭐⭐⭐ | .NET ecosystem |

### D. Port Number (Port) Supplementary Explanation

This project uses `8964` as the default port, which is an arbitrary choice.

**Common port ranges:**
- `0-1023`: System reserved ports (like 80=HTTP, 443=HTTPS)
- `1024-49151`: Registered ports (used by common applications)
- `49152-65535`: Dynamic/private ports (can be freely used)

`8964` is in the registered port range, usually won't conflict with system services, but can be occupied by other applications.

---

## 📄 License

MIT License

## 🤝 Contributing

Welcome to submit Issues and Pull Requests!

---

## 📚 Document Navigation

| Document | Description |
|:-----|:----|
| [GEMINI.md](./GEMINI.md) | AI Assistant System Prompt: slash commands, workflow triggers, deployment guide |
| [CLAUDE.md](./CLAUDE.md) | Claude Code project guide: architecture, build commands, code conventions |
| [CHANGELOG.md](./CHANGELOG.md) | Version changelog (v1.0.0 ~ v1.5.0) |
| [README.en.md](./README.en.md) | English version of this README |
| **教材/** | |
| [教材/README.md](./教材/README.md) | Course catalog (8 lessons × 3 hours) |
| [教材/05-Skill遷移實戰篇.md](./教材/05-Skill遷移實戰篇.md) | Lesson 5: Migrating from domain/ to Agent Skill architecture |
| **docs/** | |
| [docs/DOCS_STRUCTURE.md](./docs/DOCS_STRUCTURE.md) | Document directory structure guide |
| [docs/tools/](./docs/tools/) | MCP Tools API technical documentation |
| [docs/workflows/](./docs/workflows/) | Workflow design documentation |
| **domain/** | |
| [domain/README.md](./domain/README.md) | Domain knowledge catalog (AI workflow SOPs) |

---

**Enjoy your AI-powered Revit development!**
