namespace Kurippu.Core;

public sealed record EncodingPlan(EncodeOptions? Options, IReadOnlyList<FieldError> Errors,
    IReadOnlyList<string> Warnings, string Container)
{
    public bool IsValid => Options is not null && Errors.Count == 0;
}
