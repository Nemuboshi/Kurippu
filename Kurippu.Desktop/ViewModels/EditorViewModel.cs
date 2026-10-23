using System.ComponentModel;
using System.Runtime.CompilerServices;
using Kurippu.Core;

namespace Kurippu.Desktop.ViewModels;

public sealed class EditorViewModel : INotifyPropertyChanged
{
    private string lastAutoArguments;
    private CropRect? lastVisibleCrop;
    public string RawArguments { get; set; }
    public MediaInfo Media { get; }
    public OptionInputs Options { get; }
    public EncodingPlan Plan { get; private set; }
    public EditorViewModel(MediaInfo media)
    {
        Media = media;
        Options = new OptionInputs { HasAudio = media.AudioTracks.Count > 0 };
        Plan = OptionEngine.Derive(Options, media);
        lastAutoArguments = Plan.IsValid ? FfmpegArguments.FormatRaw(FfmpegArguments.Build(Plan.Options!)) : "";
        RawArguments = lastAutoArguments;
    }
    public IReadOnlyList<FieldError> Errors => Plan.Errors;
    public bool CanEncode => Plan.IsValid;
    public bool CopyAvailable => Options.AudioTrack >= 0 && Options.AudioTrack < Media.AudioTracks.Count &&
        Media.AudioTracks[Options.AudioTrack].Codec is "aac" or "vorbis" or "opus";
    public CropRect? VisibleCrop => Plan.Options?.Crop ?? lastVisibleCrop;
    public string Container => Plan.Container;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetCrfMode(bool enabled)
    {
        Options.CrfMode = enabled;
        if (enabled)
        {
            Options.LimitMode = false;
            Options.TwoPass = false;
        }
        Options.Limit = enabled ? "0" : "";
        Refresh();
    }

    public void SetLimitMode(bool enabled)
    {
        Options.LimitMode = enabled;
        if (enabled) Options.CrfMode = false;
        Options.Limit = Options.CrfMode ? "0" : "";
        Options.TwoPass = enabled;
        Refresh();
    }

    public void SetTwoPass(bool enabled)
    {
        Options.TwoPass = enabled;
        if (enabled)
        {
            Options.CrfMode = false;
            Options.LimitMode = true;
            Options.Limit = "";
        }
        Refresh();
    }

    public void SelectAudioCodec(string codec)
    {
        if (string.Equals(codec, "copy", StringComparison.Ordinal) && !CopyAvailable) codec = "aac";
        if (!string.Equals(codec, Options.AudioCodec, StringComparison.Ordinal)) Options.AudioBitrate = "";
        Options.AudioCodec = codec;
        Refresh();
    }

    public void Refresh()
    {
        Plan = OptionEngine.Derive(Options, Media);
        if (Plan.IsValid) lastVisibleCrop = Plan.Options?.Crop;
        else if (string.IsNullOrWhiteSpace(Options.CropWidth) && string.IsNullOrWhiteSpace(Options.CropHeight) &&
            string.IsNullOrWhiteSpace(Options.CropX) && string.IsNullOrWhiteSpace(Options.CropY)) lastVisibleCrop = null;
        if (Plan.IsValid)
        {
            var generated = FfmpegArguments.FormatRaw(FfmpegArguments.Build(Plan.Options!));
            if (!string.Equals(generated, lastAutoArguments, StringComparison.Ordinal))
            {
                RawArguments = generated;
                lastAutoArguments = generated;
                Changed(nameof(RawArguments));
            }
        }
        Changed(nameof(Plan));
        Changed(nameof(Errors));
        Changed(nameof(CanEncode));
        Changed(nameof(Container));
    }

    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
