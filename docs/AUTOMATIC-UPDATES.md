# Automatic updates

Installed Citrus releases check public GitHub releases at startup and every six hours. Downloads run in the background. An installer must match its published SHA-256, byte count and embedded version before it can run.

Updates apply when no conversion, file action, wheel drag or editor is active and the Citrus windows are closed or minimized. To apply a ready update sooner, open **Settings → Updates → Restart to update**. Unsaved edits and running conversions are never canceled to install an update.

The Updates section also offers **Check for updates**, a link to GitHub releases and an automatic-update preference. Turning automatic updates off leaves manual checks available. Development and portable builds do not replace themselves.

## Upgrading the first preview

Version 0.3.0-preview.1 does not contain an updater. Install the newer release once from [GitHub releases](https://github.com/Skytuhua/Citrus/releases). Quit Citrus from its tray menu before running that first upgrade. The installer preserves settings and original files.

Future updates use the accepted component terms when they are unchanged. If those terms change, install the release interactively to review them. A failed update restores the previous installation and does not repeatedly restart Citrus to retry the same failed version.

## What triggers an update

A newer **published application release** triggers an update. README edits, website deployments, media replacements, source commits and unpublished drafts do not replace the app. Preview builds can receive newer previews; stable releases do not automatically switch to previews.

The installer includes the offline engine pack, so automatic downloads remain substantial. Only a verified, receipted pending installer is retained; previous owned files are cleaned after a successful updated startup. Modified or unrelated files are preserved.

This remains an unsigned Windows preview. Automatic-update tests and package integrity checks are separate from clean-machine installation, physical Explorer workflows and application-performance acceptance.
