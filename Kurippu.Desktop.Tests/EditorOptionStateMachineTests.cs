using FsCheck;
using FsCheck.Fluent;
using Kurippu.Core;
using Kurippu.Desktop.ViewModels;

namespace Kurippu.Desktop.Tests;

public class EditorOptionStateMachineTests
{
    [Fact]
    public void LimitModeTransitionKeepsTheModelAndEditorInSync()
    {
        var failure = Run([new EditorOperation.SetLimitMode(true)]);

        Assert.Null(failure);
    }

    [Fact]
    public void EnablingCrfClearsTwoPass()
    {
        var failure = Run([
            new EditorOperation.SetLimitMode(true),
            new EditorOperation.SetCrfMode(true)
        ]);

        Assert.Null(failure);
    }

    [Fact]
    public void ModelValidationCatchesAStateThatViolatesItsInvariants()
    {
        var editor = new EditorViewModel(CreateMedia(copyCompatible: true));
        editor.Options.CrfMode = false;
        editor.Options.LimitMode = false;
        editor.Options.TwoPass = true;

        var failure = (EditorStateModel.Initial with { CrfMode = false, TwoPass = true }).Validate(editor);

        Assert.Equal("two-pass requires limit mode", failure);
    }

    // Scenario: A random business operation sequence leaves both the real
    // editor and the independent model in a valid, matching state.
    [Fact]
    public void RandomEditorOperationsMatchIndependentStateModel()
    {
        var property = Prop.ForAll(
            Arb.From(EditorOperationSequences.Generator),
            scenario =>
            {
                var failure = Run(scenario);
                return failure is null ? Prop.ToProperty(true) : Prop.Label(false, failure);
            });

        Check.One(
            Config.QuickThrowOnFailure
                .WithMaxTest(100)
                .WithStartSize(1)
                .WithEndSize(20)
                .WithReplay(42UL, 1UL),
            property);
    }

    private static string? Run(IReadOnlyList<EditorOperation> operations) =>
        Run(new EditorScenario(operations, CopyCompatible: true));

    private static string? Run(EditorScenario scenario)
    {
        var media = CreateMedia(scenario.CopyCompatible);
        var editor = new EditorViewModel(media);
        var model = EditorStateModel.Initial with { CanCopyAudio = scenario.CopyCompatible };

        for (var index = 0; index < scenario.Operations.Count; index++)
        {
            var operation = scenario.Operations[index];
            operation.Apply(editor);
            model = operation.Apply(model);

            var failure = model.Validate(editor);
            if (failure is not null)
                return $"Operation {index} ({operation}) failed: {failure}";
        }

        return null;
    }

    private static MediaInfo CreateMedia(bool copyCompatible) => new("/tmp/a.mkv", 60, 640, 480, 1,
        [new(1, "video", "h264", 640, 480)],
        [new(2, "audio", copyCompatible ? "aac" : "mp3", Channels: 2)],
        [new(3, "sub", "subrip")]);

    private sealed record EditorScenario(IReadOnlyList<EditorOperation> Operations, bool CopyCompatible);

    private abstract record EditorOperation
    {
        public abstract void Apply(EditorViewModel editor);
        public abstract EditorStateModel Apply(EditorStateModel model);

        public sealed record SetCrfMode(bool Enabled) : EditorOperation
        {
            public override void Apply(EditorViewModel editor) => editor.SetCrfMode(Enabled);

            public override EditorStateModel Apply(EditorStateModel model) => model with
            {
                CrfMode = Enabled,
                LimitMode = Enabled ? false : model.LimitMode,
                Limit = Enabled ? "0" : "",
                TwoPass = Enabled ? false : model.TwoPass
            };
        }

        public sealed record SetLimitMode(bool Enabled) : EditorOperation
        {
            public override void Apply(EditorViewModel editor) => editor.SetLimitMode(Enabled);

            public override EditorStateModel Apply(EditorStateModel model) => model with
            {
                CrfMode = Enabled ? false : model.CrfMode,
                LimitMode = Enabled,
                Limit = Enabled ? "" : model.CrfMode ? "0" : "",
                TwoPass = Enabled
            };
        }

        public sealed record SetTwoPass(bool Enabled) : EditorOperation
        {
            public override void Apply(EditorViewModel editor) => editor.SetTwoPass(Enabled);

            public override EditorStateModel Apply(EditorStateModel model) => model with
            {
                TwoPass = Enabled,
                CrfMode = Enabled ? false : model.CrfMode,
                LimitMode = Enabled ? true : model.LimitMode,
                Limit = Enabled ? "" : model.Limit
            };
        }

        public sealed record SelectAudioCodec(string Codec) : EditorOperation
        {
            public override void Apply(EditorViewModel editor) => editor.SelectAudioCodec(Codec);

