## Download

Download `FileSearch-<version>-win-x64.zip` below, unzip it anywhere (for example `C:\Tools\FileSearch`) and run `FileSearch.exe`. No installer and no .NET install are needed. Windows 10 or 11, x64.

To add **Search with File Search** to the Explorer right-click menu, open the app and choose **Tools > Add to Explorer context menu**. On Windows 11 it appears under **Show more options**.

**Windows SmartScreen:** the exe is not code-signed yet, so Windows may show "Windows protected your PC" the first time. Click **More info > Run anyway**. You can check the download against the `.sha256` file:

```powershell
Get-FileHash .\FileSearch-<version>-win-x64.zip -Algorithm SHA256
```

New to File Search? The zip includes `USER-MANUAL.md`, also readable [online](https://github.com/vladano/win-file-search/blob/main/docs/USER-MANUAL.md).

Found a bug? [Open an issue](https://github.com/vladano/win-file-search/issues/new/choose) and attach `%AppData%\FileSearch\logs\filesearch.log`.
