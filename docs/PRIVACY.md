# Privacy notes

Rbrowser stores favorites, tab metadata, preferences and browsing history in private app-local storage. History retention is 30 days by default and can be changed to 7, 90 or 365 days. Reducing the retention period immediately removes visits outside it. The app does not implement a cloud account, cloud sync or a separate analytics backend.

Visited websites, their embedded content and media hosts receive normal browser / playback requests. Cookies and DOM storage are enabled for website compatibility. Native playback passes the user agent and relevant referrer / origin; cookies are obtained separately for each requested resource URI.

Bundled ad filters work offline. Manually updating filters contacts the official upstream list servers. Voice input uses the installed system recognition service, which may process audio over its own network service. Microphone access requires the Android permission.

Android automatic app backup is disabled. This is a browser with local history, not an anonymity tool. Website privacy behavior and the installed speech service are controlled by their respective providers.
