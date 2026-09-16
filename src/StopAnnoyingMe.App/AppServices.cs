using StopAnnoyingMe.Core;
using StopAnnoyingMe.Core.Data;
using StopAnnoyingMe.Core.Models;
using StopAnnoyingMe.Core.Services;

namespace StopAnnoyingMe.App;

/// <summary>把各視窗共用的物件包成一包傳遞，避免建構子參數失控。</summary>
internal sealed class AppServices
{
    public required AppPaths Paths { get; init; }

    public required SettingsService SettingsStore { get; init; }

    public required AppSettings Settings { get; set; }

    public required InterruptionRepository Repository { get; init; }

    public required InterruptionService Interruptions { get; init; }

    public required StatisticsService Statistics { get; init; }

    public void SaveSettings() => SettingsStore.Save(Settings);
}
