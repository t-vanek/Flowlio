using Microsoft.FluentUI.AspNetCore.Components;

namespace Flowlio.Client.Services;

/// <summary>Ergonomic wrappers over <see cref="IDialogService"/> for the common confirm-then-act flow.</summary>
public static class DialogHelpers
{
    /// <summary>Shows a confirmation dialog and returns true when the user confirms (did not cancel).
    /// Typical use: <c>if (!await Dialogs.ConfirmAsync(message, "Smazat", "Smazání")) return;</c></summary>
    public static async Task<bool> ConfirmAsync(this IDialogService dialogs,
        string message, string confirmLabel, string title, string cancelLabel = "Zrušit")
    {
        var dialog = await dialogs.ShowConfirmationAsync(message, confirmLabel, cancelLabel, title);
        return !(await dialog.Result).Cancelled;
    }
}
