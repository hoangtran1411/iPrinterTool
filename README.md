# iPrinterTool

A lightweight WPF desktop utility built with .NET 10 to monitor, manage, and audit Canon network multi-function printers.

---

## Overview

**iPrinterTool** simplifies managing network-connected Canon printers by providing quick paper tray toggles, automated job log synchronization, and real-time SNMP status monitoring. It stores historical print logs locally using SQLite to deliver print usage statistics and track paper depletion events.

---

## Features

### 1. Paper Tray Configuration

- **Quick Tray Switching**: Rapidly check and toggle paper tray sizes between **A4** and **Free Size** for:
  - Multi-Purpose Tray
  - Drawer 1
- **Remote UI Integration**: Interacts directly with the printer's embedded web management interface (Remote UI).

### 2. Print Job History & Analytics

- **Job Synchronization**: Fetches detailed print job records (job number, file name, user, page count, copies, completion status).
- **Usage Statistics**: Computes and highlights top users by total printed pages over a configurable number of days.
- **Local Persistence**: Stores job history in a local SQLite database (`%LOCALAPPDATA%\iPrinterTool\printlog.db`) using Entity Framework Core.
- **Automatic Polling**: Periodically syncs new print jobs in the background.

### 3. SNMP Status & Out-of-Paper Tracking

- **Hardware State Monitoring**: Periodically checks printer health via SNMP (`hrPrinterDetectedErrorState`, RFC 2790), reporting states such as:
  - Out of paper / Low paper
  - Toner levels
  - Paper jams
  - Open doors / covers
  - Offline status
- **Paper-Out Incident Auditing**: When paper runs out, iPrinterTool captures snapshot events recording the last user who printed and the heaviest user in the preceding 3 hours.

---

## Tech Stack & Architecture

- **UI Framework**: Windows Presentation Foundation (WPF) on **.NET 10** (`net10.0-windows`)
- **Architecture**: MVVM using [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/)
- **Data Persistence**: SQLite via [Microsoft.EntityFrameworkCore.Sqlite](https://learn.microsoft.com/ef/core/)
- **HTML Parsing**: [AngleSharp](https://anglesharp.github.io/) for parsing Canon Remote UI responses
- **SNMP Client**: [SharpSnmpLib](https://github.com/lextm/sharpsnmplib) for RFC 2790 printer state detection
- **Deployment**: Published as a self-contained, single-file 32-bit executable (`win-x86`)

---

## Getting Started

### Prerequisites

- Windows 10 or later
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (or higher) to build from source
- Network access to your Canon printer's IP address (HTTP port 80 and SNMP port 161)

### Building from Source

1. **Clone the repository:**

   ```bash
   git clone https://github.com/hoangtran1411/iPrinterTool.git
   cd iPrinterTool
   ```

2. **Restore dependencies:**

   ```bash
   dotnet restore
   ```

3. **Build the project:**

   ```bash
   dotnet build -c Release
   ```

4. **Publish as a Single-File Executable:**

   ```bash
   dotnet publish -c Release
   ```

   The generated executable will be located in:

   ```
   bin\Release\net10.0-windows\win-x86\publish\iPrinterTool.exe
   ```

---

## Configuration

The printer IP can be entered directly in the application's top navigation bar:
- Enter any valid printer IP address (or hostname) in the **Printer IP** input box and click **Connect** (or press `Enter`).
- The app automatically persists your last configured IP to `%LOCALAPPDATA%\iPrinterTool\printer_ip.txt` so it is restored the next time you open the app.
- If no IP is saved, it defaults to `10.90.39.20`.

---

## Database Storage

All synced print jobs and paper events are stored locally at:

```
%LOCALAPPDATA%\iPrinterTool\printlog.db
```

EF Core migrations run automatically on startup to initialize or update the database schema.
