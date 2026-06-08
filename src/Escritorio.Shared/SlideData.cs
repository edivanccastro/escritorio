namespace Escritorio.Shared;

/// <summary>
/// Representacao neutra de um slide, usada pelos servicos de formato.
/// </summary>
public sealed class SlideData
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Background { get; set; } = "#C43E1C";
}
