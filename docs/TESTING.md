# Testing notes

Device validation has been performed on an Android 11 ARM32 television with WebView 156.0.8078.47, using D-pad / OK / Back key events and visual checks. Voice search was additionally verified with the real microphone by the device owner.

Validated flows include desktop-page JavaScript episode selection, native player source discovery, native playback / seek / speed / audio controls, history retention and a temporary 20,000-visit history. History renders 50 native rows per page, rather than constructing all visits at once. Synthetic visits are never saved into the user's actual session.

Version 0.8.0 device checks also cover empty starter favorites, the video-found notification, exit confirmation with Continue focused first, cancellation while playing / already paused, and confirmed return to the website. The source export includes its own full set of checks and a manual Android-build workflow; a hosted workflow run has not yet been performed.

The filter adapter supports a conservative subset of static uBO-style URL / CSS rules. It does not implement all uBO scriptlets, procedural selectors, request rewriting or extension APIs. Twitch clean-playlist fallbacks are best effort; real ad removal is not guaranteed. Availability of websites, media URLs, codecs, speech services and DRM varies by device and provider.

Automated checks cover state migration, history expiry / persistence, URL handling, public suffix logic, filters, remote navigation, media discovery and English localization. Device checks remain necessary for focus, keyboard, playback and permission behavior.
