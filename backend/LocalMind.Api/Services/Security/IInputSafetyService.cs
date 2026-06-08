namespace LocalMind.Api.Services.Security;

public interface IInputSafetyService
{
    void ValidateChatMessage(string message);
    string RedactSensitiveData(string value);
}
