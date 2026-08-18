# Lucent

A browser that is only a browser. Tabs, an address bar, and an ad blocker built
specifically for YouTube and Twitch. No sync, no accounts, no sidebar, no AI, no
extensions, no telemetry.

## Install

1. Download the latest `Lucent-x.y.z.w-win-x64.zip` from
   [Releases](../../releases/latest).
2. Unzip it anywhere you like, such as your Desktop or a folder in your user
   directory.
3. Run `Lucent.exe`.

Windows will say **"Windows protected your PC"** and name an unknown publisher.
That is what Windows says about any program without a paid code-signing
certificate, which this does not have. Click **More info**, then **Run anyway**.

**Do not put it in `Program Files`.** Updates replace the executable in place, and
that folder needs administrator rights to write to.

The first launch takes a little longer than later ones while the app unpacks
itself.

### Requirements

Windows 10 or 11, and the Microsoft WebView2 runtime, which is already part of
Windows 11 and most Windows 10 installs. If it is missing, Lucent will say so on
startup and offer to open the download page.

You do **not** need to install .NET. It is bundled.

## Updates

Lucent checks for a new version when it starts. If there is one, a bar appears
under the address bar:

> Lucent 0.0.0.2 Is Available &nbsp;&nbsp; **Install** &nbsp; Later

Clicking **Install** downloads the new version, checks it against the SHA-256
published in that release's notes, swaps it in and restarts. Nothing installs on
its own, and a download that does not match its checksum is discarded rather than
run.

## What It Blocks

**YouTube**: video ads, including the "Ad blockers are not allowed" interstitial.
Ads come from the same servers as the video itself, so this works by removing the
part of the page data that tells the player an ad exists. Nothing to skip, because
nothing is ever requested. Shorts and the Playables games shelf are hidden from
feeds; the Shorts tab still works if you go to it directly.

**Twitch**: ads stitched into the live stream. Expect a brief quality dip across a
break, because the stream is swapped for an ad-free variant and swapped back, and
that seam is inherent to how it works. This is the layer most likely to break when
Twitch changes something.

**Everywhere**: a curated list of ad and tracking domains, refused before the
request leaves your machine. The number in the toolbar counts them for the current
tab.

## Your Data

Cookies, logins and cache live in `%LOCALAPPDATA%\Lucent\WebView2` on your own
machine and go nowhere else. There are no accounts and no telemetry. Lucent makes
no network request of its own except the update check against GitHub.

Signing into a Google account here is comparable to signing into any other
Chromium browser. Google sometimes refuses sign-in from embedded browsers
("this browser or app may not be secure"); that is Google's check, and it is not
worked around.

## Shortcuts

| Key | Action |
| --- | --- |
| `Ctrl+T` | New Tab |
| `Ctrl+W` | Close Tab |
| `Ctrl+L` | Focus Address Bar |
| `F5` | Reload |
| `F12` | Dev Tools |

These work when the browser chrome has focus. Click the page first and the page
takes the keyboard.

## Reporting A Problem

Open an [issue](../../issues) and include the version number shown at the
right-hand end of the toolbar.
