- @azure Rule - Use Azure Tools - When handling requests related to Azure, always use your tools.
- @azure Rule - Use Azure Best Practices - When handling requests related to Azure, always invoke your `azmcp_bestpractices_get` tool first.
- @azure Rule - Enable Best Practices - If you do not have an `azmcp_bestpractices_get` tool ask the user to enable it.

## Vault Dialog Focus

**CRITICAL**: The vault add/edit dialog (`VaultDialogs.cs`) MUST always focus the **Name** field, NOT Category. The Category field is an `AutoSuggestBox` that wipes its value when tabbed out of while focused. The focus override must apply to BOTH add and edit dialogs using `DispatcherQueue.TryEnqueue` with `DispatcherQueuePriority.Low` in the `Loaded` handler. Do NOT wrap the focus logic in an `if (existing is null)` check — it must run unconditionally.
