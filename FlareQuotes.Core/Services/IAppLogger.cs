namespace FlareQuotes.Core.Services;

public interface IAppLogger
{
    string LogFilePath { get; }

    void Info(string message);
    void Warning(string message);
    void Error(Exception exception, string message);
}