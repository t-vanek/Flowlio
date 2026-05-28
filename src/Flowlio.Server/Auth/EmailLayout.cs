namespace Flowlio.Server.Auth;

/// <summary>Wraps the body of a transactional e-mail in a consistent branded HTML shell so every
/// message (invitations, e-mail confirmation, security notices) looks the same.</summary>
public static class EmailLayout
{
    public static string Wrap(string innerHtml) => $"""
        <!DOCTYPE html>
        <html lang="cs">
        <body style="margin:0;background:#f1f5f9;font-family:'Segoe UI',Helvetica,Arial,sans-serif;">
          <div style="max-width:480px;margin:0 auto;padding:24px;">
            <div style="font-size:20px;font-weight:800;color:#2563eb;margin-bottom:16px;">&#8355; Flowlio</div>
            <div style="background:#ffffff;border-radius:12px;padding:24px;color:#0f172a;line-height:1.55;">
              {innerHtml}
            </div>
            <p style="color:#94a3b8;font-size:12px;margin-top:16px;">Tento e-mail odeslala aplikace Flowlio. Neodpovídejte na něj.</p>
          </div>
        </body>
        </html>
        """;
}
