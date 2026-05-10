namespace AiDocumentAssistant.Api.Services;

public sealed class KnowledgeEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Type { get; init; } = "memory";
    public string Title { get; init; } = "";
    public string Content { get; init; } = "";
    public List<string> Tags { get; init; } = [];
    public Guid? SourceDocumentId { get; init; }
    public string? SourceFileName { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

public sealed class TeachRequest
{
    public string Title { get; init; } = "";
    public string Content { get; init; } = "";
    public List<string> Tags { get; init; } = [];
}

public sealed class KnowledgeAskRequest
{
    public string Question { get; init; } = "";
    public Guid? DocumentId { get; init; }
}

public sealed class FeedbackRequest
{
    public string Question { get; init; } = "";
    public string CorrectAnswer { get; init; } = "";
    public Guid? DocumentId { get; init; }
}

public sealed class KnowledgeAnswer
{
    public string Answer { get; init; } = "";
    public List<KnowledgeSource> Sources { get; init; } = [];
}

public sealed class KnowledgeSource
{
    public Guid Id { get; init; }
    public string Type { get; init; } = "";
    public string Title { get; init; } = "";
    public string Preview { get; init; } = "";
    public string? SourceFileName { get; init; }
}
