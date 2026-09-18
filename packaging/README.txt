RO3 Asia Japanese Patch
=======================

Installation / Update
---------------------
1. Extract this ZIP to any folder.
2. Run Install-Japanese.bat.
3. Enter the RO3 Client folder containing ro3.exe, or drag ro3.exe/the folder onto the BAT file.
4. When [OK] is shown, launch RO3 normally.

Running the installer again over an older version is supported. Existing patch files are overwritten with the files from the new release while the original BepInEx ownership marker is preserved.

Automatic Update
----------------
1. Close RO3.
2. Run Update-Japanese.bat from any previously extracted release package.
3. Enter the RO3 Client folder containing ro3.exe, or drag ro3.exe/the folder onto the BAT file.
4. The updater checks GitHub Releases, downloads the latest release ZIP directly, verifies it when GitHub provides a SHA-256 digest, extracts it to a temporary folder, and runs the latest installer over the existing local patch.

The downloaded ZIP and temporary extraction are removed after the update. You can keep using the Update-Japanese.bat from an older extracted package because it always discovers the current latest GitHub Release first.

Included
--------
- Working BepInEx runtime required by the patch
- XUnity AutoTranslator / ResourceRedirector
- Generated Japanese translation dictionaries
- Japanese TextMeshPro fallback font asset

Notes
-----
- Close RO3 before installing/updating for the most reliable result.
- Translation source TSV files and development scripts are maintained in the GitHub repository and are not required for normal installation.
