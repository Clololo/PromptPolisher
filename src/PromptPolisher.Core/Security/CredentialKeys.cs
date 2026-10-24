namespace PromptPolisher.Core.Security;

public static class CredentialKeys
{
    public static string ApiKey(string providerId) => $"provider:{providerId}:apiKey";
    public static string Session(string providerId) => $"provider:{providerId}:session";
}