            public override EditorStateModel Apply(EditorStateModel model)
            {
                var selected = string.Equals(Codec, "copy", StringComparison.Ordinal) && !model.CanCopyAudio
                    ? "aac" : Codec;
                return model with
                {
                    AudioCodec = selected,
                    AudioBitrate = string.Equals(selected, model.AudioCodec, StringComparison.Ordinal)
                        ? model.AudioBitrate : ""
                };
            }
        }

        public sealed record SetAudioBitrate(string Value) : EditorOperation
        {
            public override void Apply(EditorViewModel editor)
            {
                editor.Options.AudioBitrate = Value;
                editor.Refresh();
            }

            public override EditorStateModel Apply(EditorStateModel model) =>
                model with { AudioBitrate = Value };
        }

        public sealed record SetLimit(string Value) : EditorOperation
        {
            public override void Apply(EditorViewModel editor)
            {
                editor.Options.Limit = Value;
                editor.Refresh();
            }

            public override EditorStateModel Apply(EditorStateModel model) =>
                model with { Limit = Value };
        }

        public sealed record SetQuality(string Value) : EditorOperation
        {
            public override void Apply(EditorViewModel editor)
            {
                editor.Options.Quality = Value;
                editor.Refresh();
            }

            public override EditorStateModel Apply(EditorStateModel model) =>
                model with { Quality = Value };
        }

        public sealed record SetCrop(string Width, string Height, string X, string Y) : EditorOperation
        {
            public override void Apply(EditorViewModel editor)
            {
                editor.Options.CropWidth = Width;
                editor.Options.CropHeight = Height;
                editor.Options.CropX = X;
                editor.Options.CropY = Y;
                editor.Refresh();
            }

            public override EditorStateModel Apply(EditorStateModel model) => model with
            {
                CropWidth = Width,
                CropHeight = Height,
                CropX = X,
                CropY = Y
            };
        }

        public sealed record SetFragment(string Start, string End) : EditorOperation
        {
            public override void Apply(EditorViewModel editor)
            {
                editor.Options.Start = Start;
                editor.Options.End = End;
                editor.Refresh();
            }

            public override EditorStateModel Apply(EditorStateModel model) => model with
            {
                Start = Start,
                End = End
            };
        }
    }

    private sealed record EditorStateModel
    {
        public static EditorStateModel Initial { get; } = new();

        public bool CrfMode { get; init; } = true;
        public bool LimitMode { get; init; }
        public bool TwoPass { get; init; }
        public string Limit { get; init; } = "";
        public string AudioCodec { get; init; } = "aac";
        public string AudioBitrate { get; init; } = "";
        public string Quality { get; init; } = "";
        public bool CanCopyAudio { get; init; } = true;
        public string CropWidth { get; init; } = "";
        public string CropHeight { get; init; } = "";
        public string CropX { get; init; } = "";
        public string CropY { get; init; } = "";
        public string Start { get; init; } = "";
        public string End { get; init; } = "";

        public string? Validate(EditorViewModel editor)
        {
            var actual = editor.Options;
            return ValidateMirroredState(actual, editor.CopyAvailable) ??
                ValidateDerivedPlan(editor, actual);
        }

        private string? ValidateMirroredState(OptionInputs actual, bool copyAvailable)
        {
            if (actual.CrfMode != CrfMode) return "CRF mode diverged";
            if (actual.LimitMode != LimitMode) return "limit mode diverged";
            if (actual.TwoPass != TwoPass) return "two-pass mode diverged";
            if (!string.Equals(actual.Limit, Limit, StringComparison.Ordinal)) return "limit value diverged";
            if (!string.Equals(actual.AudioCodec, AudioCodec, StringComparison.Ordinal)) return "audio codec diverged";
            if (!string.Equals(actual.AudioBitrate, AudioBitrate, StringComparison.Ordinal)) return "audio bitrate diverged";
            if (!string.Equals(actual.Quality, Quality, StringComparison.Ordinal)) return "quality value diverged";
            if (!string.Equals(actual.CropWidth, CropWidth, StringComparison.Ordinal) ||
                !string.Equals(actual.CropHeight, CropHeight, StringComparison.Ordinal) ||
                !string.Equals(actual.CropX, CropX, StringComparison.Ordinal) ||
                !string.Equals(actual.CropY, CropY, StringComparison.Ordinal)) return "crop fields diverged";
            if (!string.Equals(actual.Start, Start, StringComparison.Ordinal) ||
                !string.Equals(actual.End, End, StringComparison.Ordinal)) return "fragment fields diverged";
            if (actual.CrfMode && actual.LimitMode) return "CRF and limit modes must be exclusive";
            if (actual.TwoPass && !actual.LimitMode) return "two-pass requires limit mode";
            if (actual.TwoPass && actual.CrfMode) return "two-pass requires non-CRF mode";
            if (string.Equals(actual.AudioCodec, "copy", StringComparison.Ordinal) && !copyAvailable)
                return "copy selected for incompatible audio";
            if (copyAvailable != CanCopyAudio) return "copy availability diverged";
            return null;
        }

