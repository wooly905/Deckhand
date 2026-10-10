---
name: change-wallpaper
description: Change the desktop wallpaper to an image file. Use for wallpaper, desktop background, background picture.
metadata:
  tools: "wallpaper_get wallpaper_set fs_find_files"
---
# When to use
The user wants a different desktop background picture.

# Steps
1. If the request has a full file path, run wallpaper_set with that path.
2. If the request names a file but no folder, run fs_find_files with the file name, then wallpaper_set with the found path.
3. If more than one file matches, use ask_user with the paths.
4. Tell the user that the wallpaper is changed and that Undo can restore the previous one.

# Example
User: set my wallpaper to C:\Pictures\beach.jpg
Step: {"thought":"Path is given","action":"wallpaper_set","args":{"path":"C:\\Pictures\\beach.jpg","style":"fill"}}
