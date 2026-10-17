EmailFileViewer - portable edition
==================================

EmailFileViewer opens .eml and .msg email files and shows them safely on your
computer. Messages are parsed locally; nothing is uploaded unless you choose to.

This edition needs no installation, no administrator rights and no .NET.


What you need
-------------
* Windows 10 (version 1809 or later) or Windows 11, 64-bit.
* The Microsoft Edge WebView2 Runtime. It is part of Windows 11 and of up-to-date
  Windows 10. If it is missing, EmailFileViewer tells you so and shows where to
  download it, then quits:
      https://developer.microsoft.com/microsoft-edge/webview2/
  (The installer edition installs WebView2 for you; this portable edition does not.)


How to use it
-------------
1. Extract the WHOLE zip file into a folder you can write to, for example
   D:\Tools\EmailFileViewer. Do not run EmailFileViewer.exe from inside the zip.
2. Keep EmailFileViewer.exe, efv-sidecar.exe and EmailFileViewer.portable together
   in the same folder.
3. Start EmailFileViewer.exe. Open mail with the Open button, by dragging .eml /
   .msg files into the window, or by dragging them onto EmailFileViewer.exe.

To double-click mail files in Explorer, right-click a file, choose "Open with" ->
"Choose another app" and pick EmailFileViewer.exe. That is a normal Windows
setting; the portable edition does not register any file association itself.

If Windows shows "Windows protected your PC" (SmartScreen), click "More info" and
then "Run anyway". Builds that are not code-signed yet trigger this.


Where your data lives
---------------------
Everything is kept in the "data" folder next to EmailFileViewer.exe:

    data\settings.json   your language, light/dark mode and colour choices
    data\webview\        data of the web view that shows the messages
    data\logs\           log files

* Copy the whole folder (including "data") to another computer and your settings
  go with it.
* Delete the whole folder and nothing of EmailFileViewer is left behind. It writes
  nothing to the registry.
* Attachments you open are written to the Windows temporary folder and removed
  again when you quit EmailFileViewer.
* If the folder is not writable (for example it is in C:\Program Files, or on
  read-only media), EmailFileViewer still runs and uses the standard per-user
  folders of the installed edition instead:
      %APPDATA%\com.github.realzony.emailfileviewer
      %LOCALAPPDATA%\com.github.realzony.emailfileviewer
  Your settings are then kept there, not in "data".
* Deleting the file EmailFileViewer.portable switches this copy to those standard
  folders on purpose.


Running together with the installed edition
-------------------------------------------
The installed and the portable edition share one application identity, and only one
instance of it runs at a time. If one of them is running and you start the other (or
open another mail file), the new process hands the files over to the running one and
quits. The files then open in the instance that was started first, with that
instance's settings. Quit the running one first if you want to use the other.