        private static string? ValidateDerivedPlan(EditorViewModel editor, OptionInputs actual)
        {
            var expected = OptionEngine.Derive(CopyInputs(actual), editor.Media);
            if (editor.Plan.IsValid != expected.IsValid) return "plan validity is stale";
            if (!editor.Plan.Errors.SequenceEqual(expected.Errors)) return "plan errors are stale";
            if (!editor.Plan.Warnings.SequenceEqual(expected.Warnings, StringComparer.Ordinal))
                return "plan warnings are stale";
            if (!string.Equals(editor.Plan.Container, expected.Container, StringComparison.Ordinal))
                return "container is stale";
            if (editor.CanEncode != expected.IsValid) return "encode availability is stale";

            if (expected.IsValid)
            {
                var expectedArguments = FfmpegArguments.FormatRaw(FfmpegArguments.Build(expected.Options!));
                if (!string.Equals(editor.RawArguments, expectedArguments, StringComparison.Ordinal))
                    return "raw arguments are stale";
                if (editor.VisibleCrop != expected.Options!.Crop) return "visible crop is stale";
            }

            return null;
        }

        private static OptionInputs CopyInputs(OptionInputs source) => new()
        {
            Threads = source.Threads,
            CropWidth = source.CropWidth,
            CropHeight = source.CropHeight,
            CropX = source.CropX,
            CropY = source.CropY,
            ScaleWidth = source.ScaleWidth,
            ScaleHeight = source.ScaleHeight,
            Deinterlace = source.Deinterlace,
            FixSar = source.FixSar,
            BurnSubtitles = source.BurnSubtitles,
            SubtitleTrack = source.SubtitleTrack,
            SubtitlePath = source.SubtitlePath,
            VideoTrack = source.VideoTrack,
            AudioTrack = source.AudioTrack,
            HasAudio = source.HasAudio,
            FadeIn = source.FadeIn,
            FadeOut = source.FadeOut,
            Amplify = source.Amplify,
            Start = source.Start,
            End = source.End,
            VideoCodec = source.VideoCodec,
            AudioCodec = source.AudioCodec,
            Limit = source.Limit,
            Quality = source.Quality,
            AudioBitrate = source.AudioBitrate,
            TwoPass = source.TwoPass,
            LimitMode = source.LimitMode,
            CrfMode = source.CrfMode,
            Multithreading = source.Multithreading,
            Preset = source.Preset
        };
    }

    private static class EditorOperationSequences
    {
        private static readonly string[] Codecs = ["aac", "opus", "vorbis", "copy"];
        private static readonly string[] CropValues = ["", "320", "640", "321", "-1"];
        private static readonly string[] TimeValues = ["", "0", "5", "60", "61", "invalid"];
        private static readonly string[] LimitValues = ["", "19", "0", "-1"];
        private static readonly string[] QualityValues = ["", "0", "23", "51", "52", "-1"];
        private static readonly string[] AudioBitrateValues = ["", "128", "300", "-1"];

        public static Gen<EditorScenario> Generator =>
            from operations in Gen.Choose(0, 20).SelectMany(length =>
                Gen.ListOf(OperationGenerator, length).Select(items =>
                    (IReadOnlyList<EditorOperation>)items))
            from copyCompatible in Gen.Elements(true, false)
            select new EditorScenario(operations, copyCompatible);

        private static Gen<EditorOperation> OperationGenerator =>
            Gen.Frequency(
                (3, Gen.Choose(0, 1).Select(value =>
                    (EditorOperation)new EditorOperation.SetCrfMode(value == 1))),
                (3, Gen.Choose(0, 1).Select(value =>
                    (EditorOperation)new EditorOperation.SetLimitMode(value == 1))),
                (3, Gen.Choose(0, 1).Select(value =>
                    (EditorOperation)new EditorOperation.SetTwoPass(value == 1))),
                (3, Gen.Elements(Codecs).Select(codec =>
                    (EditorOperation)new EditorOperation.SelectAudioCodec(codec))),
                (2, Gen.Elements(AudioBitrateValues).Select(value =>
                    (EditorOperation)new EditorOperation.SetAudioBitrate(value))),
                (2, Gen.Elements(LimitValues).Select(value =>
                    (EditorOperation)new EditorOperation.SetLimit(value))),
                (2, Gen.Elements(QualityValues).Select(value =>
                    (EditorOperation)new EditorOperation.SetQuality(value))),
                (2, from width in Gen.Elements(CropValues)
                    from height in Gen.Elements(CropValues)
                    from x in Gen.Elements(CropValues)
                    from y in Gen.Elements(CropValues)
                    select (EditorOperation)new EditorOperation.SetCrop(width, height, x, y)),
                (2, from start in Gen.Elements(TimeValues)
                    from end in Gen.Elements(TimeValues)
                    select (EditorOperation)new EditorOperation.SetFragment(start, end)));
    }
}
