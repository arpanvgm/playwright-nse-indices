# Uninstall Guide

Steps to remove everything installed on your machine for this project.

---

## 1. Remove Playwright browser binaries

Run this command to uninstall all browsers downloaded by Playwright:

```powershell
playwright uninstall --all
```

This removes Chromium, Firefox, WebKit, FFmpeg and Winldd (~400 MB) from:
`C:\Users\<you>\AppData\Local\ms-playwright`

---

## 2. Uninstall Microsoft Edge

Edge is a system component on Windows and is generally not removable via normal means. If you installed it specifically for this project on a non-Windows machine:

```powershell
winget uninstall Microsoft.Edge
```

> On Windows 10/11, Edge cannot be fully uninstalled as it is built into the OS.

---

## Verify everything is clean

```powershell
# Should return False if Playwright binaries are removed
Test-Path "$env:LOCALAPPDATA\ms-playwright"
```