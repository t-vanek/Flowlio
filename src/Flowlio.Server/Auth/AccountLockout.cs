using Flowlio.Infrastructure.Identity;

namespace Flowlio.Server.Auth;

/// <summary>Shared helpers for classifying and presenting account-lockout state consistently across the
/// login pages, the authorize endpoint and the admin user list.</summary>
public static class AccountLockout
{
    /// <summary>A lockout reaching this far into the future is a permanent admin block, as opposed to a
    /// timed lock (a short admin lock or an automatic failed-attempt lockout).</summary>
    public static readonly DateTimeOffset BlockedThreshold = DateTimeOffset.MaxValue.AddDays(-1);

    /// <summary>Short-lived cookie carrying why an interactive session was just ended, so the login page
    /// can explain it even after the OIDC re-challenge drops any query string.</summary>
    public const string SessionEndedCookie = "flowlio_session_ended";

    /// <summary>The kind of lockout in effect, for messaging.</summary>
    public enum Kind { None, Blocked, AdminLock, SystemLock }

    public static bool IsBlocked(DateTimeOffset? lockoutEnd) =>
        lockoutEnd is { } end && end >= BlockedThreshold;

    /// <summary>Classifies an active lockout: a permanent admin block, a timed admin lock, or an
    /// automatic system lockout (the account locked itself after too many failed attempts).</summary>
    public static Kind Resolve(bool isLockedOut, LockoutReason reason, DateTimeOffset? lockoutEnd)
    {
        if (!isLockedOut)
            return Kind.None;
        if (reason == LockoutReason.AdminBlock || IsBlocked(lockoutEnd))
            return Kind.Blocked;
        if (reason == LockoutReason.AdminLock)
            return Kind.AdminLock;
        return Kind.SystemLock;
    }

    /// <summary>Message shown on a sign-in attempt rejected because the account is locked.</summary>
    public static string SignInMessage(Kind kind, DateTimeOffset? lockoutEnd) => kind switch
    {
        Kind.Blocked => "Váš účet byl zablokován administrátorem. Pro obnovení přístupu kontaktujte správce.",
        Kind.AdminLock => $"Účet byl dočasně uzamčen administrátorem{Until(lockoutEnd)}.",
        Kind.SystemLock => $"Účet byl z bezpečnostních důvodů automaticky uzamčen po příliš mnoha pokusech o přihlášení{Until(lockoutEnd)}.",
        _ => "Účet je uzamčen.",
    };

    /// <summary>Message shown when an active session was ended because the account became locked. No
    /// exact unlock time is available at that point (the user is anonymous again).</summary>
    public static string SessionEndedMessage(Kind kind) => kind switch
    {
        Kind.Blocked => "Byli jste odhlášeni, protože váš účet byl zablokován administrátorem. Pro obnovení přístupu kontaktujte správce.",
        Kind.AdminLock => "Byli jste odhlášeni, protože váš účet uzamkl administrátor. Zkuste to prosím později.",
        Kind.SystemLock => "Byli jste odhlášeni, protože váš účet byl z bezpečnostních důvodů uzamčen. Zkuste to prosím později.",
        _ => "Byli jste odhlášeni. Přihlaste se prosím znovu.",
    };

    private static string Until(DateTimeOffset? end) =>
        end is { } e && e < BlockedThreshold ? $" do {e.LocalDateTime:d.M.yyyy HH:mm}" : "";
}
