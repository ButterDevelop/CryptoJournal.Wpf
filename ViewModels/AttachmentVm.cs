using CommunityToolkit.Mvvm.ComponentModel;
using CryptoJournal.Wpf.Storage.Attachments;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CryptoJournal.Wpf.ViewModels;

public sealed partial class AttachmentVm : ObservableObject
{
    public string Filename { get; }

    [ObservableProperty]
    private string _fullPath;

    public ImageSource? ImageSource { get; }

    public AttachmentVm(string filename, ILocalImageStore imageStore)
    {
        Filename  = filename;
        _fullPath = imageStore.GetImagePath(filename);

        if (File.Exists(_fullPath))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad; // Releases file lock immediately
                bmp.UriSource   = new Uri(_fullPath);
                bmp.EndInit();
                bmp.Freeze();
                ImageSource = bmp;
            }
            catch { /* best effort */ }
        }
    }
}
