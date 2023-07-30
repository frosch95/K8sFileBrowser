using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Serilog;

namespace K8sFileBrowser;

public static class ApplicationHelper
{
    public static async Task<string?> SaveFile(string? initialFolder, string? initialFile)
    {
        Window? ret;
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow is not { } wnd) return null;
        try
        {
            var filter = new List<FilePickerFileType> { new("All files") { Patterns = new List<string> { "*" } } };

            var startLocation = initialFolder != null
                ? await wnd.StorageProvider.TryGetFolderFromPathAsync(initialFolder)
                : null;
            var extension = initialFile != null ? Path.GetExtension(initialFile).TrimStart('.') : null;
            var fileName = initialFile != null ? Path.GetFileNameWithoutExtension(initialFile) : null;

            var file = await wnd.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                SuggestedStartLocation = startLocation,
                DefaultExtension = extension,
                ShowOverwritePrompt = true,
                SuggestedFileName = fileName,
                FileTypeChoices = filter
            });

            if (file != null)
            {
                return file.Path.LocalPath;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error saving file");
        }
        return null;
    }
}