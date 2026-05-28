namespace Flowlio.Server.Auth;

/// <summary>Shared helpers for presenting account-lockout state consistently across the login pages,
/// the authorize endpoint and the admin user list.</summary>
public static class AccountLockout
{
    /// <summary>A lockout reaching this far into the future is a permanent admin block, as opposed to a
    /// timed lock (a failed-attempt lockout or a short admin lock).</summary>
    public static readonly DateTimeOffset BlockedThreshold = DateTimeOffset.MaxValue.AddDays(-1);

    public static bool IsBlocked(DateTimeOffset? lockoutEnd) =>
        lockoutEnd is { } end && end >= BlockedThreshold;

    /// <summary>User-facing explanation for a locked-out sign-in attempt: a permanent admin block vs a
    /// timed lock (whether from too many failed attempts or a short admin lock).</summary>
    public static string SignInMessage(DateTimeOffset? lockoutEnd) =>
        IsBlocked(lockoutEnd)
            ? "Váš účet byl zablokován administrátorem. Pro obnovení přístupu kontaktujte správce."
            : lockoutEnd is { } end
                ? $"Účet je dočasně uzamčen do {end.LocalDateTime:d.M.yyyy HH:mm}. Zkuste to prosím znovu později."
                : "Účet je dočasně uzamčen. Zkuste to prosím znovu později.";
}
