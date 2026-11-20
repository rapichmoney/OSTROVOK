# Release policy

When a new version is ready, publish it to GitHub Releases after the relevant checks pass. The user has authorized this; do not leave a working version only in the local installation or on main.

Bump DesktopIsland.csproj, update RELEASE_NOTES.md, and push the checked changes to main. The release workflow checks both media adapters and the packaged application before publishing the portable ZIP and SHA-256. Verify the public release assets and report any failed or incomplete validation. Never overwrite an existing version: use a new version number for further changes.
