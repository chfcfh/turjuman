# Modified version of CouchSubs

This program is a **modified version of CouchSubs**
(https://github.com/yorulmazsinan/couchsubs), Copyright (C) 2026 Sinan Yorulmaz, licensed under the
GNU AGPL-3.0 (see [LICENSE](LICENSE) and [NOTICE](NOTICE)).

**Modified by Faz3a, starting on 2026-11-23.** This modified version is called «ترجمان» (Turjuman). It is a separate
program from Faz3a's own app.

## What was changed (Windows app, `windows/`)

- **Arabic target.** Every translation goes English → Arabic (`Fork.TargetLanguage`). The AI prompt
  (`AiTranslator.cs`) asks for clear Modern Standard Arabic in the style of Arabic game localizations; DeepL targets `AR`.
- **Offline English → Arabic model.** `OnnxMarianEngine.cs` and `ArabicOfflineTranslator.cs` run
  opus-mt-tc-big-en-ar (Helsinki-NLP / OPUS-MT, CC BY 4.0) in an ONNX int8 conversion made by Faz3a. It replaces the
  English → Turkish CTranslate2 model. The model (~370 MB) is not in git: it is copied next to the app from the folder
  named by `ArabicModelDir` in the git-ignored `windows/Directory.Build.local.props`, or found in
  `models\opus-mt-en-ar` next to the exe / in `%LOCALAPPDATA%\Faz3aSubs\models`.
- **DeepSeek** added as a translation engine with the user's own key (`AiProvider.DeepSeek`, OpenAI-compatible
  `https://api.deepseek.com/chat/completions`, thinking mode off; models `deepseek-flash` and `deepseek-v4-pro`).
- **Arabic interface** with right-to-left layout; Arabic subtitles are drawn right to left in the subtitle bar.
- **Translations over the original text** (`InPlaceTextWindow.cs`, `CaptureTarget.cs`): on a click-through layer over
  the window being read, the subtitle covers the game's subtitle and other translations sit on their own text.
- **All text on the screen** (`LiveScreen.cs`, `LiveScreenBoard.cs`): while the layer is on, the whole frame is read
  again and again with Windows' text reader and every text block gets one translation over it, kept until its text is
  gone from several reads. The upstream on-screen text, mission and notice readers are off meanwhile, so no text is
  translated twice.
- **Removed / switched off:** game packs, the shared line pool (no subtitle line is ever uploaded), usage statistics,
  pack downloads, updates from the upstream CouchSubs releases, and the ad band / pop-up ads for the upstream
  publisher's other apps.
- **Updates** come from this fork's own releases (github.com/chfcfh/turjuman, Velopack): downloaded in the background
  and installed when the app closes; Settings › Basics has "Check for updates now". New versions are published with
  `scripts/release-turjuman.ps1`.
- **Defaults** for PC games: the picture is read from a game window on this PC and the subtitles are shown in the
  subtitle bar on the PC screen.
- Data folder `%LOCALAPPDATA%\Faz3aSubs` (never the upstream app's `CouchSubs` folder); executable `Faz3aSubs.exe`.

The subtitle area presets, the subtitle stabilizer and the single subtitle bar are unchanged from CouchSubs.

## Source code

Under the AGPL-3.0, anyone who receives this program must be able to get its complete source code. This modified
version's source is public at https://github.com/chfcfh/turjuman (linked from the About window).
