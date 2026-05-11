- @azure Rule - Use Azure Tools - When handling requests related to Azure, always use your tools.
- @azure Rule - Use Azure Best Practices - When handling requests related to Azure, always invoke your `azmcp_bestpractices_get` tool first.
- @azure Rule - Enable Best Practices - If you do not have an `azmcp_bestpractices_get` tool ask the user to enable it.

---

# ToshanVault — Project Instructions

## Overview

ToshanVault is a **WinUI 3 desktop app** (.NET 10, x64, single-file publish) for personal vault management. It stores credentials, bank accounts, insurance policies, budget data, gold ornaments, retirement planning, and general notes in a local SQLite database.

## Solution Structure

```
ToshanVault.slnx
├── src/ToshanVault.App      # WinUI 3 app (XAML pages, dialogs, view models, services)
├── src/ToshanVault.Core     # Domain models (DomainModels.cs), security/encryption
├── src/ToshanVault.Data     # SQLite repositories, schema migrations
├── tests/ToshanVault.Tests  # Unit tests (xUnit)
└── tools/                   # PowerShell scripts (publish, seed data, icons)
```

## Build, Test & Publish

```powershell
# Build
dotnet build ToshanVault.slnx -c Debug -p:Platform=x64 --nologo -v:q

# Test (requires build first)
dotnet test tests/ToshanVault.Tests/ToshanVault.Tests.csproj -c Debug -p:Platform=x64 --nologo --no-build

# Publish (single-file .exe)
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/publish-single.ps1
```

**Known warning**: NU1603 LiveChartsCore version mismatch (pre-existing, harmless).

## Database

- **Location**: Configured in `appsettings.json` → `Storage.DatabaseFilePath`
- **Dev path**: `C:\Work\ToshanVault\App\VaultDb\vault.db`
- **Fallback**: `%LOCALAPPDATA%\ToshanVault\vault.db`
- **Override**: `TOSHANVAULT_DATA_DIR` environment variable wins over config
- **Engine**: SQLite via `Microsoft.Data.Sqlite`

### Key Schemas

- `vault_entry` → `vault_field` (credentials, FK with `ON DELETE CASCADE`)
- `bank_account` → `bank_account_credential` (FK with `ON DELETE CASCADE`)
- `bank_account.vault_entry_id` → `vault_entry(id)` (FK with `ON DELETE SET NULL`)
- `insurance` → `insurance_credential` (FK with `ON DELETE CASCADE`)
- `budget_category` (type: Income/Fixed/Variable) → `budget_item` (frequency: Weekly/Fortnightly/Monthly/Quarterly/Yearly/OneOff)
- `gold_ornament`, `general_note`, `retirement_*` tables

### Direct DB Access (without sqlite3 CLI)

```powershell
Add-Type -Path "src\ToshanVault.Data\bin\x64\Debug\net10.0-windows10.0.26100.0\Microsoft.Data.Sqlite.dll"
$conn = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=App\VaultDb\vault.db")
$conn.Open()
$cmd = $conn.CreateCommand(); $cmd.CommandText = "SELECT * FROM vault_entry"; $reader = $cmd.ExecuteReader()
```

## App Pages (Navigation Order)

Dashboard → Budget → Bank Accounts → Vault → Retirement Planning → Gold Ornaments → Insurance → General Notes → Mint Investment → Settings → About

## Architecture Patterns

### Page Structure
Each feature has: `{Feature}Page.xaml` + `{Feature}Page.xaml.cs` + `{Feature}Dialogs.cs`
- Pages use code-behind (no MVVM framework) with inline ViewModels as `sealed class` at bottom of `.xaml.cs`
- Dialogs are built programmatically using `ContentDialog` in static helper classes
- Repositories are resolved via `AppHost.GetService<T>()` (DI)

### Card-Based UI
Vault, Banking, and Insurance use a card grid layout:
- `GridView` with `DataTemplate` inside a `Border` (card)
- 3-row Grid: Row 0 = title/subtitle, Row 1 = details, Row 2 = button bar
- Button bar: `StackPanel Orientation="Horizontal" Spacing="4"` with 32×32 icon buttons
- Standard buttons: Edit (✏️ `E70F`), Notes (📝 `E70B`), Credential avatars, Add Credential (➕ `E710`), Delete (🗑️ `E74D`)

### Website Hyperlinks
Website URLs are displayed as clickable `HyperlinkButton` elements in the card body (Row 1), NOT as buttons in the action bar. The handler uses `FrameworkElement` for sender type:
```csharp
private async void LaunchWebsite_Click(object sender, RoutedEventArgs e)
{
    if (sender is FrameworkElement el && el.Tag is string url && !string.IsNullOrWhiteSpace(url))
    {
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "http" || uri.Scheme == "https"))
            await Windows.System.Launcher.LaunchUriAsync(uri);
    }
}
```

### Visibility Bindings
Use `Visibility` properties (not `bool`) in ViewModels for conditional UI:
```csharp
public Visibility WebsiteVisibility { get; }  // Collapsed when null/empty
public Visibility CanAddCredentialVisibility { get; }
```

### Credential Owners
Multi-owner credential system: `KnownOwners` list in each credentials service. Each owner gets an avatar button showing their initial.

## Vault Dialog Focus

**CRITICAL**: The vault add/edit dialog (`VaultDialogs.cs`) MUST always focus the **Name** field, NOT Category. The Category field is a `ComboBox` with a "+" button to add new categories inline. The focus override must apply to BOTH add and edit dialogs using `DispatcherQueue.TryEnqueue` with `DispatcherQueuePriority.Low` in the `Loaded` handler. Do NOT wrap the focus logic in an `if (existing is null)` check — it must run unconditionally.

### WinUI ContentDialog Focus Pattern
```csharp
dialog.Loaded += (_, _) =>
{
    DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => nameField.Focus(FocusState.Programmatic));
};
```

## WinUI Gotchas

- **ToolTipService**: Use `ToolTipService.SetToolTip(button, "text")` static method. `Button.ToolTipService = { }` does NOT compile in WinUI 3.
- **ComboBox nullable**: `ComboBox.SelectedItem` can be null. Always use `(string?)_category.SelectedItem ?? ""`.
- **XAML compiler cache**: If the XAML compiler (`XamlCompiler.exe`) fails silently (exit code 1, no error messages), run `dotnet clean` then rebuild. Stale obj caches cause phantom failures.
- **HyperlinkButton in DataTemplate**: Use `Click` handler + `Tag` binding (not `NavigateUri`) to handle URLs that may lack `https://` prefix.

## Git Workflow

- Feature branches: `anvil/{task-id}` off `master`
- Publish always from `master` after merging
- Always push with `$env:GIT_TERMINAL_PROMPT=0` to avoid credential prompts in non-interactive shells
