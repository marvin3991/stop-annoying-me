namespace StopAnnoyingMe.Core.Models;

/// <summary>一筆「被打擾」記錄。時間一律以本地時間儲存。</summary>
/// <param name="Id">資料庫主鍵。</param>
/// <param name="OccurredAt">被打擾的當下時間（本地時間，精確到秒）。</param>
/// <param name="Source">可選標籤，例如「同事」。未標記時為 null。</param>
/// <param name="Note">可選備註。</param>
/// <param name="CreatedAt">寫入資料庫的時間。</param>
public sealed record Interruption(
    long Id,
    DateTime OccurredAt,
    string? Source,
    string? Note,
    DateTime CreatedAt);
