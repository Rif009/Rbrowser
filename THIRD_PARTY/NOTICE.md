# Third-party data

Filter snapshots downloaded 2026-11-09. Original headers and copyright notices are preserved in each file.

| Files under Rbrowser/Resources/Raw/filters | Origin | License |
| --- | --- | --- |
| ublock-ads.txt, ublock-privacy.txt, ublock-unbreak.txt, ublock-quick-fixes.txt | https://github.com/uBlockOrigin/uAssets ; https://ublockorigin.github.io/uAssets/filters/ | GPL-3.0; full text in uAssets-GPL-3.0.txt |
| easylist.txt | https://easylist.to/easylist/easylist.txt | Dual GPL-3.0 / CC BY-SA-3.0 per https://easylist.to/pages/licence.html ; distributed here under GPL-3.0, text in uAssets-GPL-3.0.txt |
| ruadlist.txt | https://github.com/easylist/ruadlist/tree/master/advblock | CC BY 3.0 https://creativecommons.org/licenses/by/3.0/ ; merged official Classic source sections, attribution preserved in header. Akuma (DemonSat), denis-ovs, Lain_13, dimisa and contributors. |
| public_suffix_list.dat | https://publicsuffix.org/list/public_suffix_list.dat | MPL-2.0; full text in MPL-2.0.txt |

Snapshots are unmodified data; Rbrowser's conservative parser is independently written. Use of these lists does not imply affiliation with uBlock Origin, EasyList or Mozilla. The source snapshots are included with this repository and packaged APK. No uBlock Origin extension code or branding is included.

`compiled-v2.bin` is a machine-readable preparation of these lists for faster startup; the original text lists remain packaged. `rbrowser-fixes.txt` contains independently written, Vost-scoped fixes based on observed ad endpoints and RU AdList patterns.

MAUI and .NET/Android dependencies are restored from their upstream NuGet packages and retain their respective license metadata.

The native video player uses AndroidX Media3 / ExoPlayer 1.11.1 (HLS, DASH and UI), Copyright The Android Open Source Project, Apache-2.0: https://github.com/androidx/media . Microsoft .NET Android bindings are MIT, Copyright .NET Foundation Contributors: https://github.com/dotnet/android-libraries . Full notices are bundled in `Media3-THIRD-PARTY-NOTICES.txt` and `Media3-Bindings-MIT.md`.

`Resources/Raw/twitch/twitch-adblock.js` is independently written. Its alternate Twitch player-token / clean-HLS approach was informed by the publicly documented MIT-licensed TwitchAdSolutions project: https://github.com/pixeltris/TwitchAdSolutions (pixeltris and contributors). It does not bundle VAFT, its player-reload hooks, ad-segment stripping, or third-party proxies. Twitch is a trademark of its respective owner; no affiliation is implied.
