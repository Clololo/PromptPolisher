namespace PromptPolisher.Core.Models;

public enum PolishStrength
{
    Light,
    Standard,
    Deep
}

public enum PolishStyle
{
    General,
    Rigorous,
    Concise,
    Creative,
    Technical,
    Marketing,
    Academic
}

public enum OutputLanguage
{
    KeepOriginal,
    Chinese,
    English,
    Other
}

public enum LengthControl
{
    Shorter,
    Keep,
    Expand
}

public enum OutputFormat
{
    ResultOnly,
    ResultWithNotes,
    Markdown
}

public enum ProviderKind
{
    Api,
    Web,
    Manual
}

public enum HistoryStatus
{
    Success,
    Failed,
    Cancelled
}

public enum WebSessionStatus
{
    Unknown,
    Ready,
    NotLoggedIn
}
