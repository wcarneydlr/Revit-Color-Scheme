# DLR Group — Color Scheme Manager
### Revit 2024 Addin · WPF · C# · .NET 4.8

---

## Repo location

```
D:\Git\ColorSchemeAddin\
```

> The repo lives on **D:** (your dedicated Git drive). Revit and its API DLLs
> remain on **C:** as normal — the `.csproj` HintPaths reference `C:\Program Files\Autodesk\Revit 2024\`
> and do not need to change just because the source is on a different drive.

---

## Prerequisites

| Tool | Version | Notes |
|------|---------|-------|
| Visual Studio | 2022 (any edition) | Community is free |
| .NET Framework | 4.8 | Pre-installed with Windows 10/11 |
| Revit | 2024 | Must be installed at default path on C: |
| Git | Any | git-scm.com |
| Inter font | Any | fonts.google.com — free |
| Franklin Gothic ATF | Licensed | Falls back to Franklin Gothic Medium if absent |

---

## First-time setup

### 1 — Clone the repo to D:

```powershell
# Make sure D:\Git\ exists first
mkdir D:\Git

# Clone
git clone https://github.com/DLRGroup/color-scheme-addin.git D:\Git\ColorSchemeAddin
```

Or if starting fresh from the downloaded zip:

```powershell
mkdir D:\Git\ColorSchemeAddin
cd D:\Git\ColorSchemeAddin
git init
# copy project files here, then:
git add .
git commit -m "feat: initial Color Scheme Manager scaffold"
git remote add origin https://github.com/DLRGroup/color-scheme-addin.git
git push -u origin main
```

### 2 — Open in Visual Studio

```
File → Open → Project/Solution → D:\Git\ColorSchemeAddin\ColorSchemeAddin.csproj
```

Visual Studio will restore NuGet packages (ClosedXML + CommunityToolkit.Mvvm) automatically.

### 3 — Verify Revit API paths

The `.csproj` references Revit from its default install location on **C:**. Confirm these exist:

```
C:\Program Files\Autodesk\Revit 2024\RevitAPI.dll
C:\Program Files\Autodesk\Revit 2024\RevitAPIUI.dll
```

If your Revit install is non-standard, update the two `<HintPath>` entries in `ColorSchemeAddin.csproj`.

### 4 — Build

```powershell
cd D:\Git\ColorSchemeAddin
dotnet build -c Release
```

Output lands in `D:\Git\ColorSchemeAddin\bin\Release\net48\`.

### 5 — Deploy to Revit

Copy all files from `bin\Release\net48\` to the Revit Addins folder (always on **C:**):

```powershell
xcopy /Y "D:\Git\ColorSchemeAddin\bin\Release\net48\*.*" `
         "%APPDATA%\Autodesk\Revit\Addins\2024\"
```

Which expands to:
```
C:\Users\YourName\AppData\Roaming\Autodesk\Revit\Addins\2024\
```

**Auto-deploy on every Debug build** — uncomment the `DeployToRevit` target at the bottom of
`ColorSchemeAddin.csproj` and it happens automatically every time you hit Build in VS.

### 6 — Set up VS debugger

```
Project → Properties → Debug
  Start external program: C:\Program Files\Autodesk\Revit 2024\Revit.exe
```

Hit **F5** and Revit launches with the debugger attached to your D: source.

---

## Daily workflow

```powershell
# Start a new feature
git checkout -b feat/my-feature

# ... make changes in D:\Git\ColorSchemeAddin\ ...

# Build and auto-deploy to Revit (if DeployToRevit target is enabled)
dotnet build -c Debug

# Test in Revit, then commit
git add .
git commit -m "feat: describe what you did"
git push origin feat/my-feature

# Open a PR on GitHub → merge to main
```

---

## Excel template format

```
Sheet name = color scheme name   (one sheet per scheme)
Row 1      = headers: Name | R | G | B | Preview
Row 2+     = data rows
Column E   = filled by the addin on export; leave blank on import
```

