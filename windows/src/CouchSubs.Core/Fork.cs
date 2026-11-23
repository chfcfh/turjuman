// SPDX-License-Identifier: AGPL-3.0-only
//
// Modified version of CouchSubs (https://github.com/yorulmazsinan/couchsubs, Copyright (C) 2026 Sinan Yorulmaz).
// Changed by Faz3a in November 2026: Arabic target, Arabic interface, DeepSeek, offline English→Arabic model,
// packs/shared pool/usage data/updates removed. See FORK-NOTICE.md at the repository root.

namespace CouchSubs.Core;

/// <summary>
/// Everything that names this fork in one place. The owner chose «ترجمان» (Turjuman) on 2026-11-23. To rename, change
/// <see cref="Name"/> and <see cref="LatinName"/> here; Resources/ar.json uses "{app}" for the name, so the whole
/// interface follows.
/// </summary>
public static class Fork
{
    /// <summary>Name shown in the interface (Arabic).</summary>
    public const string Name = "ترجمان";

    /// <summary>Name in Latin letters (window class names, file names, logs).</summary>
    public const string LatinName = "Turjuman";

    /// <summary>
    /// Folder under %LOCALAPPDATA%. Kept independent of the display name so renaming the app does not lose the user's
    /// settings, and different from "CouchSubs" so this app never reads or writes the upstream app's data.
    /// </summary>
    public const string DataFolder = "Faz3aSubs";

    /// <summary>
    /// The upstream app updates itself from the CouchSubs GitHub releases. This fork has no release channel of its own
    /// yet, so self-update stays off; turning it on requires pointing the updaters at the fork's own releases first.
    /// </summary>
    public const bool UpdatesEnabled = false;

    /// <summary>Target language of every translation in this fork.</summary>
    public const string TargetLanguage = "ar";
}
