// SPDX-License-Identifier: AGPL-3.0-only

using System.IO;
using System.Net.Http;
using CouchSubs.Core;
using CouchSubs.Shell;
using Velopack;
using Velopack.Sources;

namespace CouchSubs.App.Services;

/// <summary>
/// Updates from the fork's GitHub releases (Velopack; <see cref="Fork.Repository"/>). Faz3a fork: a found update is
/// downloaded in the background by itself and installed when the app closes; "Update now" installs it at once. Copies
/// made with the Velopack setup file or portable zip update themselves; any other copy only links to the releases page.
/// </summary>
internal sealed class Updater : IUpdater
{
    private readonly UpdateManager manager = new(new GithubSource(Fork.Repository, null, false));
    private UpdateInfo? pending;
    private Task? download;
    private bool applyOnExit;
    private readonly GitHubReleaseNotes notes = new(new HttpClient
        { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 2 * 1024 * 1024 });

    public bool CanUpdateItself => manager.IsInstalled;
    public Uri DownloadPage { get; } = new(Fork.Repository + "/releases/latest");
    public UpdateAttemptJournal? Attempts { get; } =
        new(UpdateAttemptJournal.DefaultPath, AppVersion.Current(typeof(Updater).Assembly));

    /// <summary>Velopack writes Update.exe's log one folder above the app (velopack.log).</summary>
    public string? UpdateLogPath =>
        manager.IsInstalled ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "velopack.log")) : null;

    public Task<IReadOnlyList<ReleaseNoteRun>> ReleaseNotesAsync(string version) => notes.LoadAsync(version);

    public async Task<string?> CheckAsync()
    {
        if (!manager.IsInstalled) return await LatestTagAsync();
        pending = await manager.CheckForUpdatesAsync();
        if (pending is { } found) StartBackgroundDownload(found);
        return pending?.TargetFullRelease.Version.ToString();
    }

    /// <summary>
    /// Faz3a fork, automatic update: the new version is downloaded quietly; once it is on disk, Velopack is asked to
    /// install it after this process exits (silently, without reopening the app), so the next start is the new version.
    /// </summary>
    private void StartBackgroundDownload(UpdateInfo found)
    {
        lock (manager)
        {
            if (download is { IsFaulted: false }) return;
            download = Task.Run(async () =>
            {
                await manager.DownloadUpdatesAsync(found).ConfigureAwait(false);
                SubtitleLog.Write($"Güncelleme indirildi: {found.TargetFullRelease.Version}; uygulama kapanınca kurulacak");
                lock (manager)
                {
                    if (applyOnExit) return;
                    applyOnExit = true;
                }
                AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                {
                    try { manager.WaitExitThenApplyUpdates(found.TargetFullRelease, silent: true, restart: false); }
                    catch (Exception error) { StartupGuard.Log("Kapanışta güncelleme başlatılamadı", error); }
                };
            });
            _ = download.ContinueWith(task => StartupGuard.Log("Güncelleme arka planda indirilemedi", task.Exception!),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
    }

    public async Task InstallAsync(IProgress<double> progress, Action? beforeRestart = null)
    {
        if (pending is null) return;
        // The background download may be running; a second download of the same files is not started meanwhile.
        if (download is { } running)
        {
            try { await running.ConfigureAwait(false); } catch (Exception) { /* downloaded again below */ }
        }
        await manager.DownloadUpdatesAsync(pending, percent => progress.Report(percent / 100.0));
        beforeRestart?.Invoke();
        // Update.exe kurulum klasörünü değiştirir; açık kalan yardımcı süreç klasörü kilitlerse güncelleme uygulanamaz.
        var running2 = ChildProcessGuard.StopAllForUpdate(TimeSpan.FromSeconds(5));
        if (running2 > 0)
        {
            var message = Localizer.Current["The update was postponed because CouchSubs couldn't confirm that all helper processes have exited. Restart CouchSubs and try again."];
            SubtitleLog.Write($"Güncelleme ertelendi: {running2} yardımcı süreç kapanışı doğrulanamadı; " +
                              $"{pending.TargetFullRelease.Version} sürümü uygulanmadı.");
            SubtitleLog.Flush();
            throw new InvalidOperationException(message);
        }
        SubtitleLog.Write($"Güncelleme uygulanıyor: {pending.TargetFullRelease.Version}");
        SubtitleLog.Flush();
        lock (manager) applyOnExit = true; // restarting now; nothing left to install on exit
        manager.ApplyUpdatesAndRestart(pending);
    }

    /// <summary>For a copy that cannot update itself: the latest release's tag on GitHub, if it is newer.</summary>
    private static async Task<string?> LatestTagAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(Fork.LatinName);
            using var document = System.Text.Json.JsonDocument.Parse(
                await http.GetStringAsync($"https://api.github.com/repos/{Fork.RepositoryPath}/releases/latest"));
            var tag = document.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v');
            return tag is not null && AppVersion.IsNewer(tag, AppVersion.Current(typeof(Updater).Assembly)) ? tag : null;
        }
        // Network errors go up: the "Check now" button then says the check failed instead of "you're up to date".
        catch (Exception error) when (error is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}