---

## Feature reference

### Tab 1 — Create Scheme
- Import `.xlsx` — all sheets become separate schemes
- Download blank template to fill in
- Copy an existing Revit ColorFillScheme as a starting point
- Choose target category (Rooms / Areas) and fill parameter

### Tab 2 — Manage
- Grid of all ColorFillSchemes with color swatch previews
- Click scheme name to open the editor
- Edit R/G/B values or use the Windows color picker (🎨)
- Export selected or all schemes back to Excel

### Tab 3 — Apply
| Method | What it does |
|--------|-------------|
| Room/Area Color Fill | Applies ColorFillScheme to floor plan / area plan views |
| Create Materials | `[Scheme] - [Entry]` solid-color materials; duplicates from **"Color Scheme"** material if present |
| View Filters | `ParameterFilterElement` objects with solid color graphic overrides |
| Apply to active view | Temporary filter overrides on the current view |
| Generate View Templates | Batch across Floor Plan / Area Plan / 3D / Section with Color Fill, Filters, or Both |

---

## Project structure

```
D:\Git\ColorSchemeAddin\
├── .gitignore
├── README.md
├── ColorSchemeAddin.addin          Revit manifest
├── ColorSchemeAddin.csproj         .NET 4.8, WPF + WinForms, ClosedXML
├── App.cs                          IExternalApplication — ribbon setup
├── App.xaml / App.xaml.cs          Global WPF resources
├── Commands/
│   └── ColorSchemeCommand.cs       IExternalCommand entry point
├── Models/
│   └── ColorSchemeModel.cs         Data model + ColorEntryModel
├── Services/
│   ├── ExcelService.cs             Import / export via ClosedXML
│   ├── ColorFillSchemeService.cs   Revit ColorFillScheme CRUD
│   ├── MaterialService.cs          Solid-color material creation
│   └── ViewTemplateService.cs      View templates + parameter filters
├── ViewModels/
│   ├── MainDashboardViewModel.cs
│   ├── CreateSchemeViewModel.cs
│   ├── ManageSchemeViewModel.cs
│   └── ApplySchemeViewModel.cs
├── Views/
│   ├── MainDashboardWindow.xaml    1100×740 main window
│   ├── CreateSchemeView.xaml
│   ├── ManageSchemeView.xaml       DataGrid with color swatches
│   ├── ApplySchemeView.xaml        Two-column layout
│   ├── SchemeEditorDialog.xaml     Modal color editor
│   └── ValueConverters.cs          WPF value converters
└── Resources/
    └── DLRStyles.xaml              DLR Group design system
```

---

## Key principle: D: vs C: separation

| What | Drive | Path |
|------|-------|------|
| Source code (this repo) | **D:** | `D:\Git\ColorSchemeAddin\` |
| Revit install + API DLLs | **C:** | `C:\Program Files\Autodesk\Revit 2024\` |
| Revit Addins folder | **C:** | `%APPDATA%\Autodesk\Revit\Addins\2024\` |
| NuGet package cache | **C:** | `%USERPROFILE%\.nuget\packages\` |

The D: drive is purely for source — nothing Revit-runtime lives there.

---

## Troubleshooting

| Problem | Fix |
|---------|-----|
| DLR Group tab missing in Revit | Confirm `.addin` + all DLLs are in `%APPDATA%\Autodesk\Revit\Addins\2024\` |
| "Could not load ClosedXML" | Deploy all files from `bin\Release\net48\`, not just the addin DLL |
| VS can't find RevitAPI.dll | Confirm Revit 2024 is installed at `C:\Program Files\Autodesk\Revit 2024\` |
| Color picker opens behind Revit | `Alt+Tab` — Windows system dialog limitation |
| "No existing Floor Plan view found" | Template generator needs at least one non-template view of each type |
| Colors invisible in view | View tab → Color Fill → enable for the view |

---

*DLR Group Design Technology · Color Scheme Manager v1.0.0*
