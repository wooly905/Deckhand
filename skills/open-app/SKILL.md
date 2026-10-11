---
name: open-app
description: Start an installed application by name. Use for open, start, launch, run an app or program. Not for web sites or URLs; use browser_open for those.
metadata:
  tools: "app_find app_launch app_refresh_index"
---
# When to use
The user wants to start a program. Not for opening a web site, even when the user names a browser ("open chrome and go to cnn"). For web sites use browser_open only.

# Steps
1. Run app_find with the app name from the request.
2. If there is exactly one match, run app_launch with its id.
3. If there is more than one match, use ask_user with the names.
4. If there is no match, run app_refresh_index, then app_find again.
5. If there is still no match, tell the user that the app is not installed.

# Example
User: open paint
Step: {"thought":"Find Paint","action":"app_find","args":{"name":"paint"}}
