using System.Text;
using System.Text.RegularExpressions;
using PromptPolisher.Core.Models;

namespace PromptPolisher.Core.Services;

public sealed record TemplateValidationResult(
    bool IsValid,
    bool HasUserPrompt,
    IReadOnlyList<string> UnknownPlaceholders,
    string Message);

/// <summary>
/// 把参数渲染进系统 Prompt 模板。占位符语法：{{name}}。
/// 只支持白名单占位符，未知占位符会在校验时报错，避免静默丢参数。
/// </summary>
public sealed class PromptTemplateRenderer
{
    private static readonly Regex PlaceholderRegex = new(@"\{\{\s*([a-zA-Z0-9_]+)\s*\}\}", RegexOptions.Compiled);

    private static readonly HashSet<string> KnownPlaceholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "user_prompt", "strength", "style", "language", "length",
        "preserve_terms", "format", "preserve_meaning", "extra_instructions"
    };

    public const string DefaultTemplate = """
你是一名资深 Prompt 工程师。请在不改变用户核心意图的前提下，把下面的“原始 Prompt”润色成更清晰、可执行、结构化的 Prompt。

【任务类型锁定】
只优化表达、结构和可执行性，不得改变原始 Prompt 的任务类型和交付物。
如果原始 Prompt 是“写软件/写代码/做设计/排障”，润色后仍然是这些任务；
不要擅自改成“写文档”“写方案”“做分析”。

【润色要求】
- 润色强度：{{strength}}
- 目标风格：{{style}}
- 输出语言：{{language}}
- 长度控制：{{length}}
- 是否保留原意：{{preserve_meaning}}
- 必须保留的术语：{{preserve_terms}}
- 额外要求：{{extra_instructions}}

【输出格式】
{{format}}

【原始 Prompt】
<<<
{{user_prompt}}
>>>

请严格按照【输出格式】的要求输出。若原始 Prompt 信息不足，请在不编造事实的前提下补全结构，并用 [待补充] 标注缺失信息。
""";

    private const string TaskTypeGuard = """
【任务类型锁定】
只优化表达、结构和可执行性，不得改变原始 Prompt 的任务类型和交付物。
如果原始 Prompt 是“写软件/写代码/做设计/排障”，润色后仍然是这些任务；
不要擅自改成“写文档”“写方案”“做分析”。

""";

    public TemplateValidationResult Validate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return new TemplateValidationResult(false, false, Array.Empty<string>(), "模板不能为空。");
        }

        var matches = PlaceholderRegex.Matches(template);
        var unknown = matches
            .Select(m => m.Groups[1].Value)
            .Where(name => !KnownPlaceholders.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var hasUserPrompt = matches.Any(m => string.Equals(m.Groups[1].Value, "user_prompt", StringComparison.OrdinalIgnoreCase));

        if (unknown.Count > 0)
        {
            return new TemplateValidationResult(
                false,
                hasUserPrompt,
                unknown,
                $"存在未知占位符：{string.Join(", ", unknown)}。可用占位符：{string.Join(", ", KnownPlaceholders.OrderBy(x => x))}。");
        }

        if (!hasUserPrompt)
        {
            return new TemplateValidationResult(
                false,
                false,
                Array.Empty<string>(),
                "模板必须包含 {{user_prompt}}，否则原始 Prompt 不会被发送。");
        }

        return new TemplateValidationResult(true, true, Array.Empty<string>(), "模板校验通过。");
    }

    public string Render(string template, PolisherRequest request)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            template = DefaultTemplate;
        }

        var values = BuildValues(request);
        var result = template;
        foreach (var (key, value) in values)
        {
            result = result.Replace("{{" + key + "}}", value, StringComparison.OrdinalIgnoreCase);
            result = result.Replace("{{ " + key + " }}", value, StringComparison.OrdinalIgnoreCase);
        }

        // 兼容用户已有的旧模板：即使模板里没有这段，也强制补上任务类型约束。
        if (!result.Contains("【任务类型锁定】", StringComparison.Ordinal))
        {
            result = TaskTypeGuard + result;
        }

        return result;
    }

    private static Dictionary<string, string> BuildValues(PolisherRequest request)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["user_prompt"] = request.UserPrompt,
            ["strength"] = StrengthText(request.Strength),
            ["style"] = StyleText(request.Style),
            ["language"] = LanguageText(request.Language),
            ["length"] = LengthText(request.Length),
            ["preserve_terms"] = string.IsNullOrWhiteSpace(request.PreserveTerms)
                ? "无"
                : $"以下术语必须原样保留，不得翻译或改写：{request.PreserveTerms.Trim()}",
            ["format"] = FormatText(request.Format),
            ["preserve_meaning"] = request.PreserveMeaning
                ? "是。不得改变用户的核心意图、事实、约束和关键信息。"
                : "否。允许在保持任务目标的前提下重写结构与措辞。",
            ["extra_instructions"] = string.IsNullOrWhiteSpace(request.ExtraInstructions)
                ? "无"
                : request.ExtraInstructions.Trim()
        };
    }

    public static string StrengthText(PolishStrength value) => value switch
    {
        PolishStrength.Light => "轻度。只修正表达不清、语法和结构问题，尽量保留原句。",
        PolishStrength.Standard => "标准。在保留原意的前提下，优化结构、措辞和可执行性。",
        PolishStrength.Deep => "深度。可以重组结构、补充必要上下文和约束，使 Prompt 更专业、更可执行。",
        _ => value.ToString()
    };

    public static string StyleText(PolishStyle value) => value switch
    {
        PolishStyle.General => "通用。清晰、自然，适合大多数对话模型。",
        PolishStyle.Rigorous => "严谨。术语准确、边界清楚，避免模糊表述。",
        PolishStyle.Concise => "简洁。去掉冗余，保留关键指令。",
        PolishStyle.Creative => "创意。允许更有表现力的措辞，但不牺牲可执行性。",
        PolishStyle.Technical => "技术/工程。术语准确、结构清晰、可复现；只改变表达方式，不得把“写软件/写代码”改成“写文档”。",
        PolishStyle.Marketing => "营销。突出价值主张、目标受众与行动号召。",
        PolishStyle.Academic => "学术。正式、客观，强调方法、证据与限制。",
        _ => value.ToString()
    };

    public static string LanguageText(OutputLanguage value) => value switch
    {
        OutputLanguage.KeepOriginal => "保持原始 Prompt 的主要语言。",
        OutputLanguage.Chinese => "输出中文。",
        OutputLanguage.English => "输出英文。",
        OutputLanguage.Other => "按原文判断语言；无法判断时使用中文。",
        _ => value.ToString()
    };

    public static string LengthText(LengthControl value) => value switch
    {
        LengthControl.Shorter => "缩短。在不丢失关键信息的前提下压缩。",
        LengthControl.Keep => "保持。长度与原始 Prompt 大致相当。",
        LengthControl.Expand => "适度扩写。补充必要的上下文、约束和输出格式。",
        _ => value.ToString()
    };

    public static string FormatText(OutputFormat value) => value switch
    {
        OutputFormat.ResultOnly => "仅输出润色后的 Prompt。不要输出解释、前言、修改说明或 Markdown 代码围栏。",
        OutputFormat.ResultWithNotes => "先输出【润色结果】，再输出【修改说明】。修改说明用 3–6 个要点列出关键改动。",
        OutputFormat.Markdown => "输出结构化 Markdown：一级标题“# 润色结果”，正文为润色后的 Prompt；如有修改说明，放在“## 修改说明”。",
        _ => value.ToString()
    };
}
