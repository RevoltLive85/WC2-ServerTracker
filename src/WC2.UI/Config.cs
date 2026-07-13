namespace WC2.UI;

public sealed class UiFileConfig
{
    public float HudRefreshSeconds { get; set; } = 0.5f;
    public string AccentColor { get; set; } = "#ffd35c";
    public string BossBarColor { get; set; } = "#ff4b3a";
    public bool ShowKillStreaks { get; set; } = true;
    public int KillStreakMinimum { get; set; } = 3;

    /// <summary>Full-screen welcome page (your hosted HTML wrapper) shown once per map on
    /// first spawn. MUST be https. Empty string disables the panel entirely.</summary>
    public string WelcomeUrl { get; set; } = "";
    /// <summary>How long the welcome panel stays on screen before auto-closing (seconds).</summary>
    public float WelcomeDurationSeconds { get; set; } = 8f;

    // ── Welcome text panel (CenterHtml, shown once per connection on first spawn) ──
    public bool WelcomeEnabled { get; set; } = true;
    public string WelcomeServerName { get; set; } = "ServerTracker WC2 RPG server";
    public string WelcomeSubtitle { get; set; } = "Welcome to our server!";
    public string WelcomeDiscord { get; set; } = "discord.gg/xHa27KZTmT";
    public string WelcomeWebsite { get; set; } = "wc2.servertracker.live";
    public string WelcomeFooter { get; set; } = "Have fun! Enjoy your stay!";

    // ── Audio/visual flourishes ──
    /// <summary>Sound played to everyone when a boss (region, invasion, or finale) spawns.
    /// Stock CS2 sound event name; if it doesn't play, try an alternate here — a wrong
    /// name just fails silently (a benign engine warning), it can't crash anything.</summary>
    // ── Audio/visual flourishes ──
    /// <summary>Sound played to everyone when a boss (region, invasion, or finale) spawns.
    /// Real file at csgo/sounds/wc2/boss_horn.mp3 — path given WITHOUT extension, matching
    /// the "play &lt;relative path under sounds/&gt;" convention. Guessed stock engine names
    /// (e.g. "Music.MVPAnthem") do NOT work here — confirmed via ResourceSystem file-not-found
    /// errors; CS2's "play" command wants a real file path, not a soundevent identifier.</summary>
    public string BossSpawnSound { get; set; } = "sounds/music/cs_stinger.vsnd_c";
    /// <summary>Sound played to a player when they level up. Real file at
    /// csgo/sounds/wc2/levelup.mp3 — same convention as BossSpawnSound.</summary>
    public string LevelUpSound { get; set; } = "sounds/ui/xp_levelup.vsnd_c";
    /// <summary>Broadcast to all chat when a player finds Legendary-rarity loot.</summary>
    public bool AnnounceLegendaryLoot { get; set; } = true;
    /// <summary>Custom flavored killfeed lines instead of the plain default.</summary>
    public bool FlavoredKillfeed { get; set; } = true;

    public static UiFileConfig Default() => new();
}
