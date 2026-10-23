using Kurippu.Core;

namespace Kurippu.Desktop.Media;

public sealed class CropChangedEventArgs(CropRect? crop) : EventArgs
{
    public CropRect? Crop { get; } = crop;
}
